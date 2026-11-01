using System;
using System.Diagnostics.Metrics;
using System.Diagnostics;
using System.Collections.Generic;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

/// <summary>Bounded Stats read telemetry; labels never contain city or route identities.</summary>
public static class StatsOverlayTelemetry
{
    public const string MeterName = "TransitJazz.StatsOverlay";
    static readonly Meter Meter = new(MeterName, "1.0.0");
    static readonly Counter<long> Requests = Meter.CreateCounter<long>("transitjazz.stats.requests", "{request}");
    static readonly Counter<long> Rejections = Meter.CreateCounter<long>("transitjazz.stats.rejections", "{request}");
    static readonly Counter<long> CacheEvents = Meter.CreateCounter<long>("transitjazz.stats.cache.events", "{event}");
    static readonly UpDownCounter<long> CacheEntries = Meter.CreateUpDownCounter<long>("transitjazz.stats.cache.entries", "{entry}");
    static readonly UpDownCounter<long> CacheEstimatedBytes = Meter.CreateUpDownCounter<long>("transitjazz.stats.cache.estimated_bytes", "By");
    static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>("transitjazz.stats.request.duration", "s");
    static readonly Histogram<double> ReadDuration = Meter.CreateHistogram<double>("transitjazz.stats.read.duration", "s");
    static readonly Histogram<long> RequestBytes = Meter.CreateHistogram<long>("transitjazz.stats.request.bytes", "By");
    static readonly Histogram<long> ResponseBytes = Meter.CreateHistogram<long>("transitjazz.stats.response.bytes", "By");
    static readonly HashSet<string> SafeCodes = new(StringComparer.Ordinal)
    {
        "none", "invalid_request", "invalid_scope", "invalid_city", "invalid_route_key",
        "request_too_large", "unsupported_media_type", "stats_disabled", "stats_unavailable",
        "scope_too_large", "response_too_large", "busy", "read_timeout", "dependency_unavailable",
        "rate_limited", "cancelled"
    };

    public static void RecordEndpointRequest(string? scope, string status, string? code, TimeSpan elapsed, long requestBytes)
    {
        var boundedScope = NormalizeScope(scope);
        var boundedStatus = NormalizeStatus(status);
        var boundedCode = NormalizeCode(code);
        var tags = new TagList
        {
            { "scope", boundedScope },
            { "status", boundedStatus },
            { "result", boundedCode }
        };
        Safely(() =>
        {
            Requests.Add(1, tags);
            RequestDuration.Record(Math.Max(0, elapsed.TotalSeconds), tags);
            RequestBytes.Record(Math.Max(0, requestBytes), tags);
            if (boundedCode != "none")
            {
                var rejectionTags = new TagList { { "scope", boundedScope }, { "code", boundedCode } };
                Rejections.Add(1, rejectionTags);
            }
        });
    }

    public static void RecordCacheEvent(string? scope, string result) =>
        RecordCacheEvent(scope, "unfiltered", result);

    public static void RecordCacheEvent(string? scope, string? selectionKind, string result, string? reason = null)
    {
        var boundedResult = result is "hit" or "miss" or "coalesced" or "eviction" or "admission_skipped" or "uncached" or "expired"
            ? result : "other";
        var tags = new TagList
        {
            { "scope", NormalizeScope(scope) },
            { "selection_kind", NormalizeSelectionKind(selectionKind) },
            { "result", boundedResult }
        };
        if (reason is not null) tags.Add("reason", NormalizeReason(reason));
        Safely(() => CacheEvents.Add(1, tags));
    }

    public static void RecordCacheEntryChange(string? scope, string? selectionKind, long entriesDelta, long estimatedBytesDelta)
    {
        var tags = new TagList
        {
            { "scope", NormalizeScope(scope) },
            { "selection_kind", NormalizeSelectionKind(selectionKind) }
        };
        Safely(() =>
        {
            if (entriesDelta != 0) CacheEntries.Add(entriesDelta, tags);
            if (estimatedBytesDelta != 0) CacheEstimatedBytes.Add(estimatedBytesDelta, tags);
        });
    }

    public static void RecordReadDuration(string? scope, TimeSpan elapsed) =>
        RecordReadDuration(scope, "unfiltered", elapsed);

    public static void RecordReadDuration(string? scope, string? selectionKind, TimeSpan elapsed)
    {
        var tags = new TagList
        {
            { "scope", NormalizeScope(scope) },
            { "selection_kind", NormalizeSelectionKind(selectionKind) }
        };
        Safely(() => ReadDuration.Record(Math.Max(0, elapsed.TotalSeconds), tags));
    }

    public static void RecordResponseBytes(string? scope, long bytes) =>
        Safely(() => ResponseBytes.Record(Math.Max(0, bytes), new TagList { { "scope", NormalizeScope(scope) } }));

    static string NormalizeScope(string? scope) => scope switch
    {
        "all" => "all",
        "city" => "city",
        _ => "unknown"
    };

    static string NormalizeStatus(string? status) => status is "200" or "400" or "413" or "415" or "422" or "429" or "503" or "cancelled"
        ? status
        : "other";

    static string NormalizeSelectionKind(string? selectionKind) => selectionKind == "filtered" ? "filtered" : "unfiltered";

    static string NormalizeReason(string reason) => reason switch
    {
        "filtered_lru" or "oldest_baseline" or "expired" or "identity_changed" or "replacement" or "clock_rollback"
            or "total_nonfit" or "filtered_nonfit" or "filtered_retention_disabled"
            or "baseline_protection" or "expired_before_admission" or "newer_generation" => reason,
        _ => "other"
    };

    static string NormalizeCode(string? code) => code is not null && SafeCodes.Contains(code) ? code : "other";

    static void Safely(Action record)
    {
        try { record(); }
        catch (Exception) { }
    }
}
