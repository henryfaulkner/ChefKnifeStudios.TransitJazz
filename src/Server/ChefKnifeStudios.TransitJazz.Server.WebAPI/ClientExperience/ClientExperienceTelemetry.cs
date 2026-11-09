using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.ClientExperience;

public static class ClientExperienceTelemetry
{
    public const string MeterName = "TransitJazz.ClientExperience";
    static readonly Meter Meter = new(MeterName, "1.0.0");
    static readonly Counter<long> Requests = Meter.CreateCounter<long>("client_experience.intake.requests");
    static readonly Counter<long> Events = Meter.CreateCounter<long>("client_experience.intake.events");
    static readonly Counter<long> Lost = Meter.CreateCounter<long>("client_experience.client.loss");
    static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("client_experience.intake.duration", "ms");
    static readonly Counter<long> MaintenanceRuns = Meter.CreateCounter<long>("client_experience.maintenance.runs");
    static readonly Histogram<double> MaintenanceLag = Meter.CreateHistogram<double>("client_experience.maintenance.lag", "s");
    static readonly Counter<long> ProbeRuns = Meter.CreateCounter<long>("client_experience.probe.runs");
    static readonly Histogram<double> ProbeDuration = Meter.CreateHistogram<double>("client_experience.probe.duration", "ms");

    public static void RecordRequest(string outcome, TimeSpan elapsed)
    {
        Requests.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        Duration.Record(elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public static void RecordEvents(int accepted, int duplicates, int rejected)
    {
        if (accepted > 0) Events.Add(accepted, new KeyValuePair<string, object?>("result", "accepted"));
        if (duplicates > 0) Events.Add(duplicates, new KeyValuePair<string, object?>("result", "duplicate"));
        if (rejected > 0) Events.Add(rejected, new KeyValuePair<string, object?>("result", "rejected"));
    }

    public static void RecordLoss(int count, string eventKind, string reason)
    {
        if (count > 0)
            Lost.Add(count, new KeyValuePair<string, object?>("event_kind", eventKind), new KeyValuePair<string, object?>("reason", reason));
    }

    public static void RecordRateLimited()
        => Requests.Add(1, new KeyValuePair<string, object?>("outcome", "rate_limited"));

    public static void RecordMaintenance(string outcome, double lagSeconds = 0)
    {
        MaintenanceRuns.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        MaintenanceLag.Record(Math.Clamp(lagSeconds, 0, 31_536_000), new KeyValuePair<string, object?>("outcome", outcome));
    }

    public static void RecordProbe(string outcome, double elapsedMilliseconds)
    {
        ProbeRuns.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        ProbeDuration.Record(Math.Clamp(elapsedMilliseconds, 0, 10_000), new KeyValuePair<string, object?>("outcome", outcome));
    }
}
