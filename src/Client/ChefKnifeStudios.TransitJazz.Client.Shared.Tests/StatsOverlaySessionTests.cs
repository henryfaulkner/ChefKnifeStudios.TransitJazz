using ChefKnifeStudios.TransitJazz.Client.Shared.Components.Stats;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsOverlaySessionTests
{
    [Fact]
    public void RefreshPolicyUsesOneMinuteBoundaryAndNeverRefreshesWhileHidden()
    {
        var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        var lastSuccess = now;

        Assert.False(StatsOverlayRefreshPolicy.ShouldRefresh(now.AddSeconds(59), false, false, lastSuccess));
        Assert.True(StatsOverlayRefreshPolicy.ShouldRefresh(now.AddMinutes(1), false, false, lastSuccess));
        Assert.True(StatsOverlayRefreshPolicy.ShouldRefresh(now, false, true, lastSuccess));
        Assert.False(StatsOverlayRefreshPolicy.ShouldRefresh(now.AddDays(1), true, true, lastSuccess));
        Assert.True(StatsOverlayRefreshPolicy.ShouldRefresh(now, false, false, null));
    }

    [Fact]
    public void RefreshPolicyCannotPollWhenClosedHiddenOrDisposedAndAllowsOnlyOneLoop()
    {
        Assert.True(StatsOverlayRefreshPolicy.ShouldRunLoop(sessionOpen: true, hidden: false, disposed: false));
        Assert.True(StatsOverlayRefreshPolicy.ShouldStartLoop(sessionOpen: true, hidden: false, disposed: false, loopAlreadyRunning: false));
        Assert.False(StatsOverlayRefreshPolicy.ShouldStartLoop(sessionOpen: true, hidden: false, disposed: false, loopAlreadyRunning: true));
        Assert.False(StatsOverlayRefreshPolicy.ShouldRunLoop(sessionOpen: false, hidden: false, disposed: false));
        Assert.False(StatsOverlayRefreshPolicy.ShouldRunLoop(sessionOpen: true, hidden: true, disposed: false));
        Assert.False(StatsOverlayRefreshPolicy.ShouldRunLoop(sessionOpen: true, hidden: false, disposed: true));

    }

    [Fact]
    public async Task RefreshLoopLeaseCancelsPendingTickAndCannotClearANewerVisibleLoop()
    {
        using var session = new CancellationTokenSource();
        var loops = new StatsOverlayRefreshLoopState();
        Assert.True(loops.TryStart(session.Token, out var first));
        Assert.NotNull(first);
        Assert.False(loops.TryStart(session.Token, out var duplicate));
        Assert.Null(duplicate);

        var firstCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var firstRegistration = first!.Token.Register(() => firstCancelled.TrySetResult());
        loops.Stop(); // the visibility-hidden and dispose paths both stop this same owned loop
        await firstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(loops.IsRunning);

        Assert.True(loops.TryStart(session.Token, out var second)); // visible restoration starts one fresh timer
        Assert.NotNull(second);
        loops.Complete(first); // late completion of the old timer must not release the new timer's ownership
        Assert.True(loops.IsRunning);

        var secondCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var secondRegistration = second!.Token.Register(() => secondCancelled.TrySetResult());
        loops.Stop();
        await secondCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        loops.Complete(second);
        Assert.False(loops.IsRunning);
    }

    [Fact]
    public void RefreshIsDueAtTheCompletedHourAndLocalMidnightWhenTheMinuteBoundaryPasses()
    {
        var lastSuccess = new DateTimeOffset(2026, 4, 2, 3, 59, 0, TimeSpan.Zero);

        Assert.False(StatsOverlayRefreshPolicy.ShouldRefresh(lastSuccess.AddSeconds(59), false, false, lastSuccess));
        Assert.True(StatsOverlayRefreshPolicy.ShouldRefresh(lastSuccess.AddMinutes(1), false, false, lastSuccess));
        Assert.True(StatsOverlayRefreshPolicy.ShouldRefresh(lastSuccess.AddHours(1), false, false, lastSuccess));
    }

    [Fact]
    public void RequestGateKeepsCanceledReadActiveAndStartsOnlyNewestDesiredGeneration()
    {
        var gate = new StatsOverlayRequestGate();

        Assert.True(gate.TryBegin(10));
        Assert.False(gate.TryBegin(11));
        gate.QueueLatest(11, retainSnapshot: false);
        gate.QueueLatest(12, retainSnapshot: true);

        Assert.True(gate.HasActiveRequest);
        Assert.Equal(10, gate.ActiveGeneration);
        Assert.Equal(new PendingStatsOverlayRequest(12, RetainSnapshot: true), gate.Complete(10, 12));
        Assert.False(gate.HasActiveRequest);
        Assert.True(gate.TryBegin(12));
    }

    [Fact]
    public void RequestGateDropsObsoleteQueuedScopeAndDoesNotReleaseForWrongCompletion()
    {
        var gate = new StatsOverlayRequestGate();

        Assert.True(gate.TryBegin(2));
        gate.QueueLatest(3, retainSnapshot: false);
        Assert.Null(gate.Complete(1, 3));
        Assert.Equal(2, gate.ActiveGeneration);
        Assert.Null(gate.Complete(2, 4));
        Assert.False(gate.HasActiveRequest);
        Assert.True(gate.TryBegin(4));
    }

    [Fact]
    public void CanceledRequestSerializesOnlyTheLatestScopeAfterCloseAndReopen()
    {
        var gate = new StatsOverlayRequestGate();
        Assert.True(gate.TryBegin(10)); // old session remains owned until its canceled HTTP call settles
        gate.QueueLatest(11, retainSnapshot: false); // intermediate All scope
        gate.QueueLatest(12, retainSnapshot: false); // newest selected city scope
        gate.InvalidatePending(); // explicit close invalidates both queued desires
        gate.QueueLatest(20, retainSnapshot: false); // reopened session has a new generation

        Assert.Equal(new PendingStatsOverlayRequest(20, RetainSnapshot: false), gate.Complete(10, 20));
        Assert.False(gate.HasActiveRequest);
        Assert.True(gate.TryBegin(20));
        Assert.Null(gate.Complete(10, 20)); // late duplicate completion cannot release the new request
        Assert.Equal(20, gate.ActiveGeneration);
    }

    [Fact]
    public void ResponseMustMatchScopeCityAndOrdinalRouteSelection()
    {
        var response = Response(new StatsSelection("city", "toronto", ["route:A", "route:z"]));

        Assert.True(StatsOverlayResponseSelection.Matches(response, "city", "toronto", ["route:z", "route:A"]));
        Assert.False(StatsOverlayResponseSelection.Matches(response, "all", null, ["route:A", "route:z"]));
        Assert.False(StatsOverlayResponseSelection.Matches(response, "city", "Toronto", ["route:A", "route:z"]));
        Assert.False(StatsOverlayResponseSelection.Matches(response, "city", "toronto", ["route:a", "route:z"]));
    }

    [Fact]
    public void SnapshotAndProjectionAreAcceptedAsOneGenerationBoundValueAcrossCutoffAndLocalDateChanges()
    {
        var state = new StatsOverlayAcceptedSnapshotState();
        var beforeMidnight = Response(
            new StatsSelection("city", "toronto", []),
            new DateTime(2026, 4, 2, 3, 59, 30, DateTimeKind.Utc),
            new DateTime(2026, 4, 2, 3, 0, 0, DateTimeKind.Utc),
            new DateOnly(2026, 4, 1));
        var beforeView = Presentation(beforeMidnight);
        Assert.True(state.TryAccept(7, 7, beforeMidnight, beforeView, "city", "toronto", []));

        var afterMidnightBeforeCompletedHour = Response(
            new StatsSelection("city", "toronto", []),
            new DateTime(2026, 4, 2, 4, 0, 30, DateTimeKind.Utc),
            new DateTime(2026, 4, 2, 3, 0, 0, DateTimeKind.Utc),
            new DateOnly(2026, 4, 2));
        var afterMidnightView = Presentation(afterMidnightBeforeCompletedHour);
        Assert.True(state.TryAccept(8, 8, afterMidnightBeforeCompletedHour, afterMidnightView, "city", "toronto", []));
        var acceptedAfterMidnight = state.Current!;

        Assert.Equal(new DateTime(2026, 4, 2, 4, 0, 30, DateTimeKind.Utc), acceptedAfterMidnight.Snapshot.GeneratedAtUtc);
        Assert.Equal(new DateTime(2026, 4, 2, 3, 0, 0, DateTimeKind.Utc), acceptedAfterMidnight.Snapshot.CutoffUtc);
        Assert.Equal(new DateOnly(2026, 4, 2), Assert.Single(acceptedAfterMidnight.Snapshot.Cities).LocalDate);
        Assert.Same(afterMidnightBeforeCompletedHour, acceptedAfterMidnight.Presentation.Snapshot);

        var nextCompletedHour = Response(
            new StatsSelection("city", "toronto", []),
            new DateTime(2026, 4, 2, 4, 2, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 2, 4, 0, 0, DateTimeKind.Utc),
            new DateOnly(2026, 4, 2));
        Assert.True(state.TryAccept(9, 9, nextCompletedHour, Presentation(nextCompletedHour), "city", "toronto", []));
        var acceptedNextHour = state.Current!;
        Assert.Equal(new DateTime(2026, 4, 2, 4, 0, 0, DateTimeKind.Utc), acceptedNextHour.Snapshot.CutoffUtc);

        Assert.False(state.TryAccept(7, 9, beforeMidnight, beforeView, "city", "toronto", []));
        Assert.Same(acceptedNextHour, state.Current);
        Assert.Equal(new DateOnly(2026, 4, 2), Assert.Single(state.Current!.Snapshot.Cities).LocalDate);
    }

    [Fact]
    public void PresentationReplacementKeepsAcceptedEvidenceAndActiveRead()
    {
        var state = new StatsOverlayAcceptedSnapshotState();
        var response = Response(
            new StatsSelection("city", "toronto", []),
            new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 11, 11, 0, 0, DateTimeKind.Utc),
            new DateOnly(2026, 10, 11));
        Assert.True(state.TryAccept(3, 3, response, Presentation(response), "city", "toronto", []));
        var before = state.Current!;
        var requestGate = new StatsOverlayRequestGate();
        Assert.True(requestGate.TryBegin(4));

        var replacement = Presentation(response);
        Assert.True(state.TryReplacePresentation(replacement));

        Assert.Same(response, state.Current!.Snapshot);
        Assert.Same(replacement, state.Current.Presentation);
        Assert.Equal(before.Snapshot.CutoffUtc, state.Current.Snapshot.CutoffUtc);
        Assert.Equal(before.Snapshot.Cities, state.Current.Snapshot.Cities);
        Assert.Equal(4, requestGate.ActiveGeneration);
        Assert.False(requestGate.TryBegin(5));
    }

    [Fact]
    public void SameScopeRefreshFailureKeepsOriginalSnapshotTimestampAndWindowMetadata()
    {
        var state = new StatsOverlayAcceptedSnapshotState();
        var response = Response(
            new StatsSelection("city", "toronto", []),
            new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 11, 11, 0, 0, DateTimeKind.Utc),
            new DateOnly(2026, 10, 11));
        Assert.True(state.TryAccept(20, 20, response, Presentation(response), "city", "toronto", []));
        var accepted = state.Current!;

        // A failed refresh has no replacement response to commit; it only marks the accepted one stale.
        state.MarkStale();

        Assert.True(state.Current!.IsStale);
        Assert.Same(accepted.Snapshot, state.Current.Snapshot);
        Assert.Same(accepted.Presentation, state.Current.Presentation);
        Assert.Equal(new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc), state.Current.Snapshot.GeneratedAtUtc);
        Assert.Equal(new DateTime(2026, 10, 11, 11, 0, 0, DateTimeKind.Utc), state.Current.Snapshot.CutoffUtc);
        Assert.Equal(new DateOnly(2026, 10, 11), Assert.Single(state.Current.Snapshot.Cities).LocalDate);

        state.MarkFresh(); // beginning the next retry clears stale status without changing the snapshot
        Assert.False(state.Current!.IsStale);
        Assert.Same(accepted.Snapshot, state.Current.Snapshot);
    }

    static StatsOverlayPresentation Presentation(StatsOverlayResponse response) =>
        StatsOverlayProjection.Build(response, key => $"localized:{key}");

    static StatsOverlayResponse Response(
        StatsSelection selection,
        DateTime? generatedAtUtc = null,
        DateTime? cutoffUtc = null,
        DateOnly? cityLocalDate = null) => new()
    {
        GeneratedAtUtc = generatedAtUtc ?? new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc),
        CutoffUtc = cutoffUtc ?? new DateTime(2026, 10, 11, 11, 0, 0, DateTimeKind.Utc),
        Selection = selection,
        Cities = selection.CitySlug is { } citySlug ? [City(citySlug, cityLocalDate ?? new DateOnly(2026, 10, 11))] : [],
        RouteOptions = [],
        TodayDistance = new StatsDistanceSummary
        {
            Window = "today", Kilometers = 0, Coverage = Coverage()
        },
        Last24Distance = new StatsDistanceSummary
        {
            Window = "last24CompletedHours", Kilometers = 0, Coverage = Coverage()
        },
        CategoryShares = [],
        Bubbles = [],
        Timeline = new StatsTimeline { SeriesKind = "category", Stacked = true, ElapsedHourExtent = 0, Series = [] },
        Warnings = []
    };

    static StatsCityWindow City(string citySlug, DateOnly localDate)
    {
        var localStartUtc = DateTime.SpecifyKind(new DateTime(localDate.Year, localDate.Month, localDate.Day), DateTimeKind.Utc);
        var localEndUtc = localStartUtc.AddDays(1);
        return new StatsCityWindow
        {
            CitySlug = citySlug,
            DisplayNameKey = "StatsCityToronto",
            TimeZone = "America/Toronto",
            LocalDate = localDate,
            LocalDayStartUtc = localStartUtc,
            LocalDayEndUtc = localEndUtc,
            RequestedTodayStartUtc = localStartUtc,
            RequestedTodayEndUtc = localEndUtc,
            Last24StartUtc = localStartUtc,
            Last24EndUtc = localEndUtc,
            TodayCoverage = Coverage(),
            Last24Coverage = Coverage()
        };
    }

    static StatsCoverage Coverage() => new()
    {
        State = "complete", ExpectedClosedHours = 0, ContributingHours = 0,
        FullyCoveredHours = 0, AvailableRouteHours = 0, Exclusions = [],
        PendingThroughUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
    };
}
