using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services;

/// <summary>Logs bounded Stats retrieval aggregates and optionally forwards them to Umami.</summary>
public sealed class StatsSnapshotDiagnostics : IStatsSnapshotDiagnostics
{
    static readonly string[] EventNames =
    [
        "stats_preparation_complete",
        "stats_warm_open",
        "stats_loader",
        "stats_snapshot_age_on_open",
        "stats_admission_skipped",
        "stats_failure"
    ];

    readonly ILogger<StatsSnapshotDiagnostics> logger;
    readonly IJSRuntime? jsRuntime;

    public StatsSnapshotDiagnostics(ILogger<StatsSnapshotDiagnostics> logger, IJSRuntime? jsRuntime = null)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.jsRuntime = jsRuntime;
    }

    public void Record(StatsSnapshotDiagnosticEvent diagnosticEvent, StatsSnapshotAgeBucket? ageBucket = null)
    {
        var eventIndex = (int)diagnosticEvent;
        if ((uint)eventIndex >= (uint)EventNames.Length) return;
        var eventName = EventNames[eventIndex];
        var age = ageBucket?.ToString();

        try
        {
            logger.LogInformation("Stats retrieval event {EventName}; age bucket {AgeBucket}", eventName, age);
        }
        catch (Exception) { }

        if (jsRuntime is not null) _ = SendToUmamiObservedAsync(eventName, age);
    }

    async Task SendToUmamiObservedAsync(string eventName, string? ageBucket)
    {
        try
        {
            await jsRuntime!.InvokeVoidAsync("trackStatsSnapshotEvent", eventName, ageBucket);
        }
        catch (Exception) { }
    }
}
