using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using ChefKnifeStudios.MartaJazz.Shared.Geospatial;
using ChefKnifeStudios.MartaJazz.Shared.GtfsData;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker;

public class Worker(
    IHttpClientFactory httpClientFactory,
    ILogger<Worker> logger,
    ITransitHubPublisher transitHubPublisher,
    IEventNotificationService eventNotifications,
    LogEventWorker logEventWorker,
    ILoggingService loggingService,
    IEnumerable<ITransitCity> cities) : BackgroundService
{
    readonly Dictionary<string, ConcurrentDictionary<string, VehicleState>> _vehicleStateCaches = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, ulong?> _lastFeedHeaderTimestamps = new(StringComparer.OrdinalIgnoreCase);
    readonly string _batchOutputDir = Path.Combine(AppContext.BaseDirectory, "event-batches");
    static readonly JsonSerializerOptions _batchJsonOptions = new() { WriteIndented = true };

    Dictionary<string, IReadOnlyDictionary<string, RoutePoint[]>> _routeIndex = new(StringComparer.OrdinalIgnoreCase);
    // per-city routeId→TransitMode, built from GTFS route_type at static-data load time
    Dictionary<string, IReadOnlyDictionary<string, TransitMode>> _routeMode = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TransitDataWorker started.");

        await transitHubPublisher.StartAsync(stoppingToken);
        await InitializeRouteIndexAsync(stoppingToken);

        _ = Task.Run(() => PruneStaleVehicleStatesAsync(stoppingToken), stoppingToken);
        _ = Task.Run(() => RefreshRouteIndexAsync(stoppingToken), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var city in cities)
            {
                try
                {
                    var feed = await city.FetchVehiclesAsync(stoppingToken);

                    if (!_routeIndex.TryGetValue(city.Name, out var index) || index == null)
                    {
                        logger.LogWarning("City {City}: route index not ready, skipping tick.", city.Name);
                        continue;
                    }

                    _routeMode.TryGetValue(city.Name, out var modeMap);
                    if (feed.Entities.Count > 0)
                        await ProcessSpatialReconciliationAsync(city, feed, index, modeMap, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "City {City} tick failed; other cities unaffected", city.Name);
                }
            }
        }
    }

    // Partition a flat list of shapes into per-city indexes using RouteShapeProperties.City.
    // A single HTTP call to /gtfs/routes/shapes returns all cities (INV-S2, Q4).
    (Dictionary<string, IReadOnlyDictionary<string, RoutePoint[]>> index,
     Dictionary<string, IReadOnlyDictionary<string, TransitMode>> mode)
        BuildRouteIndex(List<RouteShapeFeature> shapes)
    {
        var perCityPoints = new Dictionary<string, Dictionary<string, List<RoutePoint>>>(StringComparer.OrdinalIgnoreCase);
        var perCityMode = new Dictionary<string, Dictionary<string, TransitMode>>(StringComparer.OrdinalIgnoreCase);

        foreach (var shape in shapes)
        {
            var cityName = shape.Properties.City ?? "marta";

            if (!perCityPoints.TryGetValue(cityName, out var routeGroups))
                perCityPoints[cityName] = routeGroups = new Dictionary<string, List<RoutePoint>>();
            if (!perCityMode.TryGetValue(cityName, out var modeMap))
                perCityMode[cityName] = modeMap = new Dictionary<string, TransitMode>();

            var key = shape.Properties.RouteShortName ?? shape.Properties.RouteId;
            if (!routeGroups.TryGetValue(key, out var points))
                routeGroups[key] = points = new List<RoutePoint>();

            foreach (var coord in shape.Geometry.Coordinates)
                points.Add(new RoutePoint(key, coord[1], coord[0]));

            modeMap[key] = shape.Properties.Mode;
        }

        var indexResult = perCityPoints.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyDictionary<string, RoutePoint[]>)kvp.Value.ToDictionary(
                r => r.Key, r => r.Value.ToArray()),
            StringComparer.OrdinalIgnoreCase);

        var modeResult = perCityMode.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyDictionary<string, TransitMode>)kvp.Value,
            StringComparer.OrdinalIgnoreCase);

        return (indexResult, modeResult);
    }

    async Task InitializeRouteIndexAsync(CancellationToken ct)
    {
        int maxRetries = 5;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var client = httpClientFactory.CreateClient("RouteShapeApi");
                var response = await client.GetAsync("/gtfs/routes/shapes", ct);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                var shapes = JsonSerializer.Deserialize<List<RouteShapeFeature>>(json, JsonOptions.Get());

                if (shapes == null || shapes.Count == 0)
                {
                    logger.LogWarning("Route shapes endpoint returned empty list. Retrying...");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                    continue;
                }

                (_routeIndex, _routeMode) = BuildRouteIndex(shapes);
                sw.Stop();

                int totalRoutes = _routeIndex.Values.Sum(d => d.Count);
                int totalPoints = _routeIndex.Values.Sum(d => d.Values.Sum(pts => pts.Length));
                logger.LogInformation("Built route index: {Cities} cities, {RouteCount} routes, {TotalPoints} total points in {ElapsedMs}ms",
                    _routeIndex.Count, totalRoutes, totalPoints, sw.ElapsedMilliseconds);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize route index (attempt {Attempt}/{MaxRetries}).", attempt, maxRetries);
                if (attempt < maxRetries)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }
        }

        logger.LogWarning("Could not initialize route index after {MaxRetries} attempts. V2 reconciliation will be skipped until index is built.", maxRetries);
    }

    ConcurrentDictionary<string, VehicleState> GetVehicleCache(string city)
    {
        if (!_vehicleStateCaches.TryGetValue(city, out var cache))
            _vehicleStateCaches[city] = cache = new ConcurrentDictionary<string, VehicleState>();
        return cache;
    }

    async Task ProcessSpatialReconciliationAsync(
        ITransitCity city,
        FeedMessage feed,
        IReadOnlyDictionary<string, RoutePoint[]> index,
        IReadOnlyDictionary<string, TransitMode>? modeMap,
        CancellationToken ct)
    {
        try
        {
            var vehicleStateCache = GetVehicleCache(city.Name);
            var cycleId = Guid.NewGuid().ToString("N");
            var cycleStart = DateTime.UtcNow;

            var batch = new List<RouteNearestPointBatchEvent.RouteNearestPointRecord>();
            var debugBatch = new List<BatchDebugRecord>();
            int movedCount = 0, unchangedCount = 0, stationaryCount = 0, staleCount = 0, skippedNoRouteId = 0, skippedUnknownRoute = 0;
            var activeRouteIdSet = new HashSet<string>();
            var activeVehicleIdSet = new HashSet<string>();

            foreach (var entity in feed.Entities)
            {
                try
                {
                    if (entity.Vehicle?.Position == null) continue;

                    string vehicleId = entity.Vehicle.Vehicle?.Id ?? entity.Id;
                    string? routeId = entity.Vehicle.Trip?.RouteId;

                    if (string.IsNullOrEmpty(routeId))
                    {
                        skippedNoRouteId++;
                        continue;
                    }

                    if (!index.TryGetValue(routeId, out var routePoints))
                    {
                        skippedUnknownRoute++;
                        continue;
                    }

                    activeRouteIdSet.Add(routeId);
                    activeVehicleIdSet.Add(vehicleId);

                    double lat = (double)entity.Vehicle.Position.Latitude;
                    double lon = (double)entity.Vehicle.Position.Longitude;
                    var now = DateTime.UtcNow;

                    const int SnapWindowSize = 30;

                    var snap = vehicleStateCache.TryGetValue(vehicleId, out var priorForSnap) && priorForSnap.RouteId == routeId
                        ? RouteSnapper.FindNearestInWindow(lat, lon, routePoints, priorForSnap.SnapIndex, SnapWindowSize)
                        : RouteSnapper.FindNearest(lat, lon, routePoints);
                    if (snap == null) continue;

                    var snapValue = snap.Value;
                    var nearest = snapValue.Point;
                    string outcome;
                    BatchDebugRecord debugRecord;

                    var currentVehicleTimestamp = entity.Vehicle.Timestamp;
                    bool isStale = false;

                    if (vehicleStateCache.TryGetValue(vehicleId, out var prior))
                    {
                        if (prior.LastUpdated > now)
                            continue;

                        isStale = currentVehicleTimestamp.HasValue
                            && prior.VehicleTimestamp.HasValue
                            && currentVehicleTimestamp.Value == prior.VehicleTimestamp.Value;

                        batch.Add(new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                            vehicleId,
                            nearest.RouteId,
                            prior.NearestLat,
                            prior.NearestLon,
                            prior.LastUpdated,
                            nearest.Lat,
                            nearest.Lon,
                            now,
                            entity.Vehicle.Position.Speed,
                            entity.Vehicle.Position.Bearing,
                            isStale,
                            modeMap != null && modeMap.TryGetValue(routeId, out var m) ? m : TransitMode.Bus
                        ));

                        if (isStale)
                        {
                            outcome = "Stale";
                            staleCount++;
                        }
                        else if (prior.NearestLat != nearest.Lat || prior.NearestLon != nearest.Lon)
                        {
                            outcome = "Moved";
                            movedCount++;
                        }
                        else if ((entity.Vehicle.Position.Speed ?? 0f) == 0f)
                        {
                            outcome = "Stationary";
                            stationaryCount++;
                        }
                        else
                        {
                            outcome = "Unchanged";
                            unchangedCount++;
                        }

                        var posDeltaKm = HaversineCalculator.DistanceKm(prior.NearestLat, prior.NearestLon, nearest.Lat, nearest.Lon);
                        var timeDeltaSec = (now - prior.LastUpdated).TotalSeconds;
                        double? currentSpeed = entity.Vehicle.Position.Speed.HasValue ? (double)entity.Vehicle.Position.Speed.Value : null;
                        double? currentBearing = entity.Vehicle.Position.Bearing.HasValue ? (double)entity.Vehicle.Position.Bearing.Value : null;
                        double? priorSpeed = prior.SpeedMetersPerSec.HasValue ? (double)prior.SpeedMetersPerSec.Value : null;
                        double? priorBearing = prior.Bearing.HasValue ? (double)prior.Bearing.Value : null;

                        debugRecord = new BatchDebugRecord(
                            VehicleId: vehicleId,
                            RouteId: nearest.RouteId,
                            Outcome: outcome,
                            RawLat: lat,
                            RawLon: lon,
                            SnappedLat: nearest.Lat,
                            SnappedLon: nearest.Lon,
                            SnapDistanceKm: snapValue.DistanceKm,
                            SnapIndex: snapValue.Index,
                            RoutePointCount: routePoints.Length,
                            PriorRawLat: prior.LastRawLat,
                            PriorRawLon: prior.LastRawLon,
                            PriorSnappedLat: prior.NearestLat,
                            PriorSnappedLon: prior.NearestLon,
                            PriorSnapDistanceKm: prior.LastSnapDistanceKm,
                            PriorRouteId: prior.RouteId,
                            PriorObservationUtc: prior.LastUpdated,
                            ObservationUtc: now,
                            DeltaFromPriorSnapKm: posDeltaKm,
                            DeltaFromPriorRawKm: HaversineCalculator.DistanceKm(prior.LastRawLat, prior.LastRawLon, lat, lon),
                            SecondsSincePriorObservation: timeDeltaSec,
                            SpeedMetersPerSec: entity.Vehicle.Position.Speed,
                            Bearing: entity.Vehicle.Position.Bearing
                        );

                        if (!isStale && city.EmitsTelemetry)
                        {
                            eventNotifications.PostEvent(this, new LerpEventArgs
                            {
                                CycleId = cycleId,
                                ObservationUtc = now,
                                VehicleId = vehicleId,
                                PriorRouteId = prior.RouteId,
                                PriorSnappedLat = prior.NearestLat,
                                PriorSnappedLon = prior.NearestLon,
                                PriorObservationUtc = prior.LastUpdated,
                                PriorSpeedMps = priorSpeed,
                                PriorBearingDeg = priorBearing,
                                PosDeltaKm = posDeltaKm,
                                SpeedDelta = currentSpeed.HasValue && priorSpeed.HasValue ? currentSpeed.Value - priorSpeed.Value : null,
                                BearingDelta = currentBearing.HasValue && priorBearing.HasValue ? currentBearing.Value - priorBearing.Value : null,
                                TimeDeltaSec = timeDeltaSec
                            });
                        }
                    }
                    else
                    {
                        batch.Add(new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                            vehicleId,
                            nearest.RouteId,
                            nearest.Lat,
                            nearest.Lon,
                            now,
                            nearest.Lat,
                            nearest.Lon,
                            now,
                            entity.Vehicle.Position.Speed,
                            entity.Vehicle.Position.Bearing,
                            false,
                            modeMap != null && modeMap.TryGetValue(routeId, out var m2) ? m2 : TransitMode.Bus
                        ));
                        outcome = "FirstObservation";
                        movedCount++;

                        debugRecord = new BatchDebugRecord(
                            VehicleId: vehicleId,
                            RouteId: nearest.RouteId,
                            Outcome: outcome,
                            RawLat: lat,
                            RawLon: lon,
                            SnappedLat: nearest.Lat,
                            SnappedLon: nearest.Lon,
                            SnapDistanceKm: snapValue.DistanceKm,
                            SnapIndex: snapValue.Index,
                            RoutePointCount: routePoints.Length,
                            PriorRawLat: null,
                            PriorRawLon: null,
                            PriorSnappedLat: null,
                            PriorSnappedLon: null,
                            PriorSnapDistanceKm: null,
                            PriorRouteId: null,
                            PriorObservationUtc: null,
                            ObservationUtc: now,
                            DeltaFromPriorSnapKm: null,
                            DeltaFromPriorRawKm: null,
                            SecondsSincePriorObservation: null,
                            SpeedMetersPerSec: entity.Vehicle.Position.Speed,
                            Bearing: entity.Vehicle.Position.Bearing
                        );
                    }

                    debugBatch.Add(debugRecord);

                    if (city.EmitsTelemetry)
                    {
                        eventNotifications.PostEvent(this, new SnapEventArgs
                        {
                            CycleId = cycleId,
                            ObservationUtc = now,
                            VehicleId = vehicleId,
                            RouteId = nearest.RouteId,
                            SnapOutcome = outcome,
                            RawLat = lat,
                            RawLon = lon,
                            SnappedLat = nearest.Lat,
                            SnappedLon = nearest.Lon,
                            SnapDistanceKm = snapValue.DistanceKm,
                            SnapIndex = snapValue.Index,
                            RoutePointCount = routePoints.Length,
                            SpeedMps = entity.Vehicle.Position.Speed.HasValue ? (double)entity.Vehicle.Position.Speed.Value : null,
                            BearingDeg = entity.Vehicle.Position.Bearing.HasValue ? (double)entity.Vehicle.Position.Bearing.Value : null,
                            IsStale = isStale
                        });
                    }

                    if (!isStale)
                    {
                        vehicleStateCache[vehicleId] = new VehicleState(
                            nearest.Lat,
                            nearest.Lon,
                            now,
                            nearest.RouteId,
                            entity.Vehicle.Position.Speed,
                            entity.Vehicle.Position.Bearing,
                            snapValue.DistanceKm,
                            lat,
                            lon,
                            snapValue.Index,
                            currentVehicleTimestamp);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing spatial reconciliation for entity {EntityId}.", entity.Id);
                }
            }

            var feedTs = feed.Header?.Timestamp;
            _lastFeedHeaderTimestamps.TryGetValue(city.Name, out var lastTs);
            var feedIsDuplicate = feedTs.HasValue && lastTs.HasValue && feedTs.Value == lastTs.Value;
            _lastFeedHeaderTimestamps[city.Name] = feedTs;

            var cycleEnd = DateTime.UtcNow;

            logger.LogInformation(
                "City {City} spatial reconciliation: {Moved} moved, {Unchanged} unchanged, {Stationary} stationary, {Stale} stale, {SkippedNoRouteId} skippedNoRouteId, {SkippedUnknownRoute} skippedUnknownRoute. FeedHeaderTs={FeedHeaderTs} DuplicateFeed={DuplicateFeed}",
                city.Name, movedCount, unchangedCount, stationaryCount, staleCount, skippedNoRouteId, skippedUnknownRoute, feedTs, feedIsDuplicate);

            if (city.EmitsTelemetry)
            {
                var droppedRecords = logEventWorker.DroppedRecords;
                var persistFailures = loggingService.PersistFailures;
                var bufferOccupancy = logEventWorker.BufferOccupancy;

                logger.LogInformation(
                    "Sidecar self-health: BufferOccupancy={Occupancy}, DroppedRecords={Dropped}, PersistFailures={Failures}",
                    bufferOccupancy, droppedRecords, persistFailures);

                eventNotifications.PostEvent(this, new CycleEventArgs
                {
                    CycleId = cycleId,
                    CycleStartUtc = cycleStart,
                    CycleEndUtc = cycleEnd,
                    CycleExecutionSeconds = (cycleEnd - cycleStart).TotalSeconds,
                    BusesProcessed = movedCount + unchangedCount + stationaryCount + staleCount,
                    BusesMoved = movedCount,
                    BusesUnchanged = unchangedCount,
                    BusesStationary = stationaryCount,
                    BusesStale = staleCount,
                    BusesSkippedNoRouteId = skippedNoRouteId,
                    BusesSkippedUnknownRoute = skippedUnknownRoute,
                    FeedHeaderTs = feedTs.HasValue ? (long)feedTs.Value : null,
                    DuplicateFeed = feedIsDuplicate,
                    ActiveRouteIds = string.Join(",", activeRouteIdSet.Order()),
                    ActiveVehicleIds = string.Join(",", activeVehicleIdSet.Order()),
                    LastUpdateCacheSize = 0,
                    VehicleStateCacheSize = vehicleStateCache.Count,
                    SidecarBufferOccupancy = bufferOccupancy,
                    SidecarDroppedRecords = droppedRecords,
                    SidecarPersistFailures = persistFailures
                });
            }

            if (batch.Count > 0)
            {
#if DEBUG
                await WriteBatchToDiskAsync(debugBatch, ct);
#endif
                var envelope = new EventEnvelope(
                    nameof(RouteNearestPointBatchEvent),
                    DateTimeOffset.UtcNow,
                    new RouteNearestPointBatchEvent(batch)
                );

                var isBatchPublished = await transitHubPublisher.PublishBatchAsync(city.Name, new List<EventEnvelope> { envelope }, ct);
                if (!isBatchPublished)
                    logger.LogWarning("Failed to publish spatial reconciliation batch for city {City}.", city.Name);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in spatial reconciliation for city {City}.", city.Name);
        }
    }


    async Task PruneStaleVehicleStatesAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                int pruned = 0;
                var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(20);

                foreach (var cache in _vehicleStateCaches.Values)
                {
                    foreach (var kvp in cache)
                    {
                        if (kvp.Value.LastUpdated < cutoff && cache.TryRemove(kvp.Key, out _))
                            pruned++;
                    }
                }

                logger.LogInformation("Pruned {PrunedCount} stale vehicle states.", pruned);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error pruning stale vehicle states.");
            }
        }
    }

    async Task RefreshRouteIndexAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var client = httpClientFactory.CreateClient("RouteShapeApi");
                var response = await client.GetAsync("/gtfs/routes/shapes", ct);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                var shapes = JsonSerializer.Deserialize<List<RouteShapeFeature>>(json, JsonOptions.Get());

                if (shapes == null || shapes.Count == 0)
                {
                    logger.LogWarning("Route shapes refresh returned empty list. Retaining existing index.");
                    continue;
                }

                (_routeIndex, _routeMode) = BuildRouteIndex(shapes);
                logger.LogInformation("Refreshed route index: {Cities} cities.", _routeIndex.Count);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to refresh route index. Retaining existing index.");
            }
        }
    }

#if DEBUG
    async Task WriteBatchToDiskAsync(List<BatchDebugRecord> batch, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_batchOutputDir);
            var fileName = $"route-nearest-point-batch_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.json";
            var filePath = Path.Combine(_batchOutputDir, fileName);

            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, batch, _batchJsonOptions, ct);

            logger.LogInformation("Wrote spatial reconciliation batch to {FilePath} ({Count} records).", filePath, batch.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write spatial reconciliation batch to disk.");
        }
    }
#endif
}
