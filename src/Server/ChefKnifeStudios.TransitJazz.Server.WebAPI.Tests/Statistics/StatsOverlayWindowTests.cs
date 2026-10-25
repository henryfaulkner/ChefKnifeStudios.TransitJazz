using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class StatsOverlayWindowTests
{
    [Theory]
    [InlineData("2026-04-02T04:00:00Z", 23)]
    [InlineData("2026-04-02T04:02:00Z", 24)]
    [InlineData("2026-04-02T05:01:59Z", 24)]
    [InlineData("2026-04-02T05:02:00Z", 0)]
    public void PreviousDayIsAvailableOnlyUntilTheFirstEligibleCityHour(string asOf, int expectedHours)
    {
        var options = Options(City("atlanta", "America/New_York"));
        var request = new StatsOverlayRequest { Scope = "city", CitySlug = "atlanta", RouteJoinKeys = ["10"] };
        var today = StatsOverlayPolicy.Resolve(request, options, DateTimeOffset.Parse(asOf));

        var previous = StatsOverlayPolicy.ResolvePreviousDay(today);

        if (expectedHours == 0)
        {
            Assert.Null(previous);
            Assert.Single(today.Cities[0].TodayHoursUtc);
            return;
        }
        Assert.NotNull(previous);
        var city = Assert.Single(previous.Cities);
        Assert.Equal(new DateOnly(2026, 4, 1), city.LocalDate);
        Assert.Equal(expectedHours, city.TodayHoursUtc.Count);
        Assert.Equal(today.Cities[0].LocalDayStartUtc, city.LocalDayEndUtc);
        Assert.Equal(today.CutoffUtc, previous.CutoffUtc);
        Assert.Equal(today.GeneratedAtUtc, previous.GeneratedAtUtc);
        Assert.Equal(today.Selection, previous.Selection);
        Assert.Equal(today.Cities[0].Last24HoursUtc, city.Last24HoursUtc);
    }

    [Theory]
    [InlineData("2026-03-09T04:30:00Z", 23)]
    [InlineData("2026-11-02T05:30:00Z", 25)]
    public void PreviousDayFollowsCalendarBoundariesAcrossDaylightSaving(string asOf, int dayHours)
    {
        var options = Options(City("atlanta", "America/New_York"));
        var plan = StatsOverlayPolicy.Resolve(new StatsOverlayRequest
        {
            Scope = "city", CitySlug = "atlanta", RouteJoinKeys = []
        }, options, DateTimeOffset.Parse(asOf));

        var previous = StatsOverlayPolicy.ResolvePreviousDay(plan);

        Assert.NotNull(previous);
        var city = Assert.Single(previous.Cities);
        Assert.Equal(plan.Cities[0].LocalDate.AddDays(-1), city.LocalDate);
        Assert.Equal(dayHours, city.TodayHoursUtc.Count);
        Assert.Equal(TimeSpan.FromHours(dayHours), city.LocalDayEndUtc - city.LocalDayStartUtc);
        Assert.Equal(24, city.Last24HoursUtc.Count);
    }

    [Fact]
    public void AllCitiesKeepsItsExistingLocalDayWindows()
    {
        var options = Options(City("atlanta", "America/New_York"));
        var plan = StatsOverlayPolicy.Resolve(new StatsOverlayRequest { Scope = "all", RouteJoinKeys = [] },
            options, DateTimeOffset.Parse("2026-04-02T04:30:00Z"));

        Assert.Null(StatsOverlayPolicy.ResolvePreviousDay(plan));
    }

    [Theory]
    [InlineData("2026-10-11T14:01:00+00:00", "2026-10-11T13:00:00Z")]
    [InlineData("2026-10-11T14:02:00+00:00", "2026-10-11T14:00:00Z")]
    public void CutoffUsesTwoMinuteGrace(string asOf, string expectedCutoff)
    {
        var options = Options(City("atlanta", "America/New_York"));
        var request = StatsOverlayPolicy.NormalizeRequest(new StatsOverlayRequest
        {
            Scope = "city", CitySlug = "atlanta", RouteJoinKeys = []
        }, options);

        var plan = StatsOverlayPolicy.Resolve(request, options, DateTimeOffset.Parse(asOf));

        Assert.Equal(DateTime.Parse(expectedCutoff).ToUniversalTime(), plan.CutoffUtc);
    }

    [Fact]
    public void LocalDateComesFromAsOfEvenWhenCutoffIsOnPreviousDate()
    {
        var options = Options(City("denver", "America/Denver"));
        var request = StatsOverlayPolicy.NormalizeRequest(new StatsOverlayRequest
        {
            Scope = "city", CitySlug = "denver", RouteJoinKeys = []
        }, options);

        var plan = StatsOverlayPolicy.Resolve(request, options, DateTimeOffset.Parse("2026-04-02T06:01:00Z"));
        var window = Assert.Single(plan.Cities);

        Assert.Equal(new DateOnly(2026, 4, 2), window.LocalDate);
        Assert.Equal(DateTime.Parse("2026-04-02T06:00:00Z").ToUniversalTime(), window.LocalDayStartUtc);
        Assert.True(plan.CutoffUtc < window.LocalDayStartUtc);
        Assert.Empty(window.TodayHoursUtc);
        Assert.Null(window.EligibleTodayStartUtc);
        Assert.Null(window.EligibleTodayEndUtc);
    }

    [Fact]
    public void TodayCanBePendingWhileLast24RemainsAnIndependentFullWindow()
    {
        var options = Options(City("atlanta", "America/New_York"));
        var request = StatsOverlayPolicy.NormalizeRequest(new StatsOverlayRequest
        {
            Scope = "city", CitySlug = "atlanta", RouteJoinKeys = []
        }, options);

        var plan = StatsOverlayPolicy.Resolve(request, options, DateTimeOffset.Parse("2026-04-02T04:30:00Z"));
        var window = Assert.Single(plan.Cities);

        Assert.Empty(window.TodayHoursUtc);
        Assert.Equal(24, window.Last24HoursUtc.Count);
        Assert.Equal(plan.CutoffUtc.AddHours(-24), window.Last24StartUtc);
        Assert.Equal(plan.CutoffUtc, window.Last24EndUtc);
    }

    [Theory]
    [InlineData("2024-03-10T16:00:00Z", 23, 10)]
    [InlineData("2024-11-03T16:00:00Z", 25, 11)]
    public void LocalDayBoundariesFollowDaylightSavingLength(string asOf, int expectedDayHours, int expectedCompletedHours)
    {
        var options = Options(City("atlanta", "America/New_York"));
        var request = StatsOverlayPolicy.NormalizeRequest(new StatsOverlayRequest
        {
            Scope = "city", CitySlug = "atlanta", RouteJoinKeys = []
        }, options);

        var window = Assert.Single(StatsOverlayPolicy.Resolve(request, options, DateTimeOffset.Parse(asOf)).Cities);

        Assert.Equal(TimeSpan.FromHours(expectedDayHours), window.LocalDayEndUtc - window.LocalDayStartUtc);
        Assert.Equal(expectedCompletedHours, window.TodayHoursUtc.Count);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var localHours = window.TodayHoursUtc.Select(hour => TimeZoneInfo.ConvertTime(new DateTimeOffset(hour), zone)).ToArray();
        if (expectedDayHours == 23)
        {
            Assert.DoesNotContain(localHours, value => value.Hour == 2);
        }
        else
        {
            var repeatedOneOClock = localHours.Where(value => value.Hour == 1).ToArray();
            Assert.Equal(2, repeatedOneOClock.Length);
            Assert.NotEqual(repeatedOneOClock[0].Offset, repeatedOneOClock[1].Offset);
        }
    }

    [Theory]
    [InlineData("2026-03-08T16:00:00Z", 23, 7)]
    [InlineData("2026-11-01T16:00:00Z", 25, 8)]
    public void SeattleLocalDayUsesLosAngelesMidnightAcrossDst(string asOf, int expectedDayHours, int expectedCompletedHours)
    {
        var options = Options(City("seattle", "America/Los_Angeles"));
        var request = StatsOverlayPolicy.NormalizeRequest(new StatsOverlayRequest
        {
            Scope = "city", CitySlug = "seattle", RouteJoinKeys = []
        }, options);

        var window = Assert.Single(StatsOverlayPolicy.Resolve(request, options, DateTimeOffset.Parse(asOf)).Cities);

        Assert.Equal(new DateOnly(DateTime.Parse(asOf).Year, DateTime.Parse(asOf).Month, DateTime.Parse(asOf).Day), window.LocalDate);
        Assert.Equal(TimeSpan.FromHours(expectedDayHours), window.LocalDayEndUtc - window.LocalDayStartUtc);
        Assert.Equal(expectedCompletedHours, window.TodayHoursUtc.Count);
        var expectedStart = expectedDayHours == 23 ? "2026-03-08T08:00:00Z" : "2026-11-01T07:00:00Z";
        Assert.Equal(DateTime.Parse(expectedStart).ToUniversalTime(), window.LocalDayStartUtc);
    }

    [Fact]
    public void FractionalOffsetMidnightExcludesPartialUtcBoundaryHour()
    {
        var options = Options(City("kathmandu", "Asia/Kathmandu"));
        var request = StatsOverlayPolicy.NormalizeRequest(new StatsOverlayRequest
        {
            Scope = "city", CitySlug = "kathmandu", RouteJoinKeys = []
        }, options);

        var window = Assert.Single(StatsOverlayPolicy.Resolve(request, options, DateTimeOffset.Parse("2026-04-02T05:00:00Z")).Cities);

        Assert.Equal(DateTime.Parse("2026-04-01T18:15:00Z").ToUniversalTime(), window.LocalDayStartUtc);
        Assert.Equal(9, window.TodayHoursUtc.Count);
        Assert.Equal(DateTime.Parse("2026-04-01T19:00:00Z").ToUniversalTime(), window.TodayHoursUtc[0]);
        Assert.Equal(1, window.BoundaryExcludedTodayHours);
    }

    [Fact]
    public void RequestValidationPreservesOrdinalIdentityAndRejectsScopeWidening()
    {
        var options = Options(City("atlanta", "America/New_York"));
        var normalized = StatsOverlayPolicy.NormalizeRequest(new StatsOverlayRequest
        {
            Scope = "city", CitySlug = "atlanta", RouteJoinKeys = ["10a", "10A", "10a", "10"]
        }, options);

        Assert.Equal(new[] { "10", "10A", "10a" }, normalized.RouteJoinKeys);
        Assert.Throws<StatsOverlayRequestException>(() => StatsOverlayPolicy.NormalizeRequest(new StatsOverlayRequest
        {
            Scope = "all", CitySlug = null, RouteJoinKeys = ["10"]
        }, options));
        Assert.Throws<StatsOverlayRequestException>(() => StatsOverlayPolicy.NormalizeRequest(new StatsOverlayRequest
        {
            Scope = "city", CitySlug = "marta", RouteJoinKeys = []
        }, options));
    }

    [Fact]
    public void InvalidZoneOrCadenceFailsConfigurationValidation()
    {
        var invalidZone = Options(City("atlanta", "Mars/Olympus"));
        Assert.Throws<ArgumentException>(() => invalidZone.Validate(requireCities: true));

        var invalidCadence = Options(City("atlanta", "America/New_York", cadence: 61));
        Assert.Throws<ArgumentOutOfRangeException>(() => invalidCadence.Validate(requireCities: true));
    }

    static StatsOverlayOptions Options(params StatsOverlayCityOptions[] cities) => new()
    {
        IngestionGraceMinutes = 2,
        Cities = cities.ToList()
    };

    static StatsOverlayCityOptions City(string slug, string zone, int cadence = 30) => new()
    {
        CitySlug = slug,
        DisplayNameKey = slug,
        TimeZoneId = zone,
        HealthyCadenceLimitSeconds = cadence
    };
}
