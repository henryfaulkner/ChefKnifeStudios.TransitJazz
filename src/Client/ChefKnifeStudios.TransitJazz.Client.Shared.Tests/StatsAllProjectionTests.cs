using System.Globalization;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components.Stats;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsAllProjectionTests
{
    static readonly DateTime Cutoff = new(2026, 11, 1, 5, 57, 0, DateTimeKind.Utc);

    [Fact]
    public void AllProjectionKeepsConfiguredCityRowsOwnDatesAndUnavailableOrFutureReasons()
    {
        var cities = new[]
        {
            City("atlanta", "StatsCityAtlanta", new DateOnly(2026, 11, 1), "America/New_York", "complete", 24),
            City("washington-dc", "StatsCityWashingtonDc", new DateOnly(2026, 11, 1), "America/New_York", "complete", 24),
            City("boston", "StatsCityBoston", new DateOnly(2026, 11, 1), "America/New_York", "unavailable", 0),
            City("new-york-city", "StatsCityNewYorkCity", new DateOnly(2026, 11, 1), "America/New_York", "complete", 24),
            City("toronto", "StatsCityToronto", new DateOnly(2026, 11, 1), "America/Toronto", "partial", 24),
            City("philadelphia", "StatsCityPhiladelphia", new DateOnly(2026, 11, 1), "America/New_York", "complete", 24),
            City("denver", "StatsCityDenver", new DateOnly(2026, 10, 31), "America/Denver", "partial", 23)
        };
        var citySeries = cities.Select(city => MakeSeries(city)).ToArray();
        var response = new StatsOverlayResponse
        {
            GeneratedAtUtc = Cutoff.AddMinutes(1),
            CutoffUtc = Cutoff,
            Selection = new StatsSelection("all", null, []),
            Cities = cities,
            RouteOptions = [],
            TodayDistance = new StatsDistanceSummary
            {
                Window = "today", Kilometers = 123m,
                Coverage = Coverage("partial", 174, 28),
                ContributingCityHours = 28, ExpectedCityHours = 174,
                ContributingCityCount = 6, ExpectedCityCount = 7
            },
            Last24Distance = new StatsDistanceSummary
            {
                Window = "last24CompletedHours", Kilometers = 240m,
                Coverage = Coverage("partial", 168, 144),
                ContributingCityHours = 144, ExpectedCityHours = 168,
                ContributingCityCount = 6, ExpectedCityCount = 7
            },
            CategoryShares =
            [
                new StatsCategoryShare("bus", 70m, 56.91m, "today"),
                new StatsCategoryShare("rail", 53m, 43.09m, "today")
            ],
            Bubbles = cities.Select(city => Bubble(city)).ToArray(),
            Timeline = new StatsTimeline
            {
                SeriesKind = "city", Stacked = false, ElapsedHourExtent = 25, Series = citySeries
            },
            Warnings = ["city_days_differ", "partial_coverage"]
        };

        var view = StatsOverlayProjection.Build(response, key => $"localized:{key}");

        Assert.Equal(cities.Length, view.Snapshot.Cities.Count);
        Assert.Empty(view.Snapshot.RouteOptions);
        Assert.Equal("all", view.Snapshot.Selection.Scope);
        Assert.Equal(123m, view.Snapshot.TodayDistance.Kilometers);
        Assert.Equal(123d, view.CategoryPie.Rows.Sum(row => row.Kilometers!.Value));
        Assert.Equal(2, view.CategoryPie.Rows.Count);
        Assert.True(view.Timeline.EnableSeriesVisibility);
        Assert.Equal(25, view.Timeline.NumericXValues!.Count);
        Assert.Equal(7, view.Timeline.Series.Count);

        var denver = view.Timeline.Series.Single(series => series.Key == "city:denver");
        var toronto = view.Timeline.Series.Single(series => series.Key == "city:toronto");
        var boston = view.Timeline.Series.Single(series => series.Key == "city:boston");
        Assert.Equal("localized:StatsCityDenver", denver.Label);
        Assert.Contains(new DateOnly(2026, 10, 31).ToString("d", CultureInfo.GetCultureInfo("en")), denver.PointEvidence![0]!.LocalLabel);
        Assert.Contains("01:00 -04:00", toronto.PointEvidence![0]!.LocalLabel);
        Assert.Contains("01:00 -05:00", toronto.PointEvidence[1]!.LocalLabel);
        Assert.Equal("-05:00", toronto.PointEvidence[1]!.UtcOffset);
        Assert.Null(toronto.Values[24]);
        Assert.Null(toronto.PointEvidence[24]!.HourlyKnownKilometers);
        Assert.Equal("future", toronto.PointEvidence[24]!.Reason);
        Assert.Equal("localized:StatsReasonFuture", toronto.PointEvidence[24]!.TooltipLines.Last());
        Assert.All(boston.Values, value => Assert.Null(value));
        Assert.All(boston.PointEvidence!, point => Assert.Equal("history_disabled", point!.Reason));

        Assert.Equal("localized:StatsCityAtlanta", view.Bubbles.Rows[0].Label);
        Assert.Equal("localized:StatsCityAtlanta", view.Bubbles.Rows[0].TooltipLines![0]);
        Assert.Equal(cities.Length, view.Bubbles.GetTableRows().Count);
    }

    static StatsTimelineSeries MakeSeries(StatsCityWindow city)
    {
        var count = city.CitySlug == "denver" ? 24 : 25;
        var buckets = Enumerable.Range(1, count).Select(hour =>
        {
            var unavailable = city.CitySlug == "boston";
            var future = city.CitySlug == "denver" ? hour > 23 : hour > 1;
            var offset = city.CitySlug == "denver" ? "-06:00" : hour == 1 ? "-04:00" : "-05:00";
            var endUtc = city.LocalDayStartUtc.AddHours(hour);
            var localEndHour = city.CitySlug == "denver"
                ? hour == 24 ? 0 : hour
                : hour <= 2 ? 1 : hour == 25 ? 0 : hour - 1;
            return new StatsTimelineBucket
            {
                StartUtc = endUtc.AddHours(-1), EndUtc = endUtc,
                LocalDate = city.LocalDate, LocalEndLabel = $"{localEndHour:00}:00", UtcOffset = offset,
                ElapsedHours = hour,
                HourlyKnownKilometers = unavailable || future ? null : 10m,
                CumulativeObservedKilometers = unavailable || future ? null : hour * 10m,
                PlotCumulativeKilometers = unavailable || future ? null : hour * 10m,
                CoverageState = unavailable ? "unavailable" : future ? "pending" : city.TodayCoverage.State,
                Reason = unavailable ? "history_disabled" : future ? "future" : null
            };
        }).ToArray();
        return new StatsTimelineSeries
        {
            Key = $"city:{city.CitySlug}", CitySlug = city.CitySlug,
            WindowReference = "today", Buckets = buckets
        };
    }

    static StatsCityWindow City(string slug, string displayKey, DateOnly date, string zone, string state, int contributing) => new()
    {
        CitySlug = slug, DisplayNameKey = displayKey, TimeZone = zone, LocalDate = date,
        LocalDayStartUtc = slug == "denver"
            ? new DateTime(2026, 10, 31, 6, 0, 0, DateTimeKind.Utc)
            : new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc),
        LocalDayEndUtc = slug == "denver"
            ? new DateTime(2026, 11, 1, 6, 0, 0, DateTimeKind.Utc)
            : new DateTime(2026, 11, 2, 5, 0, 0, DateTimeKind.Utc),
        RequestedTodayStartUtc = slug == "denver"
            ? new DateTime(2026, 10, 31, 6, 0, 0, DateTimeKind.Utc)
            : new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc),
        RequestedTodayEndUtc = Cutoff,
        Last24StartUtc = Cutoff.AddHours(-24), Last24EndUtc = Cutoff,
        TodayCoverage = Coverage(state, slug == "denver" ? 24 : 25, contributing),
        Last24Coverage = Coverage(state, 24, contributing)
    };

    static StatsBubbleStatistic Bubble(StatsCityWindow city) => new()
    {
        EntityKey = $"city:{city.CitySlug}", EntityKind = "city", CitySlug = city.CitySlug,
        Label = city.DisplayNameKey, X = city.CitySlug == "boston" ? null : 5m,
        Y = city.CitySlug == "boston" ? null : 10m,
        MeanActiveVehicles = city.CitySlug == "boston" ? null : 1.5m,
        DistanceKilometers = city.CitySlug == "boston" ? null : 20m,
        RouteVehicleHours = city.CitySlug == "boston" ? null : 4,
        ActiveClockHours = city.CitySlug == "boston" ? null : 2,
        ActivitySampleNumerator = city.CitySlug == "boston" ? null : 8,
        SampleDenominator = city.CitySlug == "boston" ? null : 8,
        Coverage = city.TodayCoverage,
        OmissionReason = city.CitySlug == "boston" ? "history_disabled" : null
    };

    static StatsCoverage Coverage(string state, int expected, int contributing) => new()
    {
        State = state, ExpectedClosedHours = expected, ContributingHours = contributing,
        FullyCoveredHours = contributing, AvailableRouteHours = contributing,
        PendingThroughUtc = Cutoff, Exclusions = []
    };
}
