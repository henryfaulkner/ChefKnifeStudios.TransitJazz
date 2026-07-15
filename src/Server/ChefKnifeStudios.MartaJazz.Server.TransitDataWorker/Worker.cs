using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Checkpoints;
using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.Collections;
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

    Dictionary<string, IReadOnlyDictionary<string, RoutePoint[]>> _routeIndex = new(StringComparer.OrdinalIgnoreCase);
    // per-city routeJoinKey→TransitMode, built from GTFS route_type at static-data load time
    Dictionary<string, IReadOnlyDictionary<string, TransitMode>> _routeMode = new(StringComparer.OrdinalIgnoreCase);
    // per-city routeJoinKey→cumulative-distance array (parallel to _routeIndex)
    Dictionary<string, IReadOnlyDictionary<string, double[]>> _routeCumDist = new(StringComparer.OrdinalIgnoreCase);
    // per-city routeJoinKey→trigger points (built from shared TriggerPointGenerator)
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
            var tickStart = DateTime.UtcNow;
            // Sampled once per tick and reused verbatim on every row (process-wide, not
            // partitionable per city — summing across cities would be meaningless, R3).
            var gcHeapBytes = GC.GetTotalMemory(false);
            var workingSetBytes = Process.GetCurrentProcess().WorkingSet64;

            var processedCities = new List<string>();
            var tickHealthOk = true;
            int tickTonesEmitted = 0, tickVehiclesProcessed = 0;
            int tickVehicleStateCacheSize = 0, tickCrossingBaselineCacheSize = 0, tickRouteIndexSize = 0, tickRouteTriggerPointCacheSize = 0;

            foreach (var city in cities)
            {
                var cityStart = DateTime.UtcNow;
                CityTickResult result;

                try
                {
                    var feed = await city.FetchVehiclesAsync(stoppingToken);

                    if (!_routeIndex.TryGetValue(city.Name, out var index) || index == null)
                    {
                        logger.LogWarning("City {City}: route index not ready, skipping tick.", city.Name);
                        result = CityTickResult.Unhealthy(city.Name, this);
                    }
                    else
                    {
                        _routeMode.TryGetValue(city.Name, out var modeMap);
                        result = feed.Entities.Count > 0
                            ? await ProcessSpatialReconciliationAsync(city, feed, index, modeMap, stoppingToken)
                            : CityTickResult.Healthy(city.Name, this, feed.Header?.Timestamp, cityStart);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "City {City} tick failed; other cities unaffected", city.Name);
                    result = CityTickResult.Unhealthy(city.Name, this);
                }

                if (city.EmitsTelemetry)
                {
                    var cityEnd = DateTime.UtcNow;
                    eventNotifications.PostEvent(this, new TelemetryEvent
                    {
                        event_type = "PerCityCycle",
                        event_id = Guid.NewGuid().ToString("N"),
                        observation_utc = cityEnd,
                        city_name = result.CityName,
                        feed_freshness_seconds = result.FeedFreshnessSeconds,
                        time_taken_seconds = (cityEnd - cityStart).TotalSeconds,
                        health_ok = result.HealthOk,
                        tones_emitted = result.TonesEmitted,
                        vehicles_processed = result.VehiclesProcessed,
                        gc_heap_bytes = gcHeapBytes,
                        process_working_set_bytes = workingSetBytes,
                        vehicle_state_cache_size = result.VehicleStateCacheSize,
                        crossing_baseline_cache_size = result.CrossingBaselineCacheSize,
                        route_index_size = result.RouteIndexSize,
                        route_trigger_point_cache_size = result.RouteTriggerPointCacheSize
                    });

                    processedCities.Add(city.Name);
                    tickHealthOk &= result.HealthOk;
                    tickTonesEmitted += result.TonesEmitted;
                    tickVehiclesProcessed += result.VehiclesProcessed;
                    tickVehicleStateCacheSize += result.VehicleStateCacheSize;
                    tickCrossingBaselineCacheSize += result.CrossingBaselineCacheSize;
                    tickRouteIndexSize += result.RouteIndexSize;
                    tickRouteTriggerPointCacheSize += result.RouteTriggerPointCacheSize;
                }
            }

            if (processedCities.Count > 0)
            {
                var tickEnd = DateTime.UtcNow;
                eventNotifications.PostEvent(this, new TelemetryEvent
                {
                    event_type = "FullCycle",
                    event_id = Guid.NewGuid().ToString("N"),
                    observation_utc = tickEnd,
                    cities_processed_count = processedCities.Count,
                    cities_processed_csv = string.Join(",", processedCities),
                    time_taken_seconds = (tickEnd - tickStart).TotalSeconds,
                    health_ok = tickHealthOk,
                    tones_emitted = tickTonesEmitted,
                    vehicles_processed = tickVehiclesProcessed,
                    gc_heap_bytes = gcHeapBytes,
                    process_working_set_bytes = workingSetBytes,
                    vehicle_state_cache_size = tickVehicleStateCacheSize,
                    crossing_baseline_cache_size = tickCrossingBaselineCacheSize,
                    route_index_size = tickRouteIndexSize,
                    route_trigger_point_cache_size = tickRouteTriggerPointCacheSize
                });
            }
        }
    }

    /// <summary>Per-city outcome of one tick, surfaced out of <see cref="ProcessSpatialReconciliationAsync"/>
    /// so the caller can post a PerCityCycle row on every path (healthy, not-ready, and failed) — R2/R5.</summary>
    readonly record struct CityTickResult(
        string CityName,
        bool HealthOk,
        double? FeedFreshnessSeconds,
        int TonesEmitted,
        int VehiclesProcessed,
        int VehicleStateCacheSize,
        int CrossingBaselineCacheSize,
        int RouteIndexSize,
        int RouteTriggerPointCacheSize)
    {
        public static CityTickResult Unhealthy(string cityName, Worker worker) => new(
            cityName, HealthOk: false, FeedFreshnessSeconds: null, TonesEmitted: 0, VehiclesProcessed: 0,
            VehicleStateCacheSize: worker.GetVehicleCache(cityName).Count,
            CrossingBaselineCacheSize: worker.GetCrossingBaselines(cityName).Count,
            RouteIndexSize: worker._routeIndex.TryGetValue(cityName, out var idx) ? idx.Count : 0,
            RouteTriggerPointCacheSize: worker._routeTriggerPoints.TryGetValue(cityName, out var tp) ? tp.Count : 0);

        public static CityTickResult Healthy(string cityName, Worker worker, ulong? feedHeaderTs, DateTime observationUtc) => new(
            cityName, HealthOk: true,
            FeedFreshnessSeconds: feedHeaderTs.HasValue
                ? (observationUtc - DateTimeOffset.FromUnixTimeSeconds((long)feedHeaderTs.Value).UtcDateTime).TotalSeconds
                : null,
            TonesEmitted: 0, VehiclesProcessed: 0,
            VehicleStateCacheSize: worker.GetVehicleCache(cityName).Count,
            CrossingBaselineCacheSize: worker.GetCrossingBaselines(cityName).Count,
            RouteIndexSize: worker._routeIndex.TryGetValue(cityName, out var idx) ? idx.Count : 0,
            RouteTriggerPointCacheSize: worker._routeTriggerPoints.TryGetValue(cityName, out var tp) ? tp.Count : 0);
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
            var key = shape.Properties.JoinKey;
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

            foreach (var (routeJoinKey, coordList) in coordGroups)
            {
                var coords = coordList.ToArray();
                var cd = new double[coords.Length];
                cd[0] = 0;
                for (var i = 1; i < coords.Length; i++)
                    cd[i] = cd[i - 1] + HaversineCalculator.DistanceMeters(coords[i - 1][1], coords[i - 1][0], coords[i][1], coords[i][0]);

                cityCumDist[routeJoinKey] = cd;
                cityTriggers[routeJoinKey] = triggerPointGenerator.Generate(coords, cd);
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

    async Task<CityTickResult> ProcessSpatialReconciliationAsync(
        ITransitCity city,
        FeedMessage feed,
        IReadOnlyDictionary<string, RoutePoint[]> index,
        IReadOnlyDictionary<string, TransitMode>? modeMap,
        CancellationToken ct)
    {
        try
        {
            var vehicleStateCache = GetVehicleCache(city.Name);

            // Short-lived per-tick accumulators sized to the current vehicle feed. Backed by
            // ArrayPool via RecyclableList so the backing arrays are recycled instead of allocated
            // (and LOH-pressured) on every 10s tick. Pre-sized to the entity count so no mid-fill
            // rental happens. Disposed at method scope — safe because SignalR serializes the payload
            // synchronously inside the PublishBatchAsync await, before these go out of scope.
            using var batch = new RecyclableList<RouteNearestPointBatchEvent.RouteNearestPointRecord>(feed.Entities.Count);
            int movedCount = 0, unchangedCount = 0, stationaryCount = 0, staleCount = 0, skippedNoJoinKey = 0, skippedUnknownRoute = 0;
            using var crossingRecords = new RecyclableList<RouteCrossingBatchEvent.RouteCrossingRecord>();
            var baselineMap = GetCrossingBaselines(city.Name);
            _routeCumDist.TryGetValue(city.Name, out var cityCumDist);
            _routeTriggerPoints.TryGetValue(city.Name, out var cityTriggerPoints);

            foreach (var entity in feed.Entities)
            {
                try
                {
                    if (entity.Vehicle?.Position == null) continue;

                    string vehicleId = entity.Vehicle.Vehicle?.Id ?? entity.Id;
                    string? routeJoinKey = entity.Vehicle.Trip?.RouteId;

                    if (string.IsNullOrEmpty(routeJoinKey))
                    {
                        skippedNoJoinKey++;
                        continue;
                    }

                    if (!index.TryGetValue(routeJoinKey, out var routePoints))
                    {
                        skippedUnknownRoute++;
                        continue;
                    }

                    double lat = (double)entity.Vehicle.Position.Latitude;
                    double lon = (double)entity.Vehicle.Position.Longitude;
                    var now = DateTime.UtcNow;

                    const int SnapWindowSize = 30;

                    var snap = vehicleStateCache.TryGetValue(vehicleId, out var priorForSnap) && priorForSnap.RouteJoinKey == routeJoinKey
                        ? RouteSnapper.FindNearestInWindow(lat, lon, routePoints, priorForSnap.SnapIndex, SnapWindowSize)
                        : RouteSnapper.FindNearest(lat, lon, routePoints);
                    if (snap == null) continue;

                    var snapValue = snap.Value;
                    var nearest = snapValue.Point;

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
                            nearest.RouteJoinKey,
                            Math.Round(prior.NearestLat, 5),
                            Math.Round(prior.NearestLon, 5),
                            Math.Round(nearest.Lat, 5),
                            Math.Round(nearest.Lon, 5),
                            (int)(now - prior.LastUpdated).TotalMilliseconds,
                            entity.Vehicle.Position.Speed,
                            entity.Vehicle.Position.Bearing,
                            isStale,
                            modeMap != null && modeMap.TryGetValue(routeJoinKey, out var m) ? m : TransitMode.Bus
                        ));

                        if (isStale)
                        {
                            staleCount++;
                        }
                        else if (prior.NearestLat != nearest.Lat || prior.NearestLon != nearest.Lon)
                        {
                            movedCount++;
                        }
                        else if ((entity.Vehicle.Position.Speed ?? 0f) == 0f)
                        {
                            stationaryCount++;
                        }
                        else
                        {
                            unchangedCount++;
                        }
                    }
                    else
                    {
                        batch.Add(new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                            vehicleId,
                            nearest.RouteJoinKey,
                            Math.Round(nearest.Lat, 5),
                            Math.Round(nearest.Lon, 5),
                            Math.Round(nearest.Lat, 5),
                            Math.Round(nearest.Lon, 5),
                            0, // first observation: no prior, client snaps into place instantly
                            entity.Vehicle.Position.Speed,
                            entity.Vehicle.Position.Bearing,
                            false,
                            modeMap != null && modeMap.TryGetValue(routeJoinKey, out var m2) ? m2 : TransitMode.Bus
                        ));
                        movedCount++;
                    }

                    if (!isStale)
                    {
                        vehicleStateCache[vehicleId] = new VehicleState(
                            nearest.Lat,
                            nearest.Lon,
                            now,
                            nearest.RouteJoinKey,
                            entity.Vehicle.Position.Speed,
                            entity.Vehicle.Position.Bearing,
                            snapValue.DistanceKm,
                            lat,
                            lon,
                            snapValue.Index,
                            currentVehicleTimestamp);

                        // Crossing detection: run for every non-stale snapped vehicle
                        if (cityCumDist != null && cityTriggerPoints != null
                            && cityCumDist.TryGetValue(routeJoinKey, out var routeCumDist)
                            && cityTriggerPoints.TryGetValue(routeJoinKey, out var routeTriggers)
                            && routeTriggers.Count > 0)
                        {
                            var currentDistM = routeCumDist[snapValue.Index];
                            CrossingBaseline? baseline = baselineMap.TryGetValue(vehicleId, out var b) ? b : null;
                            // Stamp crossings with the RESOLVED join key (short-name-based), the same
                            // value the nearest-point records carry and the client's RouteItems /
                            // SelectedRouteJoinKeys use. The raw local `routeJoinKey` is the GTFS-RT
                            // Trip.RouteId, which differs from JoinKey whenever a short name exists —
                            // stamping that made the client's route-filter gate never match, so tone /
                            // checkpoint-pulse filtering silently broke for those agencies.
                            var detected = CrossingDetector.Detect(vehicleId, nearest.RouteJoinKey, currentDistM, routeTriggers, ref baseline);
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
                "City {City} spatial reconciliation: {Moved} moved, {Unchanged} unchanged, {Stationary} stationary, {Stale} stale, {SkippedNoJoinKey} skippedNoJoinKey, {SkippedUnknownRoute} skippedUnknownRoute, {CrossingsEmitted} crossingsEmitted. FeedHeaderTs={FeedHeaderTs} DuplicateFeed={DuplicateFeed}",
                city.Name, movedCount, unchangedCount, stationaryCount, staleCount, skippedNoJoinKey, skippedUnknownRoute, crossingRecords.Count, feedTs, feedIsDuplicate);

            if (city.EmitsTelemetry)
            {
                var droppedRecords = logEventWorker.DroppedRecords;
                var persistFailures = loggingService.PersistFailures;
                var bufferOccupancy = logEventWorker.BufferOccupancy;

                logger.LogInformation(
                    "Sidecar self-health: BufferOccupancy={Occupancy}, DroppedRecords={Dropped}, PersistFailures={Failures}",
                    bufferOccupancy, droppedRecords, persistFailures);
            }

            if (batch.Count > 0)
            {
                var envelope = new EventEnvelope(
                    nameof(RouteNearestPointBatchEvent),
                    DateTimeOffset.UtcNow,
                    new RouteNearestPointBatchEvent(batch)
                );

                var envelopes = new List<EventEnvelope> { envelope };

                if (crossingRecords.Count > 0)
                {
                    var sorted = crossingRecords
                        .OrderBy(r => r.RouteJoinKey, StringComparer.Ordinal)
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

            double? feedFreshnessSeconds = feedTs.HasValue
                ? (cycleEnd - DateTimeOffset.FromUnixTimeSeconds((long)feedTs.Value).UtcDateTime).TotalSeconds
                : null;

            return new CityTickResult(
                city.Name,
                HealthOk: true,
                FeedFreshnessSeconds: feedFreshnessSeconds,
                TonesEmitted: crossingRecords.Count,
                VehiclesProcessed: movedCount + unchangedCount + stationaryCount + staleCount,
                VehicleStateCacheSize: vehicleStateCache.Count,
                CrossingBaselineCacheSize: baselineMap.Count,
                RouteIndexSize: index.Count,
                RouteTriggerPointCacheSize: cityTriggerPoints?.Count ?? 0);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in spatial reconciliation for city {City}.", city.Name);
            return CityTickResult.Unhealthy(city.Name, this);
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
}
