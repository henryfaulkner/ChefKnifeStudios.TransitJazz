using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class StatsOverlayWindowTests
{
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
