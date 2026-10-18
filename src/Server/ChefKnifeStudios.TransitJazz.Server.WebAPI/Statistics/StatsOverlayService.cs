#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Stats;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

/// <summary>Builds one structurally complete API snapshot from one bounded database read.</summary>
public sealed class StatsOverlayService(
    IStatsOverlayQuery? query,
    StatsOverlayOptions options,
    TimeProvider timeProvider,
    ReportedVehicleSourceCatalog? reportedVehicleCatalog = null)
{
    const long MaxJavaScriptExactInteger = 9_007_199_254_740_991L;
    static readonly HashSet<string> SafeReasons = new(StatsOverlayVocabulary.Reasons, StringComparer.Ordinal);
    readonly object cacheGate = new();
    readonly Dictionary<string, CacheEntry> cache = new(StringComparer.Ordinal);
    readonly Dictionary<string, Task<StatsOverlayResponse>> inFlight = new(StringComparer.Ordinal);
    readonly SemaphoreSlim readSlots = new(options.MaximumConcurrentReads, options.MaximumConcurrentReads);
    long cachedEstimatedBytes;

    sealed record CacheEntry(string IdentityKey, StatsOverlayResponse Response, long EstimatedBytes, DateTimeOffset CreatedAt);

    sealed class ResponseSizeLimitException : Exception;

    sealed class CappedResponseBufferWriter(int maximumBytes, CancellationToken cancellationToken) : IBufferWriter<byte>
    {
        byte[] buffer = new byte[Math.Min(maximumBytes, 16 * 1024)];
        int writtenCount;

        public int WrittenCount => writtenCount;

        public void Advance(int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count < 0 || count > buffer.Length - writtenCount) throw new ArgumentOutOfRangeException(nameof(count));
            writtenCount += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return buffer.AsMemory(writtenCount);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return buffer.AsSpan(writtenCount);
        }

        void EnsureCapacity(int sizeHint)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sizeHint < 0) throw new ArgumentOutOfRangeException(nameof(sizeHint));
            sizeHint = Math.Max(sizeHint, 1);
            var required = (long)writtenCount + sizeHint;
            if (required > maximumBytes) throw new ResponseSizeLimitException();
            if (required <= buffer.Length) return;

            var nextSize = (int)Math.Min(maximumBytes, Math.Max(required, (long)buffer.Length * 2));
            Array.Resize(ref buffer, nextSize);
        }
    }

    sealed class ProjectionIndexes
    {
        public ProjectionIndexes(StatsOverlayQueryResult result)
        {
            RouteOptionsByCityKey = result.RouteOptions.ToDictionary(
                option => (option.CitySlug, option.RouteJoinKey));
            RouteOptionKeys = RouteOptionsByCityKey.Keys.ToHashSet();
            RouteMeasuresByCityWindowKey = result.RouteMeasures.ToDictionary(
                measure => (measure.CitySlug, measure.Window, measure.RouteJoinKey));
            RouteMeasuresByCityWindow = result.RouteMeasures
                .GroupBy(measure => (measure.CitySlug, measure.Window))
                .ToDictionary(group => group.Key, group => group.ToArray());

            var cohorts = new Dictionary<(string CitySlug, string Window), List<StatsOverlayCohortHour>>();
            foreach (var row in result.CohortHours)
            {
                if (row.IsToday) AddCohort(cohorts, (row.CitySlug, "today"), row);
                if (row.IsLast24) AddCohort(cohorts, (row.CitySlug, "last24CompletedHours"), row);
            }
            CohortsByCityWindow = cohorts.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        }

        public Dictionary<(string CitySlug, string RouteJoinKey), StatsOverlayRouteOption> RouteOptionsByCityKey { get; }
        public HashSet<(string CitySlug, string RouteJoinKey)> RouteOptionKeys { get; }
        public Dictionary<(string CitySlug, string Window, string RouteJoinKey), StatsOverlayRouteMeasure> RouteMeasuresByCityWindowKey { get; }
        public Dictionary<(string CitySlug, string Window), StatsOverlayRouteMeasure[]> RouteMeasuresByCityWindow { get; }
        public Dictionary<(string CitySlug, string Window), StatsOverlayCohortHour[]> CohortsByCityWindow { get; }

        public StatsOverlayCohortHour[] Cohorts(string citySlug, string window) =>
            CohortsByCityWindow.TryGetValue((citySlug, window), out var rows) ? rows : [];

        public StatsOverlayRouteMeasure[] RouteMeasures(string citySlug, string window) =>
            RouteMeasuresByCityWindow.TryGetValue((citySlug, window), out var rows) ? rows : [];

        static void AddCohort(
            IDictionary<(string CitySlug, string Window), List<StatsOverlayCohortHour>> cohorts,
            (string CitySlug, string Window) key,
            StatsOverlayCohortHour row)
        {
            if (!cohorts.TryGetValue(key, out var rows)) cohorts.Add(key, rows = []);
            rows.Add(row);
        }
    }

    public async Task<StatsOverlayResponse> ReadAsync(StatsOverlayRequest? request, CancellationToken cancellationToken)
    {
        if (!options.Enabled) throw new StatsOverlayRequestException("stats_disabled", 503);
        try
        {
            options.Validate(requireCities: true);
        }
        catch (Exception exception) when (exception is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new StatsOverlayRequestException("dependency_unavailable", 503);
        }

        var normalized = StatsOverlayPolicy.NormalizeRequest(request, options);
        var plan = StatsOverlayPolicy.Resolve(normalized, options, timeProvider.GetUtcNow());
        var cacheable = RouteKeys(normalized).Length == 0;
        var key = BuildCacheKey(normalized, plan, options);
        var cacheScope = CacheScopeKey(normalized);
        Task<StatsOverlayResponse> fill;
        lock (cacheGate)
        {
            RemoveExpired(timeProvider.GetUtcNow());
            if (cacheable && cache.TryGetValue(cacheScope, out var cached))
            {
                if (string.Equals(cached.IdentityKey, key, StringComparison.Ordinal))
                {
                    StatsOverlayTelemetry.RecordCacheEvent(normalized.Scope, "hit");
                    return cached.Response;
                }
                // A scope has one replaceable snapshot slot. A changed cutoff or policy
                // identity evicts the old value before starting the replacement read.
                cache.Remove(cacheScope);
                cachedEstimatedBytes -= cached.EstimatedBytes;
            }
            if (cacheable && inFlight.TryGetValue(key, out var existing))
            {
                StatsOverlayTelemetry.RecordCacheEvent(normalized.Scope, "coalesced");
                fill = existing;
            }
            else
            {
                if (!readSlots.Wait(0)) throw new StatsOverlayRequestException("busy", 503);
                StatsOverlayTelemetry.RecordCacheEvent(normalized.Scope, cacheable ? "miss" : "uncached");
                fill = FillSnapshotAsync(normalized, plan, cacheScope, key, cacheable,
                    cacheable ? CancellationToken.None : cancellationToken);
                if (cacheable) inFlight.Add(key, fill);
                _ = fill.ContinueWith(static task => _ = task.Exception,
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        return await fill.WaitAsync(cancellationToken);
    }

    async Task<StatsOverlayResponse> FillSnapshotAsync(
        StatsOverlayRequest normalized,
        StatsSnapshotPlan plan,
        string cacheScope,
        string cacheKey,
        bool cacheable,
        CancellationToken callerCancellation)
    {
        await Task.Yield();
        using var deadline = callerCancellation.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(callerCancellation)
            : new CancellationTokenSource();
        deadline.CancelAfter(TimeSpan.FromSeconds(options.ReadTimeoutSeconds));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (query is null) throw new StatsOverlayRequestException("dependency_unavailable", 503);
            if (options.ReportedVehiclesEnabled && reportedVehicleCatalog is null)
                throw new StatsOverlayRequestException("dependency_unavailable", 503);
            var queryInput = BuildQueryInput(plan, options, reportedVehicleCatalog);
            StatsOverlayQueryResult result;
            try
            {
                var remaining = TimeSpan.FromSeconds(options.ReadTimeoutSeconds) - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero) throw new TimeoutException("Stats query deadline elapsed before the read started.");
                result = await query.ReadAsync(queryInput, remaining, deadline.Token);
            }
            catch (OperationCanceledException) when (callerCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                throw new StatsOverlayRequestException("read_timeout", 503);
            }
            catch (TimeoutException)
            {
                throw new StatsOverlayRequestException("read_timeout", 503);
            }
            catch (StatsOverlayRequestException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new StatsOverlayRequestException("dependency_unavailable", 503);
            }

            deadline.Token.ThrowIfCancellationRequested();
            if (result.ScopeTooLarge) throw new StatsOverlayRequestException("scope_too_large", 422);

            var indexes = new ProjectionIndexes(result);
            var windows = plan.Cities.Select(city => BuildCityWindow(city, plan, normalized, options, result, indexes)).ToArray();
            var todayCoverageByCity = windows.ToDictionary(window => window.CitySlug, window => window.TodayCoverage, StringComparer.Ordinal);
            var last24CoverageByCity = windows.ToDictionary(window => window.CitySlug, window => window.Last24Coverage, StringComparer.Ordinal);
            var todayDistance = BuildDistanceSummary("today", plan, result, todayCoverageByCity);
            var last24Distance = BuildDistanceSummary("last24CompletedHours", plan, result, last24CoverageByCity);
            var categoryShares = BuildCategoryShares(normalized, result);
            var bubbles = BuildBubbles(plan, normalized, result, options, todayCoverageByCity, indexes);
            var timeline = BuildTimeline(plan, normalized, result, options);
            var warnings = BuildWarnings(plan, normalized, result, windows);

            var response = new StatsOverlayResponse
            {
                GeneratedAtUtc = plan.GeneratedAtUtc,
                CutoffUtc = plan.CutoffUtc,
                Selection = new StatsSelection(normalized.Scope!, normalized.CitySlug, RouteKeys(normalized)),
                Cities = windows,
                RouteOptions = string.Equals(normalized.Scope, "city", StringComparison.Ordinal)
                    ? result.RouteOptions.OrderBy(option => option.CitySlug, StringComparer.Ordinal)
                        .ThenBy(option => option.RouteJoinKey, StringComparer.Ordinal)
                        .Select(option => new StatsRouteOption
                        {
                            CitySlug = option.CitySlug,
                            RouteJoinKey = option.RouteJoinKey,
                            Label = option.Label,
                            CategoryKey = option.CategoryKey,
                            MetadataTimestampUtc = DateTime.SpecifyKind(option.MetadataTimestampUtc, DateTimeKind.Utc),
                            CategoryChanged = option.CategoryChanged,
                            AvailabilityReason = null
                        }).ToArray()
                    : [],
                TodayDistance = todayDistance,
                Last24Distance = last24Distance,
                TodayReportedVehicles = BuildReportedVehicleSummary("today", plan, result, options),
                Last24ReportedVehicles = BuildReportedVehicleSummary("last24CompletedHours", plan, result, options),
                CategoryShares = categoryShares,
                Bubbles = bubbles,
                Timeline = timeline,
                Warnings = warnings
            };

            EnsureSafeNumbers(response);
            deadline.Token.ThrowIfCancellationRequested();
            int responseBytes;
            try
            {
                var responseWriter = new CappedResponseBufferWriter(options.MaximumResponseBytes, deadline.Token);
                using (var jsonWriter = new Utf8JsonWriter(responseWriter))
                    JsonSerializer.Serialize(jsonWriter, response, JsonSettings.DefaultOptions);
                responseBytes = responseWriter.WrittenCount;
                StatsOverlayTelemetry.RecordResponseBytes(normalized.Scope, responseBytes);
            }
            catch (ResponseSizeLimitException)
            {
                StatsOverlayTelemetry.RecordResponseBytes(normalized.Scope, checked(options.MaximumResponseBytes + 1));
                throw new StatsOverlayRequestException("response_too_large", 422);
            }
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(options.ReadTimeoutSeconds))
                throw new StatsOverlayRequestException("read_timeout", 503);
            if (cacheable) CacheSnapshot(cacheScope, cacheKey, response, EstimateObjectBytes(responseBytes));
            return response;
        }
        catch (OperationCanceledException) when (callerCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new StatsOverlayRequestException("read_timeout", 503);
        }
        finally
        {
            StatsOverlayTelemetry.RecordReadDuration(normalized.Scope, stopwatch.Elapsed);
            readSlots.Release();
            if (cacheable)
            {
                lock (cacheGate) inFlight.Remove(cacheKey);
            }
        }
    }

    void CacheSnapshot(string scope, string identityKey, StatsOverlayResponse response, long estimatedBytes)
    {
        if (estimatedBytes > options.MaximumCacheBytes) return;
        var generatedAtUtc = new DateTimeOffset(
            DateTime.SpecifyKind(response.GeneratedAtUtc, DateTimeKind.Utc), TimeSpan.Zero);
        var expiresAt = generatedAtUtc.AddSeconds(options.CacheSeconds);
        lock (cacheGate)
        {
            var now = timeProvider.GetUtcNow();
            RemoveExpired(now);
            // TTL is measured from the captured snapshot time, not admission time.
            // A slow read must not extend visibility beyond the policy window.
            if (now >= expiresAt) return;
            if (cache.TryGetValue(scope, out var newer)
                && newer.CreatedAt > generatedAtUtc) return;
            if (cache.Remove(scope, out var previous)) cachedEstimatedBytes -= previous.EstimatedBytes;
            while (cache.Count >= options.MaximumCacheEntries || cachedEstimatedBytes + estimatedBytes > options.MaximumCacheBytes)
            {
                var oldest = cache.MinBy(pair => pair.Value.CreatedAt);
                if (oldest.Equals(default(KeyValuePair<string, CacheEntry>))) break;
                cache.Remove(oldest.Key);
                cachedEstimatedBytes -= oldest.Value.EstimatedBytes;
            }
            if (cache.Count >= options.MaximumCacheEntries || cachedEstimatedBytes + estimatedBytes > options.MaximumCacheBytes) return;
            cache.Add(scope, new CacheEntry(identityKey, response, estimatedBytes, generatedAtUtc));
            cachedEstimatedBytes += estimatedBytes;
        }
    }

    // This deliberately overweights the serialized UTF-8 size to account for UTF-16
    // strings and per-object/collection overhead. It is an admission estimate, not a
    // measured heap size; actual object-memory measurement remains a release validation item.
    static long EstimateObjectBytes(int serializedUtf8Bytes) => checked((long)serializedUtf8Bytes * 8 + 4_096);

    void RemoveExpired(DateTimeOffset now)
    {
        foreach (var expired in cache.Where(pair => now >= pair.Value.CreatedAt.AddSeconds(options.CacheSeconds))
                     .Select(pair => pair.Key).ToArray())
        {
            cachedEstimatedBytes -= cache[expired].EstimatedBytes;
            cache.Remove(expired);
        }
    }

    static string CacheScopeKey(StatsOverlayRequest selection) =>
        string.Equals(selection.Scope, "all", StringComparison.Ordinal)
            ? "all"
            : "city:" + selection.CitySlug;

    string BuildCacheKey(StatsOverlayRequest selection, StatsSnapshotPlan plan, StatsOverlayOptions options)
    {
        var planKey = string.Join(";", plan.Cities.Select(city => string.Join(",", city.City.CitySlug,
            city.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), city.City.TimeZoneId,
            city.City.HealthyCadenceLimitSeconds.ToString(CultureInfo.InvariantCulture))));
        var material = string.Join("|", selection.Scope, selection.CitySlug ?? string.Empty,
            plan.CutoffUtc.ToString("O", CultureInfo.InvariantCulture), planKey,
            JsonSerializer.Serialize(options),
            options.ReportedVehiclesEnabled ? ReportedVehicleCacheIdentity() : "reported-disabled");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    string ReportedVehicleCacheIdentity() => reportedVehicleCatalog is null
        ? "reported-policy-missing"
        : string.Join(";", reportedVehicleCatalog.Cities.OrderBy(city => city.CitySlug, StringComparer.Ordinal)
            .Select(city => string.Join(":", city.CitySlug,
                reportedVehicleCatalog.SourceSetFingerprint(city.CitySlug),
                reportedVehicleCatalog.CadencePolicyFingerprint(city.CitySlug))));

    static string[] RouteKeys(StatsOverlayRequest request) => request.RouteJoinKeys ?? [];

    StatsOverlayQueryInput BuildQueryInput(StatsSnapshotPlan plan, StatsOverlayOptions options,
        ReportedVehicleSourceCatalog? reportedVehicleCatalog)
    {
        var target = new Dictionary<(string City, DateTime Hour), StatsOverlayQueryHour>();
        foreach (var city in plan.Cities)
        {
            foreach (var hour in city.TodayHoursUtc)
                AddExpectedHour(target, city, hour, today: true, last24: false);
            foreach (var hour in city.Last24HoursUtc)
                AddExpectedHour(target, city, hour, today: false, last24: true);
        }

        return new StatsOverlayQueryInput
        {
            Scope = plan.Selection.Scope,
            SelectedRouteKeys = RouteKeys(plan.Selection),
            MaximumRoutesPerCity = options.MaximumSelectedRoutes,
            MaximumCategories = options.MaximumCategories,
            ReportedVehiclesEnabled = options.ReportedVehiclesEnabled,
            ReportedVehiclePolicies = options.ReportedVehiclesEnabled
                ? plan.Cities.Where(city => reportedVehicleCatalog!.TryGet(city.City.CitySlug, out _)).Select(city =>
                {
                    reportedVehicleCatalog!.TryGet(city.City.CitySlug, out var catalog);
                    return new StatsOverlayReportedVehiclePolicy(city.City.CitySlug,
                        reportedVehicleCatalog.SourceSetFingerprint(city.City.CitySlug),
                        reportedVehicleCatalog.CadencePolicyFingerprint(city.City.CitySlug),
                        catalog.Sources.Select(source => new StatsOverlayReportedVehicleSource(
                            source.SourceKey, source.GapLimitSeconds)).ToArray());
                }).ToArray()
                : [],
            Hours = target.Values.OrderBy(row => row.CitySlug, StringComparer.Ordinal)
                .ThenBy(row => row.HourStartUtc).ToArray()
        };
    }

    static StatsReportedVehicleSummary BuildReportedVehicleSummary(string window,
        StatsSnapshotPlan plan, StatsOverlayQueryResult result, StatsOverlayOptions options)
    {
        var isToday = window == "today";
        var publicWindow = isToday ? "today" : "last24";
        var queryWindow = isToday ? "today" : "last24CompletedHours";
        var expectedTotal = plan.Cities.Sum(city => (long)(isToday ? city.TodayHoursUtc.Count : city.Last24HoursUtc.Count));
        var resultByCity = result.ReportedVehicleWindows
            .Where(row => string.Equals(row.Window, queryWindow, StringComparison.Ordinal))
            .ToDictionary(row => row.CitySlug, StringComparer.Ordinal);

        if (!options.ReportedVehiclesEnabled)
            return new StatsReportedVehicleSummary
            {
                Window = publicWindow,
                State = isToday && expectedTotal == 0 ? "pending" : "unavailable",
                PopulationScope = plan.Selection.Scope == "all" ? "sumOfCityFeeds" : "cityFeeds",
                ExpectedCityHours = checked((int)expectedTotal),
                ExpectedCityCount = plan.Cities.Count,
                Cities = plan.Cities.Select(city => new StatsReportedVehicleCitySummary
                {
                    CitySlug = city.City.CitySlug,
                    WindowReference = city.City.CitySlug + ":" + queryWindow,
                    State = isToday && city.TodayHoursUtc.Count == 0 ? "pending" : "unavailable",
                    ExpectedHours = isToday ? city.TodayHoursUtc.Count : city.Last24HoursUtc.Count,
                    BoundaryExcludedHours = isToday ? city.BoundaryExcludedTodayHours : 0,
                    Exclusions = [new StatsReportedVehicleExclusion("reported_reads_disabled", 0)],
                    Limitations = ["reported_reads_disabled"],
                }).ToArray(),
                Limitations = ["reported_reads_disabled"],
            };

        var citySummaries = new List<StatsReportedVehicleCitySummary>(plan.Cities.Count);
        foreach (var city in plan.Cities)
        {
            var expected = isToday ? city.TodayHoursUtc.Count : city.Last24HoursUtc.Count;
            var boundary = isToday ? city.BoundaryExcludedTodayHours : 0;
            resultByCity.TryGetValue(city.City.CitySlug, out var row);
            var contributing = row?.ContributingHours ?? 0;
            var fullyCovered = row?.FullyCoveredHours ?? 0;
            var count = row?.Count;
            var state = expected == 0 && isToday ? "pending"
                : count is null ? "unavailable"
                : fullyCovered == expected && boundary == 0 ? "complete" : "partial";
            var exclusions = row?.Exclusions
                .Where(x => ReportedVehicleVocabularies.ExclusionReasons.Contains(x.Code, StringComparer.Ordinal))
                .Select(x => new StatsReportedVehicleExclusion(x.Code, x.Hours)).ToList() ?? [];
            if (boundary > 0) exclusions.Add(new StatsReportedVehicleExclusion("boundary_excluded", boundary));
            var limitations = row?.Limitations
                .Where(x => ReportedVehicleVocabularies.IncompleteReasons.Contains(x, StringComparer.Ordinal)
                    || ReportedVehicleVocabularies.IdentityLimitations.Contains(x, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() ?? [];
            citySummaries.Add(new StatsReportedVehicleCitySummary
            {
                CitySlug = city.City.CitySlug,
                WindowReference = city.City.CitySlug + ":" + queryWindow,
                Count = count,
                State = state,
                ExpectedHours = expected,
                ContributingHours = contributing,
                FullyCoveredHours = fullyCovered,
                BoundaryExcludedHours = boundary,
                FirstContributingUtc = row?.FirstContributingUtc,
                LastContributingUtc = row?.LastContributingUtc,
                Exclusions = exclusions,
                UnidentifiableRecordCount = row?.UnidentifiableRecordCount ?? 0,
                FallbackRecordCount = row?.FallbackRecordCount ?? 0,
                Limitations = limitations,
            });
        }

        var contributingCities = citySummaries.Where(city => city.Count is not null).ToArray();
        long? totalCount = contributingCities.Length == 0 ? null
            : contributingCities.Aggregate(0L, (sum, city) => checked(sum + city.Count!.Value));
        var contributingHoursTotal = citySummaries.Sum(city => (long)city.ContributingHours);
        var fullyCoveredHoursTotal = citySummaries.Sum(city => (long)city.FullyCoveredHours);
        var hasPendingCity = citySummaries.Any(city => city.State == "pending");
        var hasBoundary = citySummaries.Any(city => city.BoundaryExcludedHours > 0);
        var aggregateState = expectedTotal == 0 && isToday ? "pending"
            : totalCount is null ? "unavailable"
            : citySummaries.All(city => city.State == "complete") && !hasPendingCity && !hasBoundary ? "complete" : "partial";
        var aggregateLimitations = citySummaries.SelectMany(city => city.Limitations)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        return new StatsReportedVehicleSummary
        {
            Window = publicWindow,
            Count = totalCount,
            State = aggregateState,
            PopulationScope = plan.Selection.Scope == "all" ? "sumOfCityFeeds" : "cityFeeds",
            ExpectedCityHours = checked((int)expectedTotal),
            ContributingCityHours = checked((int)contributingHoursTotal),
            FullyCoveredCityHours = checked((int)fullyCoveredHoursTotal),
            ExpectedCityCount = plan.Cities.Count,
            ContributingCityCount = contributingCities.Length,
            Cities = citySummaries,
            Limitations = aggregateLimitations,
        };
    }

    static void AddExpectedHour(
        IDictionary<(string City, DateTime Hour), StatsOverlayQueryHour> target,
        StatsResolvedCityWindow city,
        DateTime hour,
        bool today,
        bool last24)
    {
        var key = (city.City.CitySlug, hour);
        if (target.TryGetValue(key, out var existing))
        {
            existing.IsToday |= today;
            existing.IsLast24 |= last24;
            return;
        }

        target.Add(key, new StatsOverlayQueryHour
        {
            CitySlug = city.City.CitySlug,
            HourStartUtc = hour,
            IsToday = today,
            IsLast24 = last24,
            ExpectedCadenceSeconds = city.City.HealthyCadenceLimitSeconds
        });
    }

    static StatsCityWindow BuildCityWindow(
        StatsResolvedCityWindow city,
        StatsSnapshotPlan plan,
        StatsOverlayRequest selection,
        StatsOverlayOptions options,
        StatsOverlayQueryResult result,
        ProjectionIndexes indexes) => new()
    {
        CitySlug = city.City.CitySlug,
        DisplayNameKey = city.City.DisplayNameKey,
        TimeZone = city.City.TimeZoneId,
        LocalDate = city.LocalDate,
        LocalDayStartUtc = city.LocalDayStartUtc,
        LocalDayEndUtc = city.LocalDayEndUtc,
        RequestedTodayStartUtc = city.RequestedTodayStartUtc,
        RequestedTodayEndUtc = city.RequestedTodayEndUtc,
        EligibleTodayStartUtc = city.EligibleTodayStartUtc,
        EligibleTodayEndUtc = city.EligibleTodayEndUtc,
        Last24StartUtc = city.Last24StartUtc,
        Last24EndUtc = city.Last24EndUtc,
        TodayCoverage = BuildCoverage(city, plan, selection, options, result, indexes, "today"),
        Last24Coverage = BuildCoverage(city, plan, selection, options, result, indexes, "last24CompletedHours")
    };

    static StatsCoverage BuildCoverage(
        StatsResolvedCityWindow city,
        StatsSnapshotPlan plan,
        StatsOverlayRequest selection,
        StatsOverlayOptions options,
        StatsOverlayQueryResult result,
        ProjectionIndexes indexes,
        string window)
    {
        var expectedHours = window == "today" ? city.TodayHoursUtc : city.Last24HoursUtc;
        var isToday = window == "today";
        var sourceHours = indexes.Cohorts(city.City.CitySlug, window);
        var selectedKeys = RouteKeys(selection);
        var filtered = selection.Scope == "city" && selectedKeys.Length > 0;
        var contributing = sourceHours.Where(row => row.IsEligible && (!filtered || row.SelectedRouteCount > 0)).ToArray();
        var fullyCovered = sourceHours.Where(row => row.IsEligible && (!filtered || row.SelectedRouteCount == selectedKeys.Length)).ToArray();
        var cityContributions = result.CityHours.Where(row => row.CitySlug == city.City.CitySlug && row.Window == window).ToArray();
        var routeMeasures = indexes.RouteMeasures(city.City.CitySlug, window);
        long availableRouteHours = filtered
            ? routeMeasures.Sum(row => (long)row.AvailableHours)
            : contributing.Sum(row => (long)row.RouteCount);
        long? expectedRouteHours;
        if (filtered)
        {
            expectedRouteHours = checked((long)selectedKeys.Length * expectedHours.Count);
        }
        else if (expectedHours.Count > 0 && sourceHours.Length == expectedHours.Count && sourceHours.All(row => row.IsEligible))
        {
            expectedRouteHours = sourceHours.Sum(row => (long)row.RouteCount);
        }
        else
        {
            expectedRouteHours = null;
        }

        var exclusionCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in sourceHours.Where(row => !row.IsEligible))
            AddReason(exclusionCounts, SafeReason(row.Reason));
        if (filtered && sourceHours.Any(row => row.IsEligible && row.SelectedRouteCount < selectedKeys.Length))
        {
            var knownKeys = new HashSet<string>(result.RouteOptions
                .Where(option => option.CitySlug == city.City.CitySlug)
                .Select(option => option.RouteJoinKey), StringComparer.Ordinal);
            var missingReason = selectedKeys.Any(key => !knownKeys.Contains(key)) ? "unknown_route" : "route_not_retained";
            AddReason(exclusionCounts, missingReason,
                sourceHours.Count(row => row.IsEligible && row.SelectedRouteCount < selectedKeys.Length));
        }
        if (isToday && city.BoundaryExcludedTodayHours > 0)
            exclusionCounts["boundary_excluded"] = city.BoundaryExcludedTodayHours;

        var selectedCoverage = filtered
            ? selectedKeys.Select(key => BuildSelectedRouteCoverage(city, expectedHours.Count, window, key, indexes)).ToArray()
            : null;
        var first = cityContributions.Length == 0 ? (DateTime?)null : cityContributions.Min(row => row.HourStartUtc);
        var last = cityContributions.Length == 0 ? (DateTime?)null : cityContributions.Max(row => row.HourStartUtc).AddHours(1);
        var historicalUnknown = !filtered
            ? expectedHours.Count > 0 && sourceHours.Any(row => !row.IsEligible)
            : selectedKeys.Any(key => !indexes.RouteOptionKeys.Contains((city.City.CitySlug, key)));
        var state = CoverageState(expectedHours.Count, contributing.Length, fullyCovered.Length,
            isToday && city.BoundaryExcludedTodayHours > 0);

        return new StatsCoverage
        {
            State = state,
            ExpectedClosedHours = expectedHours.Count,
            ContributingHours = contributing.Length,
            FullyCoveredHours = fullyCovered.Length,
            ExpectedRouteHours = expectedRouteHours,
            AvailableRouteHours = availableRouteHours,
            SelectedRouteCoverage = selectedCoverage,
            Exclusions = exclusionCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new StatsExclusion(pair.Key, pair.Value)).ToArray(),
            BoundaryExcludedHours = isToday ? city.BoundaryExcludedTodayHours : 0,
            FirstContributingUtc = first,
            LastContributingUtc = last,
            PendingThroughUtc = plan.CutoffUtc,
            HistoricalMembershipUnknown = historicalUnknown
        };
    }

    static StatsSelectedRouteCoverage BuildSelectedRouteCoverage(
        StatsResolvedCityWindow city,
        int expectedHours,
        string window,
        string routeKey,
        ProjectionIndexes indexes)
    {
        indexes.RouteMeasuresByCityWindowKey.TryGetValue((city.City.CitySlug, window, routeKey), out var measure);
        var availableHours = measure?.AvailableHours ?? 0;
        var optionExists = indexes.RouteOptionKeys.Contains((city.City.CitySlug, routeKey));
        var cohortRows = indexes.Cohorts(city.City.CitySlug, window);
        var reasons = cohortRows.Where(row => !row.IsEligible)
            .Select(row => SafeReason(row.Reason)).ToHashSet(StringComparer.Ordinal);
        var eligibleHours = cohortRows.Count(row => row.IsEligible);
        if (!optionExists)
            reasons.Add("unknown_route");
        else if (availableHours < eligibleHours)
            reasons.Add("route_not_retained");

        return new StatsSelectedRouteCoverage(city.City.CitySlug, routeKey, expectedHours, availableHours,
            reasons.OrderBy(reason => reason, StringComparer.Ordinal).ToArray());
    }

    static StatsDistanceSummary BuildDistanceSummary(
        string window,
        StatsSnapshotPlan plan,
        StatsOverlayQueryResult result,
        IReadOnlyDictionary<string, StatsCoverage> cityCoverage)
    {
        var cityHours = result.CityHours.Where(row => row.Window == window).ToArray();
        var metric = cityHours.Length == 0 ? (decimal?)null : cityHours.Sum(row => row.DistanceKilometers);
        var expectedHours = plan.Cities.Sum(city => window == "today" ? city.TodayHoursUtc.Count : city.Last24HoursUtc.Count);
        var coverage = BuildAggregateCoverage(plan, cityCoverage, window);
        return new StatsDistanceSummary
        {
            Window = window,
            Kilometers = metric,
            Coverage = coverage,
            ContributingCityHours = cityHours.Length,
            ExpectedCityHours = expectedHours,
            ContributingCityCount = cityHours.Select(row => row.CitySlug).Distinct(StringComparer.Ordinal).Count(),
            ExpectedCityCount = expectedHours == 0 ? 0 : plan.Cities.Count
        };
    }

    static StatsCoverage BuildAggregateCoverage(
        StatsSnapshotPlan plan,
        IReadOnlyDictionary<string, StatsCoverage> cityCoverage,
        string window)
    {
        var rows = cityCoverage.Values.ToArray();
        var exclusions = rows.SelectMany(row => row.Exclusions)
            .GroupBy(row => row.Reason, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new StatsExclusion(group.Key, group.Sum(row => row.Hours))).ToArray();
        var expected = rows.Sum(row => row.ExpectedClosedHours);
        var contributing = rows.Sum(row => row.ContributingHours);
        var full = rows.Sum(row => row.FullyCoveredHours);
        long? expectedRouteHours = rows.All(row => row.ExpectedRouteHours.HasValue)
            ? rows.Sum(row => row.ExpectedRouteHours!.Value)
            : null;
        var boundary = rows.Sum(row => row.BoundaryExcludedHours);
        var first = rows.Where(row => row.FirstContributingUtc.HasValue).Select(row => row.FirstContributingUtc!.Value).DefaultIfEmpty().Min();
        var last = rows.Where(row => row.LastContributingUtc.HasValue).Select(row => row.LastContributingUtc!.Value).DefaultIfEmpty().Max();
        return new StatsCoverage
        {
            State = expected == 0 && window == "today" ? "pending"
                : contributing == 0 ? "unavailable"
                : full == expected && boundary == 0 ? "complete" : "partial",
            ExpectedClosedHours = expected,
            ContributingHours = contributing,
            FullyCoveredHours = full,
            ExpectedRouteHours = expectedRouteHours,
            AvailableRouteHours = rows.Sum(row => row.AvailableRouteHours),
            SelectedRouteCoverage = rows.Any(row => row.SelectedRouteCoverage is not null) ? rows.SelectMany(row => row.SelectedRouteCoverage ?? []).ToArray() : null,
            Exclusions = exclusions,
            BoundaryExcludedHours = boundary,
            FirstContributingUtc = first == default ? null : first,
            LastContributingUtc = last == default ? null : last,
            PendingThroughUtc = plan.CutoffUtc,
            HistoricalMembershipUnknown = rows.Any(row => row.HistoricalMembershipUnknown)
        };
    }

    static IReadOnlyList<StatsCategoryShare> BuildCategoryShares(StatsOverlayRequest selection, StatsOverlayQueryResult result)
    {
        var categories = result.Categories.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        var knownAmounts = selection.Scope == "all"
            ? result.CategoryTotals.ToDictionary(row => row.CategoryKey, row => row.Kilometers, StringComparer.Ordinal)
            : result.CategoryHours.Where(row => row.Window == "today")
                .GroupBy(row => row.CategoryKey, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(row => row.Kilometers), StringComparer.Ordinal);
        var hasKnownToday = result.CityHours.Any(row => row.Window == "today");
        var total = knownAmounts.Values.Sum();
        return categories.Select(category =>
        {
            var amount = hasKnownToday
                ? knownAmounts.TryGetValue(category, out var value) ? value : 0m
                : (decimal?)null;
            return new StatsCategoryShare(category, amount,
                amount is { } kilometers && total > 0 ? kilometers * 100m / total : null, "today");
        }).ToArray();
    }

    static IReadOnlyList<StatsBubbleStatistic> BuildBubbles(
        StatsSnapshotPlan plan,
        StatsOverlayRequest selection,
        StatsOverlayQueryResult result,
        StatsOverlayOptions options,
        IReadOnlyDictionary<string, StatsCoverage> todayCoverage,
        ProjectionIndexes indexes)
    {
        var bubbles = new List<StatsBubbleStatistic>();
        if (selection.Scope == "city")
        {
            var city = plan.Cities.Single();
            var selectedKeys = RouteKeys(selection);
            var keys = selectedKeys.Length > 0
                ? selectedKeys
                : result.RouteOptions.Where(option => option.CitySlug == city.City.CitySlug)
                    .Select(option => option.RouteJoinKey).OrderBy(key => key, StringComparer.Ordinal).ToArray();
            foreach (var key in keys)
            {
                indexes.RouteOptionsByCityKey.TryGetValue((city.City.CitySlug, key), out var routeOption);
                indexes.RouteMeasuresByCityWindowKey.TryGetValue((city.City.CitySlug, "today", key), out var measure);
                var routeCoverage = BuildRouteBubbleCoverage(city, key, measure, routeOption, indexes);
                bubbles.Add(new StatsBubbleStatistic
                {
                    EntityKey = EntityKey("route", city.City.CitySlug, key),
                    EntityKind = "route",
                    CitySlug = city.City.CitySlug,
                    RouteJoinKey = key,
                    Label = routeOption?.Label ?? key,
                    CategoryKey = routeOption?.CategoryKey,
                    DistanceKilometers = measure?.DistanceKilometers,
                    RouteVehicleHours = measure?.RouteVehicleHours,
                    ActiveClockHours = measure?.ActiveClockHours,
                    ActivitySampleNumerator = measure?.ActivitySampleNumerator,
                    SampleDenominator = measure?.SampleDenominator,
                    X = Divide(measure?.DistanceKilometers, measure?.RouteVehicleHours),
                    Y = Divide(measure?.DistanceKilometers, measure?.ActiveClockHours),
                    MeanActiveVehicles = Divide(measure?.ActivitySampleNumerator, measure?.SampleDenominator),
                    Coverage = routeCoverage,
                    OmissionReason = measure is null && city.TodayHoursUtc.Count == 0
                        ? "pending"
                        : OmissionReason(measure?.DistanceKilometers, measure?.RouteVehicleHours, measure?.ActiveClockHours, measure?.SampleDenominator, routeOption is null)
                });
            }
        }

        if (selection.Scope == "all") foreach (var city in plan.Cities)
        {
            var cityHours = result.CityHours.Where(row => row.CitySlug == city.City.CitySlug && row.Window == "today").ToArray();
            var coverage = todayCoverage[city.City.CitySlug];
            decimal? distance = cityHours.Length == 0 ? null : cityHours.Sum(row => row.DistanceKilometers);
            long? routeVehicleHours = cityHours.Length == 0 ? null : cityHours.Sum(row => row.RouteVehicleHours);
            long? activeClockHours = cityHours.Length == 0 ? null : cityHours.Sum(row => row.ActiveClockHours);
            long? sampleNumerator = cityHours.Length == 0 ? null : cityHours.Sum(row => row.ActivitySampleNumerator);
            long? sampleDenominator = cityHours.Length == 0 ? null : cityHours.Sum(row => row.SampleDenominator);
            bubbles.Add(new StatsBubbleStatistic
            {
                EntityKey = EntityKey("city", city.City.CitySlug, null),
                EntityKind = "city",
                CitySlug = city.City.CitySlug,
                Label = city.City.DisplayNameKey,
                DistanceKilometers = distance,
                RouteVehicleHours = routeVehicleHours,
                ActiveClockHours = activeClockHours,
                ActivitySampleNumerator = sampleNumerator,
                SampleDenominator = sampleDenominator,
                X = Divide(distance, routeVehicleHours),
                Y = Divide(distance, activeClockHours),
                MeanActiveVehicles = Divide(sampleNumerator, sampleDenominator),
                Coverage = coverage,
                OmissionReason = cityHours.Length == 0 && city.TodayHoursUtc.Count == 0
                    ? "pending"
                    : OmissionReason(distance, routeVehicleHours, activeClockHours, sampleDenominator, missing: false)
            });
        }
        return bubbles;
    }

    static StatsCoverage BuildRouteBubbleCoverage(
        StatsResolvedCityWindow city,
        string routeKey,
        StatsOverlayRouteMeasure? measure,
        StatsOverlayRouteOption? option,
        ProjectionIndexes indexes)
    {
        var expected = city.TodayHoursUtc.Count;
        var available = measure?.AvailableHours ?? 0;
        var rows = indexes.Cohorts(city.City.CitySlug, "today");
        var exclusionCounts = rows.Where(row => !row.IsEligible).GroupBy(row => SafeReason(row.Reason), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var reasons = rows.Where(row => !row.IsEligible).Select(row => SafeReason(row.Reason)).ToHashSet(StringComparer.Ordinal);
        if (option is null) reasons.Add("unknown_route");
        else if (available < rows.Count(row => row.IsEligible)) reasons.Add("route_not_retained");
        var eligibleRows = rows.Where(row => row.IsEligible).ToArray();
        if (option is null && eligibleRows.Length > 0)
            exclusionCounts["unknown_route"] = eligibleRows.Length;
        else if (option is not null && eligibleRows.Length > available)
            exclusionCounts["route_not_retained"] = eligibleRows.Length - available;
        var routeCoverage = new StatsSelectedRouteCoverage(city.City.CitySlug, routeKey, expected, available,
            reasons.OrderBy(reason => reason, StringComparer.Ordinal).ToArray());
        var state = CoverageState(expected, available, available,
            city.BoundaryExcludedTodayHours > 0);
        return new StatsCoverage
        {
            State = state,
            ExpectedClosedHours = expected,
            ContributingHours = available,
            FullyCoveredHours = available,
            ExpectedRouteHours = expected,
            AvailableRouteHours = available,
            SelectedRouteCoverage = [routeCoverage],
            Exclusions = exclusionCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new StatsExclusion(pair.Key, pair.Value)).ToArray(),
            BoundaryExcludedHours = city.BoundaryExcludedTodayHours,
            FirstContributingUtc = measure is null ? null : DateTime.SpecifyKind(measure.FirstHourStartUtc, DateTimeKind.Utc),
            LastContributingUtc = measure is null ? null : DateTime.SpecifyKind(measure.LastHourStartUtc.AddHours(1), DateTimeKind.Utc),
            PendingThroughUtc = city.EligibleTodayEndUtc ?? city.RequestedTodayEndUtc,
            HistoricalMembershipUnknown = option is null
        };
    }

    static StatsTimeline BuildTimeline(
        StatsSnapshotPlan plan,
        StatsOverlayRequest selection,
        StatsOverlayQueryResult result,
        StatsOverlayOptions options)
    {
        var isCity = selection.Scope == "city";
        var categories = result.Categories.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        var timelineCities = plan.Cities.Select(city => (Window: city, Hours: FullDayHours(city))).ToArray();
        var extent = plan.Cities.Count == 0 ? 0m : plan.Cities.Max(city => (decimal)(city.LocalDayEndUtc - city.LocalDayStartUtc).TotalHours);
        if (isCity)
        {
            var city = plan.Cities.Single();
            var series = categories.Select(category => BuildCategorySeries(city, category, selection, result, plan.GeneratedAtUtc)).ToArray();
            return new StatsTimeline { SeriesKind = "category", Stacked = true, ElapsedHourExtent = extent, Series = series };
        }

        var citySeries = timelineCities.Select(row => BuildCitySeries(row.Window, row.Hours, result, plan.GeneratedAtUtc)).ToArray();
        return new StatsTimeline { SeriesKind = "city", Stacked = false, ElapsedHourExtent = extent, Series = citySeries };
    }

    static StatsTimelineSeries BuildCategorySeries(
        StatsResolvedCityWindow city,
        string category,
        StatsOverlayRequest selection,
        StatsOverlayQueryResult result,
        DateTime generatedAtUtc)
    {
        var fullDayHours = FullDayHours(city);
        var categoriesByHour = result.CategoryHours.Where(row => row.CitySlug == city.City.CitySlug
                && row.Window == "today" && row.CategoryKey == category)
            .ToDictionary(row => row.HourStartUtc, row => row.Kilometers);
        var cohortByHour = result.CohortHours.Where(row => row.CitySlug == city.City.CitySlug && row.IsToday)
            .ToDictionary(row => row.HourStartUtc);
        var knownPrefix = (decimal?)null;
        var buckets = new List<StatsTimelineBucket>(fullDayHours.Length);
        foreach (var hour in fullDayHours)
        {
            cohortByHour.TryGetValue(hour, out var cohort);
            var status = BucketStatus(hour, cohort, city, selection, generatedAtUtc, out var reason, out var hasContribution, out var fullyCovered);
            decimal? hourlyKnown = null;
            if (hasContribution)
            {
                hourlyKnown = categoriesByHour.TryGetValue(hour, out var value) ? value : 0m;
                knownPrefix = (knownPrefix ?? 0m) + hourlyKnown.Value;
            }
            var notMeasuredYet = status == "pending" || reason == "future";
            var plot = fullyCovered && !notMeasuredYet ? knownPrefix : null;
            buckets.Add(CreateBucket(city, hour, status, reason, hourlyKnown, notMeasuredYet ? null : knownPrefix, plot,
                RouteKeys(selection).Length == 0 ? null : cohort?.SelectedRouteCount,
                RouteKeys(selection).Length == 0 ? null : RouteKeys(selection).Length));
        }

        return new StatsTimelineSeries
        {
            Key = "category:" + category,
            CategoryKey = category,
            WindowReference = "today",
            Buckets = buckets
        };
    }

    static StatsTimelineSeries BuildCitySeries(
        StatsResolvedCityWindow city,
        DateTime[] fullDayHours,
        StatsOverlayQueryResult result,
        DateTime generatedAtUtc)
    {
        var cityByHour = result.CityHours.Where(row => row.CitySlug == city.City.CitySlug && row.Window == "today")
            .ToDictionary(row => row.HourStartUtc);
        var cohortByHour = result.CohortHours.Where(row => row.CitySlug == city.City.CitySlug && row.IsToday)
            .ToDictionary(row => row.HourStartUtc);
        var knownPrefix = (decimal?)null;
        var buckets = new List<StatsTimelineBucket>(fullDayHours.Length);
        foreach (var hour in fullDayHours)
        {
            cohortByHour.TryGetValue(hour, out var cohort);
            cityByHour.TryGetValue(hour, out var cityMeasure);
            var status = BucketStatus(hour, cohort, city, null, generatedAtUtc, out var reason, out var hasContribution, out var fullyCovered);
            decimal? hourlyKnown = hasContribution ? cityMeasure?.DistanceKilometers : null;
            if (hourlyKnown is { } amount)
                knownPrefix = (knownPrefix ?? 0m) + amount;
            var notMeasuredYet = status == "pending" || reason == "future";
            var plot = fullyCovered && !notMeasuredYet ? knownPrefix : null;
            buckets.Add(CreateBucket(city, hour, status, reason, notMeasuredYet ? null : hourlyKnown,
                notMeasuredYet ? null : knownPrefix, plot, null, null));
        }

        return new StatsTimelineSeries
        {
            Key = "city:" + city.City.CitySlug,
            CitySlug = city.City.CitySlug,
            WindowReference = "today",
            Buckets = buckets
        };
    }

    static string BucketStatus(
        DateTime hour,
        StatsOverlayCohortHour? cohort,
        StatsResolvedCityWindow city,
        StatsOverlayRequest? selection,
        DateTime generatedAtUtc,
        out string? reason,
        out bool hasContribution,
        out bool fullyCovered)
    {
        hasContribution = false;
        fullyCovered = false;
        if (cohort is null)
        {
            if (hour < city.RequestedTodayEndUtc)
            {
                reason = "missing_cohort";
                return "unavailable";
            }
            if (hour < generatedAtUtc)
            {
                reason = "pending";
                return "pending";
            }
            reason = "future";
            return "unavailable";
        }
        if (!cohort.IsEligible)
        {
            reason = SafeReason(cohort.Reason);
            return "unavailable";
        }

        var selectedKeys = selection is null ? [] : RouteKeys(selection);
        var selectedCount = selectedKeys.Length;
        var availableCount = selection is null || selectedCount == 0 ? cohort.RouteCount : cohort.SelectedRouteCount;
        hasContribution = availableCount > 0;
        fullyCovered = selectedCount == 0 || cohort.SelectedRouteCount == selectedCount;
        reason = fullyCovered ? null : selectedCount > 0 && availableCount > 0 ? "route_not_retained" : "unknown_route";
        return fullyCovered ? "complete" : "partial";
    }

    static StatsTimelineBucket CreateBucket(
        StatsResolvedCityWindow city,
        DateTime hour,
        string state,
        string? reason,
        decimal? hourlyKnown,
        decimal? cumulativeKnown,
        decimal? plot,
        int? availableRoutes,
        int? expectedRoutes)
    {
        var endUtc = hour.AddHours(1);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(city.City.TimeZoneId);
        var localEnd = TimeZoneInfo.ConvertTime(new DateTimeOffset(endUtc), zone);
        var offsetMinutes = (int)localEnd.Offset.TotalMinutes;
        var sign = offsetMinutes < 0 ? "-" : "+";
        var absoluteOffset = Math.Abs(offsetMinutes);
        var offsetText = string.Create(CultureInfo.InvariantCulture, $"{sign}{absoluteOffset / 60:00}:{absoluteOffset % 60:00}");
        return new StatsTimelineBucket
        {
            StartUtc = hour,
            EndUtc = endUtc,
            LocalDate = DateOnly.FromDateTime(localEnd.DateTime),
            LocalEndLabel = localEnd.ToString("HH:mm", CultureInfo.InvariantCulture),
            UtcOffset = offsetText,
            ElapsedHours = (decimal)(endUtc - city.LocalDayStartUtc).TotalHours,
            HourlyKnownKilometers = hourlyKnown,
            CumulativeObservedKilometers = cumulativeKnown,
            PlotCumulativeKilometers = plot,
            AvailableSelectedRoutes = availableRoutes,
            ExpectedSelectedRoutes = expectedRoutes,
            CoverageState = state,
            Reason = reason
        };
    }

    static DateTime[] FullDayHours(StatsResolvedCityWindow city)
    {
        var hour = CeilingUtcHour(city.LocalDayStartUtc);
        var hours = new List<DateTime>();
        while (hour.AddHours(1) <= city.LocalDayEndUtc)
        {
            hours.Add(hour);
            hour = hour.AddHours(1);
        }
        return hours.ToArray();
    }

    static DateTime CeilingUtcHour(DateTime value)
    {
        var floor = StatsOverlayPolicy.FloorUtcHour(value);
        return floor == value ? floor : floor.AddHours(1);
    }

    static IReadOnlyList<string> BuildWarnings(
        StatsSnapshotPlan plan,
        StatsOverlayRequest selection,
        StatsOverlayQueryResult result,
        IReadOnlyList<StatsCityWindow> cityWindows)
    {
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        if (cityWindows.Any(city => city.TodayCoverage.State == "partial" || city.TodayCoverage.State == "unavailable"
            || city.Last24Coverage.State == "partial" || city.Last24Coverage.State == "unavailable"))
            warnings.Add("partial_coverage");
        if (cityWindows.Any(city => city.TodayCoverage.BoundaryExcludedHours > 0)) warnings.Add("boundary_excluded");
        if (cityWindows.Any(city => city.TodayCoverage.HistoricalMembershipUnknown || city.Last24Coverage.HistoricalMembershipUnknown))
            warnings.Add("historical_membership_unknown");
        if (result.RouteOptions.Any(option => option.CategoryChanged)) warnings.Add("catalog_changed");
        if (selection.Scope == "all" && plan.Cities.Select(city => city.LocalDate).Distinct().Count() > 1)
            warnings.Add("city_days_differ");
        if (selection.Scope == "city")
        {
            var todayMeasures = result.RouteMeasures.Where(measure => measure.Window == "today").ToArray();
            var differingPeriods = todayMeasures
                .Select(measure => (measure.AvailableHours, measure.FirstHourStartUtc, measure.LastHourStartUtc))
                .Distinct().Count() > 1;
            var missingTodayMeasure = result.RouteOptions.Count > todayMeasures.Length;
            if (differingPeriods || todayMeasures.Length > 0 && missingTodayMeasure)
                warnings.Add("different_route_periods");
        }
        return warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    static string CoverageState(int expected, int contributing, int full, bool boundaryExcluded)
    {
        if (expected == 0) return "pending";
        if (contributing == 0) return "unavailable";
        return full == expected && !boundaryExcluded ? "complete" : "partial";
    }

    static string SafeReason(string reason) => SafeReasons.Contains(reason) ? reason : "inconsistent_cohort";

    static void AddReason(IDictionary<string, int> counts, string reason, int amount = 1)
    {
        counts.TryGetValue(reason, out var existing);
        counts[reason] = existing + amount;
    }

    static decimal? Divide(decimal? numerator, long? denominator) => numerator is { } value && denominator is > 0
        ? value / denominator.Value
        : null;

    static string? OmissionReason(decimal? distance, long? routeHours, long? activeHours, long? sampleCount, bool missing)
    {
        if (missing) return "unknown_route";
        if (distance is null) return "missing_cohort";
        if (sampleCount is null or <= 0) return "zero_denominator";
        if (activeHours is null or <= 0) return "no_active_vehicles";
        if (routeHours is null or <= 0) return "zero_denominator";
        return null;
    }

    static string EntityKey(string kind, string city, string? route)
    {
        var value = route is null ? city : city + "\0" + route;
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return kind + ":" + token;
    }

    static void EnsureSafeNumbers(StatsOverlayResponse response)
    {
        static bool Unsafe(long? value) => value is < 0 or > MaxJavaScriptExactInteger;
        static void Check(StatsCoverage coverage)
        {
            if (Unsafe(coverage.ExpectedRouteHours) || Unsafe(coverage.AvailableRouteHours)
                || coverage.SelectedRouteCoverage?.Any(route => route.ExpectedHours < 0 || route.AvailableHours < 0) == true)
                throw new StatsOverlayRequestException("scope_too_large", 422);
        }

        Check(response.TodayDistance.Coverage);
        Check(response.Last24Distance.Coverage);
        foreach (var city in response.Cities)
        {
            Check(city.TodayCoverage);
            Check(city.Last24Coverage);
        }
        foreach (var bubble in response.Bubbles)
            if (Unsafe(bubble.RouteVehicleHours) || Unsafe(bubble.ActiveClockHours)
                || Unsafe(bubble.ActivitySampleNumerator) || Unsafe(bubble.SampleDenominator))
                throw new StatsOverlayRequestException("scope_too_large", 422);
        CheckReported(response.TodayReportedVehicles);
        CheckReported(response.Last24ReportedVehicles);

        static void CheckReported(StatsReportedVehicleSummary summary)
        {
            if (Unsafe(summary.Count) || summary.ExpectedCityHours < 0 || summary.ContributingCityHours < 0
                || summary.FullyCoveredCityHours < 0 || summary.ExpectedCityCount < 0 || summary.ContributingCityCount < 0)
                throw new StatsOverlayRequestException("scope_too_large", 422);
            foreach (var city in summary.Cities)
                if (Unsafe(city.Count) || Unsafe(city.UnidentifiableRecordCount) || Unsafe(city.FallbackRecordCount)
                    || city.ExpectedHours < 0 || city.ContributingHours < 0 || city.FullyCoveredHours < 0
                    || city.BoundaryExcludedHours < 0 || city.Exclusions.Any(exclusion => exclusion.Hours < 0))
                    throw new StatsOverlayRequestException("scope_too_large", 422);
        }
    }
}
