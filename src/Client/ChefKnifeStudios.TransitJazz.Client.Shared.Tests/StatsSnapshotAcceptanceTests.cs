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

    [Fact]
    public void ValidV2EvidenceIsAcceptedAndEveryNestedCollectionIsFrozen()
    {
        var selection = new StatsSelection("city", "toronto", []);
        var periods = new List<StatsUtcHourPeriod> { new() { StartUtc = Generated.AddHours(-2), EndUtc = Generated.AddHours(-1) } };
        var reasons = new List<string>();
        var versions = new List<string> { StatsOverlayVocabulary.MetricDefinitionVersionV2 };
        var v2 = ValidV2Response(selection, periods, reasons, versions);

        var accepted = StatsSnapshotPolicy.Accept(StatsSnapshotState.Empty(selection), v2, Received, 100);

        Assert.Equal(StatsSnapshotAcceptanceKind.Accepted, accepted.Kind);
        var snapshot = accepted.State.Snapshot!;
        periods.Clear();
        reasons.Add("source_failure");
        versions.Clear();
        Assert.Single(snapshot.TodayDistance.Coverage.SourceCoverage![0].CoveredPeriods);
        Assert.Empty(snapshot.TodayDistance.Coverage.SourceCoverage[0].Reasons);
        Assert.Equal(StatsOverlayVocabulary.MetricDefinitionVersionV2,
            Assert.Single(snapshot.TodayDistance.Coverage.MeasureAvailability![0].DefinitionVersions));
        Assert.Throws<NotSupportedException>(() => ((IList<StatsUtcHourPeriod>)snapshot.TodayDistance.Coverage.SourceCoverage[0].CoveredPeriods).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)snapshot.TodayDistance.Coverage.SourceCoverage[0].Reasons).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)snapshot.TodayDistance.Coverage.MeasureAvailability![0].DefinitionVersions).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)snapshot.Bubbles[0].MeasureAvailability!.YReasons).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)snapshot.Timeline.Series[0].Buckets[0].SourceCoverage![0].Reasons).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<StatsBucketSourceCoverage>)snapshot.Timeline.Series[0].Buckets[0].SourceCoverage!).Clear());
    }

    [Theory]
    [InlineData("period")]
    [InlineData("reason")]
    [InlineData("count")]
    [InlineData("definition")]
    [InlineData("measure")]
    [InlineData("contract")]
    [InlineData("selection")]
    public void MalformedV2NestedEvidenceOrIdentityIsRejected(string invalidity)
    {
        var selection = new StatsSelection("city", "toronto", []);
        var valid = ValidV2Response(selection);
        var source = Assert.Single(valid.TodayDistance.Coverage.SourceCoverage!);
        var malformed = invalidity switch
        {
            "period" => valid with { TodayDistance = valid.TodayDistance with
            {
                Coverage = valid.TodayDistance.Coverage with
                {
                    SourceCoverage = [source with { CoveredPeriods = [new StatsUtcHourPeriod
                    {
                        StartUtc = Generated.AddMinutes(-90), EndUtc = Generated.AddMinutes(-30)
                    }] }]
                }
            } },
            "reason" => valid with { TodayDistance = valid.TodayDistance with
            {
                Coverage = valid.TodayDistance.Coverage with
                {
                    SourceCoverage = [source with { Reasons = ["raw database error"] }]
                }
            } },
            "count" => valid with { TodayDistance = valid.TodayDistance with
            {
                Coverage = valid.TodayDistance.Coverage with { ObservedRouteHours = -1 }
            } },
            "definition" => valid with { TodayDistance = valid.TodayDistance with
            {
                Coverage = valid.TodayDistance.Coverage with
                {
                    DefinitionVersions = [new string('x', 129)]
                }
            } },
            "measure" => valid with { Bubbles = [valid.Bubbles[0] with { MeasureAvailability = null }] },
            "contract" => valid with { MetricDefinitionVersion = StatsOverlayVocabulary.MetricDefinitionVersion },
            _ => valid with { Selection = selection with { RouteJoinKeys = ["other-route"] } }
        };

        var result = StatsSnapshotPolicy.Accept(StatsSnapshotState.Empty(selection), malformed, Received, 100);

        Assert.Equal(StatsSnapshotAcceptanceKind.Rejected, result.Kind);
        Assert.Null(result.State.Snapshot);
    }

    [Fact]
    public void V2NestedEvidenceChangesSnapshotIdentityEvenWhenGenerationMetadataMatches()
    {
        var selection = new StatsSelection("city", "toronto", []);
        var first = ValidV2Response(selection);
        var state = StatsSnapshotPolicy.Accept(StatsSnapshotState.Empty(selection), first, Received, 100).State;
        var source = Assert.Single(first.TodayDistance.Coverage.SourceCoverage!);
        var changed = first with { TodayDistance = first.TodayDistance with
        {
            Coverage = first.TodayDistance.Coverage with
            {
                SourceCoverage = [source with { Reasons = ["source_disabled"] }]
            }
        } };

        var result = StatsSnapshotPolicy.Accept(state, changed, Received.AddSeconds(1), 200);

        Assert.Equal(StatsSnapshotAcceptanceKind.Accepted, result.Kind);
        Assert.NotSame(state.Snapshot, result.State.Snapshot);
        Assert.Equal("source_disabled", Assert.Single(result.State.Snapshot!.TodayDistance.Coverage.SourceCoverage![0].Reasons));
    }

    internal static StatsOverlayResponse ValidV2Response(
        StatsSelection selection,
        IReadOnlyList<StatsUtcHourPeriod>? periods = null,
        IReadOnlyList<string>? reasons = null,
        IReadOnlyList<string>? versions = null)
    {
        var legacy = ValidResponse(selection);
        var start = legacy.CutoffUtc.AddHours(-1);
        var coveredPeriods = periods ?? [new StatsUtcHourPeriod { StartUtc = start, EndUtc = legacy.CutoffUtc }];
        var safeReasons = reasons ?? [];
        var definitions = versions ?? [StatsOverlayVocabulary.MetricDefinitionVersionV2];
        var source = new StatsSourceCoverage
        {
            CitySlug = legacy.Cities[0].CitySlug, SourceKey = "test-source", WindowReference = "today",
            State = "complete", ContributingHours = 1, FullyCoveredHours = 1,
            CoveredPeriods = coveredPeriods, UnavailablePeriods = [], Reasons = safeReasons
        };
        var availability = new StatsMeasureAvailability
        {
            MeasureKey = "distance", State = "complete", Reasons = safeReasons,
            DefinitionVersions = definitions
        };
        StatsCoverage V2Coverage(StatsCoverage coverage, string windowReference = "today") => coverage with
        {
            ExpectedRouteHours = windowReference == "today" ? 1 : null,
            AvailableRouteHours = 1,
            ObservedRouteHours = 1,
            UnavailableRouteHours = windowReference == "today" ? 0 : null,
            HistoricalMembershipUnknown = windowReference != "today",
            AssessmentMeasure = "distance",
            SourceCoverage = [source with { WindowReference = windowReference }],
            MeasureAvailability = [availability], DefinitionVersions = definitions
        };
        var v2City = legacy.Cities[0] with
        {
            TodayCoverage = V2Coverage(legacy.Cities[0].TodayCoverage),
            Last24Coverage = V2Coverage(legacy.Cities[0].Last24Coverage, "last24CompletedHours")
        };
        var categoryCoverage = V2Coverage(legacy.TodayDistance.Coverage);
        var bucketSourceReasons = new List<string>(safeReasons);
        return legacy with
        {
            ContractVersion = StatsOverlayVocabulary.ContractVersionV2,
            MetricDefinitionVersion = StatsOverlayVocabulary.MetricDefinitionVersionV2,
            Cities = [v2City],
            TodayDistance = legacy.TodayDistance with { Coverage = V2Coverage(legacy.TodayDistance.Coverage) },
            Last24Distance = legacy.Last24Distance with { Coverage = V2Coverage(legacy.Last24Distance.Coverage, "last24CompletedHours") },
            CategoryShares = [new StatsCategoryShare("bus", 1m, 100m, "today")
            {
                DenominatorKind = "availableDistance", Coverage = categoryCoverage
            }],
            Bubbles = [new StatsBubbleStatistic
            {
                EntityKey = "route:test", EntityKind = "route", CitySlug = legacy.Cities[0].CitySlug,
                RouteJoinKey = "route:test", Label = "Test", CategoryKey = "bus", DistanceKilometers = 1m,
                X = 1m, Y = 1m, Coverage = V2Coverage(legacy.TodayDistance.Coverage),
                MeasureAvailability = new StatsBubbleMeasureAvailability
                {
                    XReasons = safeReasons, YReasons = safeReasons, SizeReasons = safeReasons, PopulationReasons = safeReasons
                }
            }],
            Timeline = new StatsTimeline
            {
                SeriesKind = "single", Series = [new StatsTimelineSeries
                {
                    Key = "test", WindowReference = "today", Buckets = [new StatsTimelineBucket
                    {
                        StartUtc = start, EndUtc = legacy.CutoffUtc, LocalDate = DateOnly.FromDateTime(start),
                        LocalEndLabel = "11:00", UtcOffset = "+00:00", ElapsedHours = 1,
                        CoverageState = "complete", SourceCoverage = [new StatsBucketSourceCoverage
                        {
                            CitySlug = legacy.Cities[0].CitySlug, SourceKey = "test-source", State = "complete",
                            Reasons = bucketSourceReasons
                        }]
                    }]
                }]
            }
        };
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
