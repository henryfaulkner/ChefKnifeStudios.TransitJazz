using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Shared.Tests;

public sealed class StatsOverlayContractTests
{
    [Fact]
    public void RequestUsesExactScopeAndPreservesCaseDistinctRouteKeys()
    {
        var request = JsonSerializer.Deserialize<StatsOverlayRequest>(
            """{"scope":"city","citySlug":"atlanta","routeJoinKeys":["10","10A","10a"]}""",
            JsonSettings.DefaultOptions)!;

        Assert.Equal("city", request.Scope);
        Assert.Equal("atlanta", request.CitySlug);
        Assert.Equal(new[] { "10", "10A", "10a" }, request.RouteJoinKeys);
        Assert.Equal("/stats/overlay/query", ApiEndpoints.Stats.Query);
    }

    [Fact]
    public void ContractVersionsUseStableWireDiscriminators()
    {
        Assert.Equal("stats-overlay-v1", StatsOverlayVocabulary.ContractVersion);
        Assert.Equal("observed-city-route-hour-statistics-v1", StatsOverlayVocabulary.MetricDefinitionVersion);
        Assert.Contains("unknown_route", StatsOverlayVocabulary.Reasons);
        Assert.Contains("pending", StatsOverlayVocabulary.CoverageStates);
    }

    [Fact]
    public void NullableMetricAndIdentityFieldsAreWrittenAsJsonNull()
    {
        var bubble = new StatsBubbleStatistic
        {
            EntityKey = "route:5:atlanta:unknown",
            EntityKind = "route",
            CitySlug = "atlanta",
            RouteJoinKey = null,
            Label = "Unknown route",
            CategoryKey = null,
            DistanceKilometers = null,
            RouteVehicleHours = null,
            ActiveClockHours = null,
            ActivitySampleNumerator = null,
            SampleDenominator = null,
            X = null,
            Y = null,
            MeanActiveVehicles = null,
            Coverage = new StatsCoverage
            {
                State = "unavailable",
                ExpectedClosedHours = 0,
                ContributingHours = 0,
                FullyCoveredHours = 0,
                ExpectedRouteHours = null,
                AvailableRouteHours = 0,
                SelectedRouteCoverage = null,
                Exclusions = [],
                BoundaryExcludedHours = 0,
                FirstContributingUtc = null,
                LastContributingUtc = null,
                PendingThroughUtc = DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Utc),
                HistoricalMembershipUnknown = true
            },
            OmissionReason = "unknown_route"
        };

        var json = JsonSerializer.Serialize(bubble, JsonSettings.DefaultOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("x").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("routeJoinKey").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("coverage").GetProperty("expectedRouteHours").ValueKind);
        Assert.Contains("1970-01-01T00:00:00Z", json);
        Assert.DoesNotContain("distanceMetersSum", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("captureRunId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllSelectionAndUndefinedCategoryMetricsRetainTheirNullFields()
    {
        var json = JsonSerializer.Serialize(new
        {
            Selection = new StatsSelection("all", null, []),
            CategoryShares = new[] { new StatsCategoryShare("unknown", null, null, "today") }
        }, JsonSettings.DefaultOptions);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("selection").GetProperty("citySlug").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("categoryShares")[0].GetProperty("kilometers").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("categoryShares")[0].GetProperty("percent").ValueKind);
    }

    [Fact]
    public void RequestRejectsUnsupportedPropertiesInsteadOfIgnoringThem()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StatsOverlayRequest>(
            """{"scope":"city","citySlug":"atlanta","routeJoinKeys":[],"startDate":"2026-01-01"}""",
            JsonSettings.DefaultOptions));
    }

    [Fact]
    public void ReportedVehicleSummaryPreservesNullAndDoesNotExposeIdentityMaterial()
    {
        var summary = new StatsReportedVehicleSummary
        {
            Window = "today",
            Count = null,
            State = "unavailable",
            PopulationScope = "cityFeeds",
            ExpectedCityHours = 4,
            ContributingCityHours = 0,
            FullyCoveredCityHours = 0,
            Cities =
            [
                new StatsReportedVehicleCitySummary
                {
                    CitySlug = "atlanta",
                    WindowReference = "today",
                    Count = null,
                    State = "unavailable",
                    Exclusions = [new StatsReportedVehicleExclusion("missing_hour", 4)],
                }
            ],
            Limitations = ["descriptor_identity"],
        };

        var json = JsonSerializer.Serialize(summary, JsonSettings.DefaultOptions);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("count").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("cities")[0].GetProperty("count").ValueKind);
        Assert.Contains("reported-vehicles-v1", json);
        Assert.DoesNotContain("vehicleId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("identityValue", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OlderOverlayPayloadGetsUnavailableReportedSummaries()
    {
        // Omitted additive fields are populated from their CLR defaults.
        var response = new StatsOverlayResponse
        {
            GeneratedAtUtc = DateTime.UnixEpoch,
            CutoffUtc = DateTime.UnixEpoch,
            Selection = new StatsSelection("city", "atlanta", []),
            Cities = [],
            RouteOptions = [],
            TodayDistance = new StatsDistanceSummary
            {
                Window = "today", Kilometers = null,
                Coverage = EmptyCoverage(),
            },
            Last24Distance = new StatsDistanceSummary
            {
                Window = "last24", Kilometers = null,
                Coverage = EmptyCoverage(),
            },
            CategoryShares = [],
            Bubbles = [],
            Timeline = new StatsTimeline { SeriesKind = "none", Series = [] },
            Warnings = [],
        };

        var json = JsonSerializer.Serialize(response, JsonSettings.DefaultOptions);
        var oldPayload = json.Replace(",\"todayReportedVehicles\":{\"window\":\"today\",\"count\":null,\"state\":\"unavailable\",\"populationScope\":\"cityFeeds\",\"definitionVersion\":\"reported-vehicles-v1\",\"expectedCityHours\":0,\"contributingCityHours\":0,\"fullyCoveredCityHours\":0,\"expectedCityCount\":0,\"contributingCityCount\":0,\"cities\":[],\"limitations\":[]},\"last24ReportedVehicles\":{\"window\":\"last24\",\"count\":null,\"state\":\"unavailable\",\"populationScope\":\"cityFeeds\",\"definitionVersion\":\"reported-vehicles-v1\",\"expectedCityHours\":0,\"contributingCityHours\":0,\"fullyCoveredCityHours\":0,\"expectedCityCount\":0,\"contributingCityCount\":0,\"cities\":[],\"limitations\":[]}", string.Empty, StringComparison.Ordinal);
        var deserialized = JsonSerializer.Deserialize<StatsOverlayResponse>(oldPayload, JsonSettings.DefaultOptions)!;

        Assert.Equal("unavailable", deserialized.TodayReportedVehicles.State);
        Assert.Null(deserialized.TodayReportedVehicles.Count);
        Assert.Equal("unavailable", deserialized.Last24ReportedVehicles.State);
    }

    [Fact]
    public void RefreshSchedulePresenceDistinguishesLegacyAbsenceFromExplicitNull()
    {
        var response = new StatsOverlayResponse
        {
            GeneratedAtUtc = DateTime.UnixEpoch,
            CutoffUtc = DateTime.UnixEpoch,
            Selection = new StatsSelection("all", null, []),
            Cities = [],
            RouteOptions = [],
            TodayDistance = new StatsDistanceSummary { Window = "today", Kilometers = null, Coverage = EmptyCoverage() },
            Last24Distance = new StatsDistanceSummary { Window = "last24CompletedHours", Kilometers = null, Coverage = EmptyCoverage() },
            CategoryShares = [],
            Bubbles = [],
            Timeline = new StatsTimeline { SeriesKind = "unavailable", Series = [] },
            Warnings = []
        };

        var legacyJson = JsonSerializer.Serialize(response, JsonSettings.DefaultOptions);
        var legacy = JsonSerializer.Deserialize<StatsOverlayResponse>(legacyJson, JsonSettings.DefaultOptions)!;
        var explicitNullJson = legacyJson[..^1]
            + ",\"refreshIntervalMinutes\":null,\"refreshWindowStartUtc\":null,\"nextRefreshAtUtc\":null}";
        var explicitNull = JsonSerializer.Deserialize<StatsOverlayResponse>(explicitNullJson, JsonSettings.DefaultOptions)!;

        Assert.False(legacy.RefreshIntervalMinutesPresent);
        Assert.False(legacy.RefreshWindowStartUtcPresent);
        Assert.False(legacy.NextRefreshAtUtcPresent);
        Assert.True(explicitNull.RefreshIntervalMinutesPresent);
        Assert.True(explicitNull.RefreshWindowStartUtcPresent);
        Assert.True(explicitNull.NextRefreshAtUtcPresent);
        Assert.Null(explicitNull.RefreshIntervalMinutes);
        Assert.Null(explicitNull.RefreshWindowStartUtc);
        Assert.Null(explicitNull.NextRefreshAtUtc);
    }

    [Fact]
    public void RefreshSchedulePresenceTracksEveryPartialJsonCombinationAndRejectsFractionalInterval()
    {
        var legacyJson = JsonSerializer.Serialize(new StatsOverlayResponse
        {
            GeneratedAtUtc = DateTime.UnixEpoch,
            CutoffUtc = DateTime.UnixEpoch,
            Selection = new StatsSelection("all", null, []),
            Cities = [],
            RouteOptions = [],
            TodayDistance = new StatsDistanceSummary { Window = "today", Kilometers = null, Coverage = EmptyCoverage() },
            Last24Distance = new StatsDistanceSummary { Window = "last24CompletedHours", Kilometers = null, Coverage = EmptyCoverage() },
            CategoryShares = [],
            Bubbles = [],
            Timeline = new StatsTimeline { SeriesKind = "unavailable", Series = [] },
            Warnings = []
        }, JsonSettings.DefaultOptions);
        var propertyValues = new[]
        {
            "\"refreshIntervalMinutes\":60",
            "\"refreshWindowStartUtc\":\"1970-01-01T00:00:00Z\"",
            "\"nextRefreshAtUtc\":\"1970-01-01T01:00:00Z\""
        };

        for (var mask = 1; mask < 8; mask++)
        {
            var selected = propertyValues.Where((_, index) => (mask & (1 << index)) != 0);
            var json = legacyJson[..^1] + "," + string.Join(',', selected) + "}";
            var parsed = JsonSerializer.Deserialize<StatsOverlayResponse>(json, JsonSettings.DefaultOptions)!;

            Assert.Equal((mask & 1) != 0, parsed.RefreshIntervalMinutesPresent);
            Assert.Equal((mask & 2) != 0, parsed.RefreshWindowStartUtcPresent);
            Assert.Equal((mask & 4) != 0, parsed.NextRefreshAtUtcPresent);
        }

        var fractionalJson = legacyJson[..^1] + ",\"refreshIntervalMinutes\":7.5}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StatsOverlayResponse>(fractionalJson, JsonSettings.DefaultOptions));
    }

    [Fact]
    public void RefreshScheduleMetadataSerializesAdditivelyAndRoundTripsItsPresence()
    {
        var response = new StatsOverlayResponse
        {
            GeneratedAtUtc = new DateTime(2026, 1, 2, 12, 2, 0, DateTimeKind.Utc),
            CutoffUtc = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc),
            RefreshIntervalMinutes = 15,
            RefreshWindowStartUtc = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc),
            NextRefreshAtUtc = new DateTime(2026, 1, 2, 12, 15, 0, DateTimeKind.Utc),
            Selection = new StatsSelection("all", null, []),
            Cities = [],
            RouteOptions = [],
            TodayDistance = new StatsDistanceSummary { Window = "today", Kilometers = null, Coverage = EmptyCoverage() },
            Last24Distance = new StatsDistanceSummary { Window = "last24CompletedHours", Kilometers = null, Coverage = EmptyCoverage() },
            CategoryShares = [],
            Bubbles = [],
            Timeline = new StatsTimeline { SeriesKind = "unavailable", Series = [] },
            Warnings = []
        };

        var json = JsonSerializer.Serialize(response, JsonSettings.DefaultOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(15, root.GetProperty("refreshIntervalMinutes").GetInt32());
        Assert.Equal("2026-01-02T12:00:00Z", root.GetProperty("refreshWindowStartUtc").GetString());
        Assert.Equal("2026-01-02T12:15:00Z", root.GetProperty("nextRefreshAtUtc").GetString());

        var parsed = JsonSerializer.Deserialize<StatsOverlayResponse>(json, JsonSettings.DefaultOptions)!;
        Assert.True(parsed.RefreshIntervalMinutesPresent);
        Assert.True(parsed.RefreshWindowStartUtcPresent);
        Assert.True(parsed.NextRefreshAtUtcPresent);
        Assert.Equal(response.RefreshIntervalMinutes, parsed.RefreshIntervalMinutes);
        Assert.Equal(response.RefreshWindowStartUtc, parsed.RefreshWindowStartUtc);
        Assert.Equal(response.NextRefreshAtUtc, parsed.NextRefreshAtUtc);
    }

    static StatsCoverage EmptyCoverage() => new()
    {
        State = "unavailable", Exclusions = [], PendingThroughUtc = DateTime.UnixEpoch,
    };
}
