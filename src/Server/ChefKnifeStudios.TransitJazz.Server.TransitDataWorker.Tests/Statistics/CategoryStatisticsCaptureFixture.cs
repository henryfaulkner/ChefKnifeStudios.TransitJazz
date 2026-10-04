using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class CategoryStatisticsCaptureFixture
{
    public const string City = "atlanta";
    public const string Category = "bus";
    public readonly DateTimeOffset Start = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    public readonly StatisticsTimeProvider Clock;
    public readonly RecordingCategoryStatisticsSink Sink;
    public readonly CityCategoryStatisticsCapture Capture;

    public CategoryStatisticsCaptureFixture(bool accept = true)
    {
        Clock = new StatisticsTimeProvider(Start);
        Sink = new RecordingCategoryStatisticsSink { Accept = accept };
        Capture = new CityCategoryStatisticsCapture(
            new CityCategoryInsightsOptions
            {
                Enabled = true,
                MaxObservationGapSeconds = 30,
            },
            Sink,
            Clock,
            NullLogger<CityCategoryStatisticsCapture>.Instance);
    }

    public CityCategoryStatisticsCycle Begin(long geometryGeneration = 1, params string[] categories) =>
        Capture.BeginCycle(City, categories.Length == 0 ? [Category] : categories, geometryGeneration)!;

    public void Complete(CityCategoryStatisticsCycle cycle, DateTimeOffset at, bool activityEligible = true,
        bool publicationKnown = true, bool failed = false)
    {
        Clock.SetUtcNow(at);
        Capture.CompleteCycle(cycle, activityEligible, publicationKnown, failed, at.UtcDateTime);
    }

    public static CategoryCycleSnapshot Snapshot(DateTime atUtc, string category = Category,
        IEnumerable<string>? activeVehicles = null, bool activityEligible = true, bool publicationKnown = true,
        bool cycleFailed = false, long crossings = 0, MovementMetrics? movement = null)
    {
        var vehicles = (activeVehicles ?? []).ToHashSet(StringComparer.Ordinal);
        return new CategoryCycleSnapshot(
            atUtc,
            [category],
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal) { [category] = vehicles },
            new Dictionary<string, MovementMetrics>(StringComparer.Ordinal) { [category] = movement ?? MovementMetrics.None },
            new Dictionary<string, long>(StringComparer.Ordinal) { [category] = crossings },
            activityEligible,
            publicationKnown,
            cycleFailed);
    }

    public sealed class RecordingCategoryStatisticsSink : ICategoryStatisticsSink
    {
        readonly ConcurrentQueue<FinalizedCategoryStatisticsBatch> _batches = new();
        public TaskCompletionSource<FinalizedCategoryStatisticsBatch> Enqueued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Accept { get; set; }
        public IReadOnlyList<FinalizedCategoryStatisticsBatch> Batches => _batches.ToArray();

        public bool TryEnqueue(FinalizedCategoryStatisticsBatch batch)
        {
            if (!Accept) return false;
            _batches.Enqueue(batch);
            Enqueued.TrySetResult(batch);
            return true;
        }
    }

    public sealed class StatisticsTimeProvider(DateTimeOffset now) : TimeProvider
    {
        DateTimeOffset _now = now.ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => _now;
        public void SetUtcNow(DateTimeOffset value) => _now = value.ToUniversalTime();
    }
}
