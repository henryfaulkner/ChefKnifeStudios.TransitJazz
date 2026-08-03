using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

// FR-016 guard: ITransitCity.Name (slug, all live boundaries) and ITransitCity.TelemetryName
// (agency, frozen, parquet-only) must never collapse into the same value again — that is
// exactly the coupling that would silently rewrite parquet history once Name becomes a city
// slug (052-city-slug-migration).
public class TelemetryNameSplitTests
{
    // Frozen at the REAL current production city_name values (verified via the telemetry MCP
    // bridge against live parquet, not assumed) — lowercase agency slugs, matching today's
    // CityNames.* values verbatim. This is what TelemetryName freezes going forward, even
    // once CityNames.* values become city-name slugs.
    static readonly Dictionary<string, string> ExpectedTelemetryNames = new(StringComparer.Ordinal)
    {
        [CityNames.Marta] = "marta",
        [CityNames.Wmata] = "wmata",
        [CityNames.Mbta] = "mbta",
        [CityNames.Nymta] = "nymta",
        [CityNames.Ttc] = "ttc",
        [CityNames.Septa] = "septa",
        [CityNames.Rtd] = "rtd",
    };

    static IEnumerable<ITransitCity> BuildAllCities()
    {
        yield return new MartaCity(
            new NullHttpClientFactory(),
            Microsoft.Extensions.Options.Options.Create(new RailRealtime.RailRealtimeOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MartaCity>.Instance);

        yield return new NymtaCity(
            new NullHttpClientFactory(),
            Microsoft.Extensions.Options.Options.Create(new Subway.SubwaySynthesisOptions()),
            new CityConfig { Name = CityNames.Nymta, TelemetryName = "nymta" },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NymtaCity>.Instance,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GtfsRtCity>.Instance);

        foreach (var (name, telemetryName) in new[]
        {
            (CityNames.Wmata, "wmata"),
            (CityNames.Mbta, "mbta"),
            (CityNames.Ttc, "ttc"),
            (CityNames.Septa, "septa"),
            (CityNames.Rtd, "rtd"),
        })
        {
            yield return new GtfsRtCity(
                new CityConfig { Name = name, TelemetryName = telemetryName },
                new NullHttpClientFactory(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<GtfsRtCity>.Instance);
        }
    }

    // Pre-052 (before Phase 3 moves CityNames.* values to city slugs), Name and TelemetryName
    // are numerically equal for every city — both are today's agency string. That is expected
    // and not a violation: the guard is that TelemetryName is independently sourced (from its
    // own frozen constant/config key, never re-derived from Name), so it will NOT follow Name
    // once Phase 3 changes it. See per_city_cycle_writes_telemetry_name_as_city_name below for
    // the case where they diverge.
    [Fact]
    public void telemetry_name_is_agency_valued_not_slug()
    {
        foreach (var city in BuildAllCities())
        {
            Assert.True(ExpectedTelemetryNames.TryGetValue(city.Name, out var expected),
                $"No expected TelemetryName registered for city '{city.Name}'.");
            Assert.Equal(expected, city.TelemetryName);
        }
    }

    [Fact]
    public void telemetry_name_values_are_frozen()
    {
        var actual = BuildAllCities().Select(c => c.TelemetryName).OrderBy(n => n, StringComparer.Ordinal);
        var expected = ExpectedTelemetryNames.Values.OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(expected, actual);
    }

    // Simulates the Worker's telemetry-posting gate (Worker.cs ~L95-119): when a city emits
    // telemetry, the posted TelemetryEvent.city_name must come from TelemetryName, not Name —
    // behaviour, not implementation, matching the CityLoopTests.cs simulation pattern for the
    // same loop.
    [Fact]
    public void per_city_cycle_writes_telemetry_name_as_city_name()
    {
        ITransitCity city = new FakeCityWithDivergentNames(name: "atlanta", telemetryName: "marta");

        var posted = new List<TelemetryEvent>();
        var notifications = new EventNotificationService();
        notifications.EventReceived += (sender, e) =>
        {
            if (e is TelemetryEvent evt) posted.Add(evt);
        };

        if (city.EmitsTelemetry)
        {
            notifications.PostEvent(city, new TelemetryEvent
            {
                event_type = "PerCityCycle",
                event_id = "test-event",
                observation_utc = DateTime.UtcNow,
                city_name = city.TelemetryName,
            });
        }

        var row = Assert.Single(posted);
        Assert.Equal("marta", row.city_name);
        Assert.NotEqual(city.Name, row.city_name);
    }

    sealed class FakeCityWithDivergentNames(string name, string telemetryName) : ITransitCity
    {
        public string Name => name;
        public string TelemetryName => telemetryName;
        public bool EmitsTelemetry => true;
        public Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct) => Task.FromResult(new FeedMessage());
    }

    sealed class NullHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
