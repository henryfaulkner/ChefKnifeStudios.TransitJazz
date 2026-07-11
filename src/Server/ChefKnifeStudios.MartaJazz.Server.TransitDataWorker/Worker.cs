using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Checkpoints;
using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using ChefKnifeStudios.MartaJazz.Shared.Geospatial;
using ChefKnifeStudios.MartaJazz.Shared.GtfsData;
using ChefKnifeStudios.MartaJazz.Shared.Models;
using ChefKnifeStudios.MartaJazz.Shared.Services;
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
    IEnumerable<ITransitCity> cities,
    ITriggerPointGenerator triggerPointGenerator) : BackgroundService
{
    readonly Dictionary<string, ConcurrentDictionary<string, VehicleState>> _vehicleStateCaches = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, ulong?> _lastFeedHeaderTimestamps = new(StringComparer.OrdinalIgnoreCase);
    readonly string _batchOutputDir = Path.Combine(AppContext.BaseDirectory, "event-batches");
    static readonly JsonSerializerOptions _batchJsonOptions = new() { WriteIndented = true };

    Dictionary<string, IReadOnlyDictionary<string, RoutePoint[]>> _routeIndex = new(StringComparer.OrdinalIgnoreCase);
    // per-city routeId→TransitMode, built from GTFS route_type at static-data load time
    Dictionary<string, IReadOnlyDictionary<string, TransitMode>> _routeMode = new(StringComparer.OrdinalIgnoreCase);
    // per-city routeId→cumulative-distance array (parallel to _routeIndex)
    Dictionary<string, IReadOnlyDictionary<string, double[]>> _routeCumDist = new(StringComparer.OrdinalIgnoreCase);
    // per-city routeId→trigger points (built from shared TriggerPointGenerator)
    Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<TriggerPoint>>> _routeTriggerPoints = new(StringComparer.OrdinalIgnoreCase);
    // per-city vehicleId→crossing baseline (mirrors _vehicleStateCaches key structure)
    readonly Dictionary<string, Dictionary<string, CrossingBaseline?>> _crossingBaselines = new(StringComparer.OrdinalIgnoreCase);

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
     Dictionary<string, IReadOnlyDictionary<string, TransitMode>> mode,
     Dictionary<string, IReadOnlyDictionary<string, double[]>> cumDist,
     Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<TriggerPoint>>> triggerPoints)
        BuildRouteIndex(List<RouteShapeFeature> shapes)
    {
        var perCityPoints = new Dictionary<string, Dictionary<string, List<RoutePoint>>>(StringComparer.OrdinalIgnoreCase);
        var perCityMode = new Dictionary<string, Dictionary<string, TransitMode>>(StringComparer.OrdinalIgnoreCase);
        // also need per-city per-route raw coords for cumDist + triggerPoint build
        var perCityCoords = new Dictionary<string, Dictionary<string, List<double[]>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var shape in shapes)
        {
            var cityName = shape.Properties.City ?? CityNames.Marta;

            if (!perCityPoints.TryGetValue(cityName, out var routeGroups))
                perCityPoints[cityName] = routeGroups = new Dictionary<string, List<RoutePoint>>();
            if (!perCityMode.TryGetValue(cityName, out var modeMap))
                perCityMode[cityName] = modeMap = new Dictionary<string, TransitMode>();
            if (!perCityCoords.TryGetValue(cityName, out var coordGroups))
                perCityCoords[cityName] = coordGroups = new Dictionary<string, List<double[]>>();

            // Primary key is the display identifier (short name when available).
            // GTFS-RT feeds can send either route_id or route_short_name depending on the agency,
            // so alias both keys to the same data so index.TryGetValue succeeds either way.
            var key = shape.Properties.RouteShortName ?? shape.Properties.RouteId;
            if (!routeGroups.TryGetValue(key, out var points))
                routeGroups[key] = points = new List<RoutePoint>();
            if (!coordGroups.TryGetValue(key, out var coordList))
                coordGroups[key] = coordList = new List<double[]>();

            foreach (var coord in shape.Geometry.Coordinates)
            {
                points.Add(new RoutePoint(key, coord[1], coord[0]));
                coordList.Add(coord); // [lon, lat]
            }

            modeMap[key] = shape.Properties.Mode;

            // Alias raw route_id → primary key so GTFS-RT lookups hit regardless of which value
            // the agency sends (e.g. MARTA sends short name "95"; WMATA sends route_id "RED").
            var rawId = shape.Properties.RouteId;
            if (!string.IsNullOrEmpty(rawId) && rawId != key)
            {
                routeGroups.TryAdd(rawId, points);
                coordGroups.TryAdd(rawId, coordList);
                modeMap.TryAdd(rawId, shape.Properties.Mode);
            }
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

        // Build cumDist and triggerPoints for each city/route
        var cumDistResult = new Dictionary<string, IReadOnlyDictionary<string, double[]>>(StringComparer.OrdinalIgnoreCase);
        var triggerPointsResult = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<TriggerPoint>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (cityName, coordGroups) in perCityCoords)
        {
            var cityCumDist = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            var cityTriggers = new Dictionary<string, IReadOnlyList<TriggerPoint>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (routeId, coordList) in coordGroups)
            {
                var coords = coordList.ToArray();
                var cd = new double[coords.Length];
                cd[0] = 0;
                for (var i = 1; i < coords.Length; i++)
                    cd[i] = cd[i - 1] + HaversineCalculator.DistanceMeters(coords[i - 1][1], coords[i - 1][0], coords[i][1], coords[i][0]);

                cityCumDist[routeId] = cd;
                cityTriggers[routeId] = triggerPointGenerator.Generate(coords, cd);
            }

            cumDistResult[cityName] = cityCumDist;
            triggerPointsResult[cityName] = cityTriggers;
        }

        return (indexResult, modeResult, cumDistResult, triggerPointsResult);
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

                (_routeIndex, _routeMode, _routeCumDist, _routeTriggerPoints) = BuildRouteIndex(shapes);
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

    Dictionary<string, CrossingBaseline?> GetCrossingBaselines(string city)
    {
        if (!_crossingBaselines.TryGetValue(city, out var map))
            _crossingBaselines[city] = map = new Dictionary<string, CrossingBaseline?>(StringComparer.OrdinalIgnoreCase);
        return map;
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
            var crossingRecords = new List<RouteCrossingBatchEvent.RouteCrossingRecord>();
            var baselineMap = GetCrossingBaselines(city.Name);
            _routeCumDist.TryGetValue(city.Name, out var cityCumDist);
            _routeTriggerPoints.TryGetValue(city.Name, out var cityTriggerPoints);

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
                    // Captured for crossing-offset timing below (the `prior` binding itself
                    // goes out of scope before the detection block).
                    DateTime? priorObservationUtc = null;

                    if (vehicleStateCache.TryGetValue(vehicleId, out var prior))
                    {
                        if (prior.LastUpdated > now)
                            continue;

                        priorObservationUtc = prior.LastUpdated;

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

                        // Crossing detection: run for every non-stale snapped vehicle
                        if (cityCumDist != null && cityTriggerPoints != null
                            && cityCumDist.TryGetValue(routeId, out var routeCumDist)
                            && cityTriggerPoints.TryGetValue(routeId, out var routeTriggers)
                            && routeTriggers.Count > 0)
                        {
                            var currentDistM = routeCumDist[snapValue.Index];
                            CrossingBaseline? baseline = baselineMap.TryGetValue(vehicleId, out var b) ? b : null;
                            // Spread this cycle's crossings over the REAL elapsed time since the
                            // prior observation, so a slow vehicle's notes pace out over the
                            // actual ~10s it took and a fast one's stay tight — capped so a long
                            // feed gap can't stretch a burst across the next batch's window.
                            var elapsedMs = priorObservationUtc.HasValue
                                ? (now - priorObservationUtc.Value).TotalMilliseconds
                                : CrossingDetector.DefaultSpreadMs;
                            var spreadMs = Math.Clamp(elapsedMs, 0, CrossingDetector.DefaultSpreadMs);
                            var detected = CrossingDetector.Detect(vehicleId, routeId, currentDistM, routeTriggers, ref baseline, spreadMs);
                            baselineMap[vehicleId] = baseline;
                            crossingRecords.AddRange(detected);
                        }
                        else
                        {
                            // Ensure baseline is seeded even when no trigger points yet
                            if (!baselineMap.ContainsKey(vehicleId))
                                baselineMap[vehicleId] = null;
                        }
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
                "City {City} spatial reconciliation: {Moved} moved, {Unchanged} unchanged, {Stationary} stationary, {Stale} stale, {SkippedNoRouteId} skippedNoRouteId, {SkippedUnknownRoute} skippedUnknownRoute, {CrossingsEmitted} crossingsEmitted. FeedHeaderTs={FeedHeaderTs} DuplicateFeed={DuplicateFeed}",
                city.Name, movedCount, unchangedCount, stationaryCount, staleCount, skippedNoRouteId, skippedUnknownRoute, crossingRecords.Count, feedTs, feedIsDuplicate);

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

                var envelopes = new List<EventEnvelope> { envelope };

                if (crossingRecords.Count > 0)
                {
                    var sorted = crossingRecords
                        .OrderBy(r => r.RouteId, StringComparer.Ordinal)
                        .ThenBy(r => r.VehicleId, StringComparer.Ordinal)
                        .ThenBy(r => r.TriggerIndex)
                        .ToList();
                    envelopes.Add(new EventEnvelope(
                        nameof(RouteCrossingBatchEvent),
                        DateTimeOffset.UtcNow,
                        new RouteCrossingBatchEvent(sorted)));
                }

                var isBatchPublished = await transitHubPublisher.PublishBatchAsync(city.Name, envelopes, ct);
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

                foreach (var (cityName, cache) in _vehicleStateCaches)
                {
                    foreach (var kvp in cache)
                    {
                        if (kvp.Value.LastUpdated < cutoff && cache.TryRemove(kvp.Key, out _))
                        {
                            pruned++;
                            // Prune crossing baseline alongside vehicle state (FR-015)
                            if (_crossingBaselines.TryGetValue(cityName, out var baselines))
                                baselines.Remove(kvp.Key);
                        }
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

                (_routeIndex, _routeMode, _routeCumDist, _routeTriggerPoints) = BuildRouteIndex(shapes);
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
