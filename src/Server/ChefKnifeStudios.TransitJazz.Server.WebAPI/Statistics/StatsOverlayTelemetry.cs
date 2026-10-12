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
        Requests.Add(1, tags);
        RequestDuration.Record(Math.Max(0, elapsed.TotalSeconds), tags);
        RequestBytes.Record(Math.Max(0, requestBytes), tags);
        if (boundedCode != "none")
        {
            var rejectionTags = new TagList { { "scope", boundedScope }, { "code", boundedCode } };
            Rejections.Add(1, rejectionTags);
        }
    }

    public static void RecordCacheEvent(string? scope, string result)
    {
        var boundedResult = result is "hit" or "miss" or "coalesced" or "uncached" or "expired" ? result : "other";
        CacheEvents.Add(1, new TagList { { "scope", NormalizeScope(scope) }, { "result", boundedResult } });
    }

    public static void RecordReadDuration(string? scope, TimeSpan elapsed) =>
        ReadDuration.Record(Math.Max(0, elapsed.TotalSeconds), new TagList { { "scope", NormalizeScope(scope) } });

    public static void RecordResponseBytes(string? scope, long bytes) =>
        ResponseBytes.Record(Math.Max(0, bytes), new TagList { { "scope", NormalizeScope(scope) } });

    static string NormalizeScope(string? scope) => scope switch
    {
        "all" => "all",
        "city" => "city",
        _ => "unknown"
    };

    static string NormalizeStatus(string? status) => status is "200" or "400" or "413" or "415" or "422" or "429" or "503" or "cancelled"
        ? status
        : "other";

    static string NormalizeCode(string? code) => code is not null && SafeCodes.Contains(code) ? code : "other";
}
