using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Geospatial;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProtoBuf;
using System.Net.Http.Headers;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;

/// <summary>Combines KCM and Sound Transit while keeping source access and raw capture separate.</summary>
public sealed class SeattleCity(
    CityConfig config,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<SeattleCity> logger,
    TimeProvider? timeProvider = null,
    bool captureReportedVehicles = false,
    ReportedVehicleBufferBudget? reportedVehicleBufferBudget = null,
    ReportedVehicleHistoryOptions? reportedVehicleHistoryOptions = null) : ITransitCity
{
    public const string SoundTransitHttpClientName = "SeattleSoundTransit";
    public const string SoundTransitApiKeySetting = "SOUND_TRANSIT_OBA_API_KEY";
    const string KcmSourceName = "KCM";
    const string SoundTransitSourceName = "Sound Transit agency 40";
    const int MaxOverlapAgeSeconds = 120;
    const int MaxOverlapTimeDifferenceSeconds = 120;
    const double MaxOverlapDistanceMeters = 5_000;
    const int MaxRememberedOverlapAliases = 10_000;
    static readonly TimeSpan OverlapAliasLifetime = TimeSpan.FromMinutes(10);

    static readonly IReadOnlyDictionary<string, string> SharedRouteAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["100451"] = "556",
            ["102767"] = "547",
            ["556"] = "556",
            ["547"] = "547",
        };

    readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    readonly Dictionary<OverlapKey, DateTimeOffset> _rememberedOverlapAliases = [];

    public string Name => config.Name;

    public async Task<CityFetchResult> FetchVehiclesAsync(CancellationToken ct)
    {
        ValidateConfiguration();
        if (captureReportedVehicles && config.ReportedVehicleSources.Length != config.GtfsRtUrls.Length)
            throw new InvalidOperationException("Seattle reported-source policies must match both realtime endpoints.");

        var apiKey = string.IsNullOrWhiteSpace(config.SeattleSoundTransitApiKeyEnvVar)
            ? null
            : configuration[config.SeattleSoundTransitApiKeyEnvVar];

        var results = await Task.WhenAll(
            FetchSourceAsync(KcmSourceName, config.GtfsRtUrls[0], isSoundTransit: false, apiKey: null,
                SourcePolicy(0), ct),
            FetchSourceAsync(SoundTransitSourceName, config.GtfsRtUrls[1], isSoundTransit: true, apiKey,
                SourcePolicy(1), ct));

        var kcm = results[0];
        var soundTransit = results[1];
        var feed = SelectProcessedFeed(kcm.Feed, soundTransit.Feed);
        return CityFetchResult.FromSources(feed,
            results.Count(result => result.Succeeded), results.Count(result => !result.Succeeded),
            reportedVehicleObservations: results.Where(result => result.Observation is not null)
                .Select(result => result.Observation!));
    }

    ReportedVehicleSourceDefinition? SourcePolicy(int index) => captureReportedVehicles
        ? config.ReportedVehicleSources[index].ToDefinition()
        : null;

    async Task<SourceResult> FetchSourceAsync(string sourceName, string url, bool isSoundTransit,
        string? apiKey, ReportedVehicleSourceDefinition? sourcePolicy, CancellationToken ct)
    {
        if (isSoundTransit && string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Seattle source {Source} is unavailable because its credential is not configured.", sourceName);
            return FailedResult(sourcePolicy, "source_failure");
        }

        try
        {
            var client = httpClientFactory.CreateClient(isSoundTransit ? SoundTransitHttpClientName : string.Empty);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                isSoundTransit ? AddApiKey(url, apiKey!) : url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TransitJazz", "1.0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Seattle source {Source} returned HTTP {StatusCode}.", sourceName, (int)response.StatusCode);
                return FailedResult(sourcePolicy, "source_failure");
            }

            FeedMessage feed;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                feed = Serializer.Deserialize<FeedMessage>(stream)
                    ?? throw new InvalidDataException("The GTFS-RT response was empty.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Seattle source {Source} returned an unreadable GTFS-RT response; exception type {ExceptionType}.",
                    sourceName, StructuredLogRedactor.SafeExceptionType(ex));
                return FailedResult(sourcePolicy, "parse_failure");
            }

            // Receipt belongs to the decoded response, before route mapping or display selection.
            var receiptUtc = _timeProvider.GetUtcNow();
            var observation = sourcePolicy is null ? null : CreateSuccessfulObservation(sourcePolicy, receiptUtc, feed);
            return new SourceResult(feed, true, observation);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Seattle source {Source} request failed; exception type {ExceptionType}.",
                sourceName, StructuredLogRedactor.SafeExceptionType(ex));
            return FailedResult(sourcePolicy, "source_failure");
        }
    }

    ReportedVehicleSourceObservation? CreateSuccessfulObservation(ReportedVehicleSourceDefinition source,
        DateTimeOffset receiptUtc, FeedMessage feed)
    {
        var identities = feed.Entities
            .Where(entity => entity.IsDeleted != true && entity.Vehicle is not null)
            .Select(entity => entity.Vehicle!.Vehicle?.Id);
        return reportedVehicleBufferBudget is not null && reportedVehicleHistoryOptions is not null
            ? ReportedVehicleSourceObservation.Successful(source, receiptUtc, identities,
                reportedVehicleHistoryOptions, reportedVehicleBufferBudget)
            : ReportedVehicleSourceObservation.Successful(source, receiptUtc, identities);
    }

    SourceResult FailedResult(ReportedVehicleSourceDefinition? source, string failureCode) =>
        new(new FeedMessage(), false, source is null
            ? null
            : ReportedVehicleSourceObservation.Failed(source, _timeProvider.GetUtcNow(), failureCode));

    FeedMessage SelectProcessedFeed(FeedMessage kcm, FeedMessage soundTransit)
    {
        var now = _timeProvider.GetUtcNow();
        PruneRememberedAliases(now);
        var kcmVehicles = kcm.Entities.Where(IsVehicleEntity).ToArray();
        var stVehicles = soundTransit.Entities.Where(IsVehicleEntity).ToArray();
        var withheld = new HashSet<FeedEntity>();
        var pairedSt = new HashSet<FeedEntity>();
        var withheldMissingEvidence = 0;
        var withheldConflicts = 0;
        var withheldAmbiguous = 0;

        var kcmGroups = kcmVehicles
            .Select(entity => (Entity: entity, Key: GetOverlapKey(entity)))
            .Where(item => item.Key is not null)
            .GroupBy(item => item.Key!.Value)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Entity).ToArray());
        var stGroups = stVehicles
            .Select(entity => (Entity: entity, Key: GetOverlapKey(entity)))
            .Where(item => item.Key is not null)
            .GroupBy(item => item.Key!.Value)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Entity).ToArray());

        foreach (var (key, stCandidates) in stGroups)
        {
            if (!kcmGroups.TryGetValue(key, out var possibleKcm))
                continue;

            if (possibleKcm.Length != 1 || stCandidates.Length != 1)
            {
                foreach (var candidate in possibleKcm) withheld.Add(candidate);
                foreach (var candidate in stCandidates) withheld.Add(candidate);
                withheldAmbiguous++;
                continue;
            }

            var kcmEntity = possibleKcm[0];
            var stEntity = stCandidates[0];
            if (!TryGetComparison(kcmEntity, now, out var kcmPosition, out var kcmTimestamp)
                || !TryGetComparison(stEntity, now, out var stPosition, out var stTimestamp))
            {
                withheld.Add(kcmEntity);
                withheld.Add(stEntity);
                withheldMissingEvidence++;
                continue;
            }

            if (Math.Abs((kcmTimestamp - stTimestamp).TotalSeconds) > MaxOverlapTimeDifferenceSeconds
                || HaversineCalculator.DistanceMeters(kcmPosition.Latitude, kcmPosition.Longitude,
                    stPosition.Latitude, stPosition.Longitude) > MaxOverlapDistanceMeters)
            {
                withheld.Add(kcmEntity);
                withheld.Add(stEntity);
                withheldConflicts++;
                continue;
            }

            pairedSt.Add(stEntity);
            RememberAlias(key, now);
        }

        if (withheldMissingEvidence + withheldConflicts + withheldAmbiguous > 0)
        {
            logger.LogWarning("Seattle withheld ambiguous shared-route observations: missingEvidence={MissingEvidence}, "
                + "conflictingEvidence={ConflictingEvidence}, ambiguousGroups={AmbiguousGroups}.",
                withheldMissingEvidence, withheldConflicts, withheldAmbiguous);
        }

        var selected = new FeedMessage { Header = kcm.Header ?? soundTransit.Header };
        foreach (var entity in kcm.Entities)
        {
            if (withheld.Contains(entity) || entity.IsDeleted == true) continue;
            var route = MapRouteId(entity.Vehicle?.Trip?.RouteId);
            if (route == "512") continue;
            if (entity.Vehicle is not null)
                QualifyDisplayIdentity(entity, "kcm", GetOverlapKey(entity) is { } key && HasRememberedAlias(key, now));
            if (entity.Vehicle?.Trip is { } trip && route is not null) trip.RouteId = route;
            selected.Entities.Add(entity);
        }

        foreach (var entity in soundTransit.Entities)
        {
            if (withheld.Contains(entity) || pairedSt.Contains(entity) || entity.IsDeleted == true) continue;
            var route = MapRouteId(entity.Vehicle?.Trip?.RouteId);
            if (route == "512") continue;
            if (entity.Vehicle is not null)
                QualifyDisplayIdentity(entity, "st", GetOverlapKey(entity) is { } key && HasRememberedAlias(key, now));
            if (entity.Vehicle?.Trip is { } trip && route is not null) trip.RouteId = route;
            selected.Entities.Add(entity);
        }

        return selected;
    }

    static bool IsVehicleEntity(FeedEntity entity) => entity.IsDeleted != true && entity.Vehicle is not null;

    static OverlapKey? GetOverlapKey(FeedEntity entity)
    {
        var id = entity.Vehicle?.Vehicle?.Id;
        var route = GetSharedRoute(entity.Vehicle?.Trip?.RouteId);
        return string.IsNullOrWhiteSpace(id) || route is null ? null : new OverlapKey(route, id);
    }

    static string? GetSharedRoute(string? routeId)
    {
        if (string.IsNullOrWhiteSpace(routeId)) return null;
        return SharedRouteAliases.TryGetValue(routeId, out var mapped) ? mapped : null;
    }

    static string? MapRouteId(string? routeId)
    {
        if (string.IsNullOrWhiteSpace(routeId)) return routeId;
        if (SharedRouteAliases.TryGetValue(routeId, out var sharedRoute)) return sharedRoute;
        if (string.Equals(routeId, "100479", StringComparison.OrdinalIgnoreCase)) return "1 Line";
        if (string.Equals(routeId, "2LINE", StringComparison.OrdinalIgnoreCase)) return "2 Line";
        return routeId;
    }

    bool TryGetComparison(FeedEntity entity, DateTimeOffset now, out Position position, out DateTimeOffset timestamp)
    {
        var vehicle = entity.Vehicle;
        position = vehicle?.Position!;
        timestamp = default;
        if (vehicle?.Position is not { } candidate
            || !double.IsFinite(candidate.Latitude) || !double.IsFinite(candidate.Longitude)
            || candidate.Latitude is < -90 or > 90 || candidate.Longitude is < -180 or > 180
            || vehicle.Timestamp is not { } unixSeconds || unixSeconds > long.MaxValue)
            return false;

        timestamp = DateTimeOffset.FromUnixTimeSeconds((long)unixSeconds);
        var age = now - timestamp;
        if (age < TimeSpan.Zero || age > TimeSpan.FromSeconds(MaxOverlapAgeSeconds))
            return false;
        position = candidate;
        return true;
    }

    void QualifyDisplayIdentity(FeedEntity entity, string sourcePrefix, bool shareKnownIdentity)
    {
        var descriptor = entity.Vehicle!.Vehicle;
        var rawId = descriptor?.Id;
        var stateId = string.IsNullOrWhiteSpace(rawId)
            ? $"{sourcePrefix}:{entity.Id}"
            : $"{(shareKnownIdentity ? "kcm" : sourcePrefix)}:{rawId}";
        entity.Id = stateId;
        if (descriptor is not null)
            descriptor.Id = stateId;
    }

    void RememberAlias(OverlapKey key, DateTimeOffset now)
    {
        if (_rememberedOverlapAliases.Count >= MaxRememberedOverlapAliases && !_rememberedOverlapAliases.ContainsKey(key))
            PruneRememberedAliases(now, forceOne: true);
        if (_rememberedOverlapAliases.Count < MaxRememberedOverlapAliases)
            _rememberedOverlapAliases[key] = now;
    }

    bool HasRememberedAlias(OverlapKey key, DateTimeOffset now) =>
        _rememberedOverlapAliases.TryGetValue(key, out var confirmedUtc) && now - confirmedUtc <= OverlapAliasLifetime;

    void PruneRememberedAliases(DateTimeOffset now, bool forceOne = false)
    {
        foreach (var key in _rememberedOverlapAliases
                     .Where(pair => now - pair.Value > OverlapAliasLifetime)
                     .Select(pair => pair.Key).ToArray())
            _rememberedOverlapAliases.Remove(key);
        if (forceOne && _rememberedOverlapAliases.Count > 0)
            _rememberedOverlapAliases.Remove(_rememberedOverlapAliases.MinBy(pair => pair.Value).Key);
    }

    void ValidateConfiguration()
    {
        if (!string.Equals(config.Name, CityNames.Seattle, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SeattleCity requires the seattle city identity.");
        if (config.GtfsRtUrls.Length != 2
            || !string.Equals(config.GtfsRtUrls[0], "https://s3.amazonaws.com/kcm-alerts-realtime-prod/vehiclepositions.pb", StringComparison.Ordinal)
            || !string.Equals(config.GtfsRtUrls[1], "https://api.pugetsound.onebusaway.org/api/gtfs_realtime/vehicle-positions-for-agency/40.pb", StringComparison.Ordinal))
            throw new InvalidOperationException("Seattle requires the two pinned direct-agency realtime endpoints in KCM/ST order.");
        if (config.ApiKeyEnvVar is not null)
            throw new InvalidOperationException("Seattle must not configure a city-wide API credential.");
        if (!string.Equals(config.SeattleSoundTransitApiKeyEnvVar, SoundTransitApiKeySetting, StringComparison.Ordinal))
            throw new InvalidOperationException("Seattle's Sound Transit credential variable name is missing or unsupported.");
    }

    static string AddApiKey(string url, string apiKey)
    {
        var builder = new UriBuilder(url);
        var existing = builder.Query.TrimStart('?');
        var pair = $"key={Uri.EscapeDataString(apiKey)}";
        builder.Query = string.IsNullOrEmpty(existing) ? pair : $"{existing}&{pair}";
        return builder.Uri.AbsoluteUri;
    }

    readonly record struct OverlapKey(string Route, string DescriptorId);
    sealed record SourceResult(FeedMessage Feed, bool Succeeded, ReportedVehicleSourceObservation? Observation);
}
