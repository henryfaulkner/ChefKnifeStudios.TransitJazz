using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsSnapshotAcceptanceTests
{
    static readonly DateTime Generated = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
    static readonly DateTimeOffset Received = new(Generated, TimeSpan.Zero);

    [Fact]
    public void MismatchedSelectionIsRejectedWithoutReplacingAcceptedEvidence()
    {
        var selection = new StatsSelection("city", "toronto", ["route:a"]);
        var existing = ValidResponse(selection);
        var state = StatsSnapshotPolicy.Accept(StatsSnapshotState.Empty(selection), existing, Received, 100).State;
        var accepted = state.Snapshot;
        var mismatch = ValidResponse(selection with { RouteJoinKeys = ["route:b"] }, Generated.AddMinutes(1));

        var result = StatsSnapshotPolicy.Accept(state, mismatch, Received.AddMinutes(1), 200);

        Assert.Equal(StatsSnapshotAcceptanceKind.Rejected, result.Kind);
        Assert.Equal("invalid_response", result.State.ProblemCode);
        Assert.Same(accepted, result.State.Snapshot);
        Assert.Equal(state.ReceivedTimestamp, result.State.ReceivedTimestamp);
    }

    [Fact]
    public void MalformedSuccessIsRejectedAtomicallyAndPreservesPriorEvidence()
    {
        var selection = new StatsSelection("city", "toronto", []);
        var existing = ValidResponse(selection);
        var state = StatsSnapshotPolicy.Accept(StatsSnapshotState.Empty(selection), existing, Received, 100).State;
        var accepted = state.Snapshot;

        var result = StatsSnapshotPolicy.Accept(state, existing with { Cities = null! }, Received.AddMinutes(1), 200);

        Assert.Equal(StatsSnapshotAcceptanceKind.Rejected, result.Kind);
        Assert.Equal("invalid_response", result.State.ProblemCode);
        Assert.Same(accepted, result.State.Snapshot);
        Assert.Equal(100, result.State.ReceivedTimestamp);
    }

    [Fact]
    public void IdenticalEvidenceUpdatesContactWithoutReanchoringOrReplacingDto()
    {
        var selection = new StatsSelection("city", "toronto", []);
        var original = ValidResponse(selection);
        var state = StatsSnapshotPolicy.Accept(StatsSnapshotState.Empty(selection), original, Received, 100).State;
        var accepted = state.Snapshot;

        var result = StatsSnapshotPolicy.Accept(state, original with { }, Received.AddSeconds(20), 20_000);

        Assert.Equal(StatsSnapshotAcceptanceKind.Identical, result.Kind);
        Assert.Same(accepted, result.State.Snapshot);
        Assert.Equal(Received, result.State.ReceivedUtc);
        Assert.Equal(100, result.State.ReceivedTimestamp);
        Assert.Equal(Received.AddSeconds(20), result.State.LastContactUtc);
        Assert.Null(result.State.ProblemCode);
    }

    [Theory]
    [InlineData("contract")]
    [InlineData("metric")]
    public void UnsupportedVersionsAreRejected(string version)
    {
        var selection = new StatsSelection("city", "toronto", []);
        var response = ValidResponse(selection) with
        {
            ContractVersion = version == "contract" ? "future-contract" : StatsOverlayVocabulary.ContractVersion,
            MetricDefinitionVersion = version == "metric" ? "future-metric" : StatsOverlayVocabulary.MetricDefinitionVersion
        };

        var result = StatsSnapshotPolicy.Accept(StatsSnapshotState.Empty(selection), response, Received, 100);

        Assert.Equal(StatsSnapshotAcceptanceKind.Rejected, result.Kind);
        Assert.Equal("invalid_response", result.State.ProblemCode);
        Assert.Null(result.State.Snapshot);
    }

    [Theory]
    [InlineData("cities")]
    [InlineData("routes")]
    [InlineData("categories")]
    [InlineData("bubbles")]
    [InlineData("warnings")]
    [InlineData("timeline")]
    [InlineData("series")]
    public void NullRequiredCollectionsAreRejected(string collection)
    {
        var selection = new StatsSelection("city", "toronto", []);
        var valid = ValidResponse(selection);
        var malformed = collection switch
        {
            "cities" => valid with { Cities = null! },
            "routes" => valid with { RouteOptions = null! },
            "categories" => valid with { CategoryShares = null! },
            "bubbles" => valid with { Bubbles = null! },
            "warnings" => valid with { Warnings = null! },
            "timeline" => valid with { Timeline = null! },
            _ => valid with { Timeline = valid.Timeline with { Series = null! } }
        };

        var result = StatsSnapshotPolicy.Accept(StatsSnapshotState.Empty(selection), malformed, Received, 100);

        Assert.Equal(StatsSnapshotAcceptanceKind.Rejected, result.Kind);
        Assert.Equal("invalid_response", result.State.ProblemCode);
    }

    [Theory]
    [InlineData("cutoff")]
    [InlineData("day")]
    [InlineData("unspecified_time")]
    public void ImpossibleCutoffOrCityTimeOrderingIsRejected(string invalidity)
    {
        var selection = new StatsSelection("city", "toronto", []);
        var valid = ValidResponse(selection);
        var city = Assert.Single(valid.Cities);
        var malformed = invalidity switch
        {
            "cutoff" => valid with { CutoffUtc = Generated.AddMinutes(1) },
            "day" => valid with { Cities = [city with { LocalDayEndUtc = city.LocalDayStartUtc }] },
            _ => valid with { GeneratedAtUtc = DateTime.SpecifyKind(Generated, DateTimeKind.Unspecified) }
        };

        var result = StatsSnapshotPolicy.Accept(StatsSnapshotState.Empty(selection), malformed, Received, 100);

        Assert.Equal(StatsSnapshotAcceptanceKind.Rejected, result.Kind);
        Assert.Equal("invalid_response", result.State.ProblemCode);
    }

    [Fact]
    public void OlderReplicaGenerationCannotReplaceNewerEvidence()
    {
        var selection = new StatsSelection("city", "toronto", []);
        var newer = ValidResponse(selection);
        var state = StatsSnapshotPolicy.Accept(StatsSnapshotState.Empty(selection), newer, Received, 100).State;
        var accepted = state.Snapshot;
        var older = ValidResponse(selection, Generated.AddMinutes(-1));

        var result = StatsSnapshotPolicy.Accept(state, older, Received.AddSeconds(1), 200);

        Assert.Equal(StatsSnapshotAcceptanceKind.Rejected, result.Kind);
        Assert.Equal("invalid_response", result.State.ProblemCode);
        Assert.Same(accepted, result.State.Snapshot);
        Assert.Equal(100, result.State.ReceivedTimestamp);
    }

    [Fact]
    public void GenerationSkewAllowsExactlyThirtySecondsAndRejectsOneTickMore()
    {
        var selection = new StatsSelection("city", "toronto", []);
        var exactlyThirtySeconds = ValidResponse(selection, Generated.AddSeconds(30));
        var thirtySecondsAndOneTick = ValidResponse(selection, Generated.AddSeconds(30).AddTicks(1));

        var atLimit = StatsSnapshotPolicy.Accept(StatsSnapshotState.Empty(selection), exactlyThirtySeconds, Received, 100);
        var beyondLimit = StatsSnapshotPolicy.Accept(StatsSnapshotState.Empty(selection), thirtySecondsAndOneTick, Received, 100);

        Assert.Equal(StatsSnapshotAcceptanceKind.Accepted, atLimit.Kind);
        Assert.Equal(StatsSnapshotAcceptanceKind.Rejected, beyondLimit.Kind);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(15)]
    [InlineData(1)]
    public void ScheduleAwareEvidenceUsesBoundaryStaleNoticeAndTwoIntervalExpiry(int intervalMinutes)
    {
        var state = AcceptScheduled(new StatsSelection("city", "toronto", []), intervalMinutes, Generated).State;
        var boundary = TimeSpan.FromMinutes(intervalMinutes);
        var staleAt = boundary + TimeSpan.FromSeconds(30);
        var expiryAt = boundary + TimeSpan.FromMinutes(intervalMinutes * 2);

        Assert.False(StatsSnapshotPolicy.Evaluate(state, boundary - TimeSpan.FromTicks(1), Received).IsDue);
        Assert.True(StatsSnapshotPolicy.Evaluate(state, boundary, Received).IsDue);
        Assert.False(StatsSnapshotPolicy.Evaluate(state, staleAt - TimeSpan.FromTicks(1), Received).IsStale);
        Assert.True(StatsSnapshotPolicy.Evaluate(state, staleAt, Received).IsStale);
        Assert.False(StatsSnapshotPolicy.Evaluate(state, expiryAt - TimeSpan.FromTicks(1), Received).IsExpired);
        Assert.True(StatsSnapshotPolicy.Evaluate(state, expiryAt, Received).IsExpired);
        Assert.True(StatsSnapshotPolicy.Evaluate(state, expiryAt + TimeSpan.FromTicks(1), Received).IsExpired);
    }

    [Fact]
    public void ElapsedAgeIsUnaffectedByUtcClockJumpsAndAdvancesWithFixedWallClock()
    {
        var state = AcceptScheduled(new StatsSelection("city", "toronto", []), 1, Generated).State;
        var elapsed = TimeSpan.FromSeconds(30);
        var forwardJump = StatsSnapshotPolicy.Evaluate(state, elapsed, Received.AddDays(1));
        var backwardJump = StatsSnapshotPolicy.Evaluate(state, elapsed, Received.AddDays(-1));
        var fixedWallAdvance = StatsSnapshotPolicy.Evaluate(state, TimeSpan.FromSeconds(90), Received);

        Assert.Equal(forwardJump, backwardJump);
        Assert.False(forwardJump.IsDue);
        Assert.True(fixedWallAdvance.IsStale);
    }

    [Fact]
    public void LegacyEvidenceExpiresAtOriginalGenerationPlusSixtySeconds()
    {
        var selection = new StatsSelection("city", "toronto", []);
        var state = StatsSnapshotPolicy.Accept(StatsSnapshotState.Empty(selection), ValidResponse(selection), Received, 100).State;

        Assert.Equal(StatsSnapshotScheduleMode.Legacy, state.ScheduleMode);
        Assert.False(StatsSnapshotPolicy.Evaluate(state, TimeSpan.FromSeconds(60).Subtract(TimeSpan.FromTicks(1)), Received).IsExpired);
        Assert.True(StatsSnapshotPolicy.Evaluate(state, TimeSpan.FromSeconds(60), Received).IsExpired);
        Assert.True(StatsSnapshotPolicy.Evaluate(state, TimeSpan.FromSeconds(60).Add(TimeSpan.FromTicks(1)), Received).IsExpired);
    }

    [Fact]
    public void RootLocalDayEndBecomesHistoricalAtItsMonotonicTransition()
    {
        var selection = new StatsSelection("city", "toronto", []);
        var generatedAt = new DateTime(2026, 10, 11, 23, 59, 30, DateTimeKind.Utc);
        var state = AcceptScheduled(selection, 1, generatedAt, generatedAt.Date.AddDays(1)).State;
        var untilDayEnd = TimeSpan.FromSeconds(30);

        Assert.False(StatsSnapshotPolicy.Evaluate(state, untilDayEnd - TimeSpan.FromTicks(1), Received).IsHistorical);
        Assert.True(StatsSnapshotPolicy.Evaluate(state, untilDayEnd, Received).IsHistorical);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("invalid_interval")]
    [InlineData("start_after_generation")]
    [InlineData("next_before_generation")]
    [InlineData("next_beyond_window")]
    public void ScheduleAwareEvidenceRequiresCompleteCoherentMetadata(string invalidity)
    {
        var selection = new StatsSelection("city", "toronto", []);
        var valid = ValidResponse(selection);
        var malformed = invalidity switch
        {
            "partial" => valid with { RefreshIntervalMinutes = 60 },
            "invalid_interval" => valid with
            {
                RefreshIntervalMinutes = 7,
                RefreshWindowStartUtc = Generated.AddMinutes(-1),
                NextRefreshAtUtc = Generated.AddMinutes(1)
            },
            "start_after_generation" => valid with
            {
                RefreshIntervalMinutes = 60,
                RefreshWindowStartUtc = Generated.AddTicks(1),
                NextRefreshAtUtc = Generated.AddMinutes(1)
            },
            "next_before_generation" => valid with
            {
                RefreshIntervalMinutes = 60,
                RefreshWindowStartUtc = Generated.AddMinutes(-1),
                NextRefreshAtUtc = Generated
            },
            _ => valid with
            {
                RefreshIntervalMinutes = 60,
                RefreshWindowStartUtc = Generated.AddMinutes(-1),
                NextRefreshAtUtc = Generated.AddMinutes(60)
            }
        };

        var result = StatsSnapshotPolicy.Accept(StatsSnapshotState.Empty(selection), malformed, Received, 100);

        Assert.Equal(StatsSnapshotAcceptanceKind.Rejected, result.Kind);
    }

    [Fact]
    public void LegacyExpiryRemainsAnchoredToGenerationWhenResponseArrivesLate()
    {
        var selection = new StatsSelection("city", "toronto", []);
        var state = StatsSnapshotPolicy.Accept(StatsSnapshotState.Empty(selection), ValidResponse(selection), Received.AddSeconds(10), 100).State;

        Assert.Equal(TimeSpan.FromSeconds(50), state.HardExpiryAfter);
        Assert.False(StatsSnapshotPolicy.Evaluate(state, TimeSpan.FromSeconds(49).Add(TimeSpan.FromTicks(9_999_999)), Received).IsExpired);
        Assert.True(StatsSnapshotPolicy.Evaluate(state, TimeSpan.FromSeconds(50), Received).IsExpired);
    }

    [Fact]
    public void AcceptedSnapshotFreezesTopLevelAndNestedCollections()
    {
        var selection = new StatsSelection("city", "toronto", []);
        var response = ValidResponse(selection);
        var cities = response.Cities.ToList();
        var exclusions = response.Cities[0].TodayCoverage.Exclusions.ToList();
        response = response with { Cities = cities };

        var accepted = StatsSnapshotPolicy.Accept(StatsSnapshotState.Empty(selection), response, Received, 100).State.Snapshot!;
        cities.Clear();

        Assert.Single(accepted.Cities);
        Assert.Throws<NotSupportedException>(() => ((IList<StatsCityWindow>)accepted.Cities).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<StatsExclusion>)accepted.Cities[0].TodayCoverage.Exclusions).Clear());
        Assert.Empty(exclusions);
    }

    internal static StatsOverlayResponse ValidResponse(StatsSelection selection, DateTime? generated = null)
    {
        var generatedAt = generated ?? Generated;
        var dayStart = new DateTime(generatedAt.Year, generatedAt.Month, generatedAt.Day, 0, 0, 0, DateTimeKind.Utc);
        var cutoff = new DateTime(generatedAt.Ticks - TimeSpan.FromMinutes(2).Ticks, DateTimeKind.Utc);
        cutoff = new DateTime(cutoff.Ticks - cutoff.Ticks % TimeSpan.TicksPerHour, DateTimeKind.Utc);
        var requestedEnd = cutoff < dayStart ? dayStart : cutoff > dayStart.AddDays(1) ? dayStart.AddDays(1) : cutoff;
        var coverage = new StatsCoverage { State = "complete", Exclusions = [], PendingThroughUtc = cutoff };
        var citySlug = selection.CitySlug ?? "toronto";
        var city = new StatsCityWindow
        {
            CitySlug = citySlug, DisplayNameKey = $"StatsCity{citySlug}", TimeZone = "Etc/UTC",
            LocalDate = DateOnly.FromDateTime(generatedAt), LocalDayStartUtc = dayStart, LocalDayEndUtc = dayStart.AddDays(1),
            RequestedTodayStartUtc = dayStart, RequestedTodayEndUtc = requestedEnd,
            Last24StartUtc = cutoff.AddHours(-24), Last24EndUtc = cutoff,
            TodayCoverage = coverage, Last24Coverage = coverage
        };
        return new StatsOverlayResponse
        {
            GeneratedAtUtc = generatedAt, CutoffUtc = cutoff, Selection = selection,
            Cities = [city], RouteOptions = [],
            TodayDistance = new StatsDistanceSummary { Window = "today", Coverage = coverage },
            Last24Distance = new StatsDistanceSummary { Window = "last24CompletedHours", Coverage = coverage },
            CategoryShares = [], Bubbles = [], Timeline = new StatsTimeline { SeriesKind = "category", Series = [] }, Warnings = []
        };
    }

    static StatsSnapshotAcceptance AcceptScheduled(
        StatsSelection selection,
        int intervalMinutes,
        DateTime generatedAt,
        DateTime? nextRefresh = null)
    {
        var next = nextRefresh ?? generatedAt.AddMinutes(intervalMinutes);
        var response = ValidResponse(selection, generatedAt) with
        {
            RefreshIntervalMinutes = intervalMinutes,
            RefreshWindowStartUtc = next.AddMinutes(-intervalMinutes),
            NextRefreshAtUtc = next
        };
        var receivedAt = new DateTimeOffset(generatedAt, TimeSpan.Zero);
        return StatsSnapshotPolicy.Accept(StatsSnapshotState.Empty(selection), response, receivedAt, 100);
    }
}
