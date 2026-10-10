using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class RouteHourActivityTests
{
    [Fact]
    public void Exact_case_distinct_keys_keep_separate_active_representatives()
    {
        var fixture = new RouteHourCaptureFixture();
        var catalog = RouteHourCatalog.Create([
            new RouteHourCatalogInput("Route-A", "bus", "A", ["shape-upper"], [new(33.7, -84.4), new(33.7, -84.39)], ["upper"]),
            new RouteHourCatalogInput("route-a", "rail", "a", ["shape-lower"], [new(33.8, -84.4), new(33.8, -84.39)], ["lower"]),
        ]);
        var capture = new RouteHourStatisticsCapture(fixture.Runtime(), fixture.Sink, fixture.Clock,
            NullLogger<RouteHourStatisticsCapture>.Instance);
        var at = new DateTime(2026, 10, 1, 12, 10, 0, DateTimeKind.Utc);
        var cycle = capture.BeginCycle(RouteHourCaptureFixture.City, catalog)!;

        Assert.True(capture.RecordEligibleVehicle(cycle, "vehicle-upper", "Route-A"));
        Assert.True(capture.RecordEligibleVehicle(cycle, "vehicle-lower", "route-a"));
        capture.CompleteCycle(cycle, true, true, false, at, catalog);
        capture.FlushPending();

        var rows = fixture.Sink.Batches.SelectMany(batch => batch.Rows).OrderBy(row => row.RouteJoinKey, StringComparer.Ordinal).ToArray();
        Assert.Equal(["Route-A", "route-a"], rows.Select(row => row.RouteJoinKey));
        Assert.All(rows, row => Assert.Equal(1, row.ActiveVehicleCountSum));
        Assert.NotEqual(rows[0].Category, rows[1].Category);
        Assert.All(rows, row => Assert.Contains("route_index_unavailable", row.IncompleteReasons));
    }

    [Fact]
    public void City_scoped_capture_keeps_same_route_and_vehicle_ids_independent()
    {
        var fixture = new RouteHourCaptureFixture();
        var runtime = fixture.Runtime() with
        {
            SelectedCities = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "atlanta", "boston"),
        };
        var capture = new RouteHourStatisticsCapture(runtime, fixture.Sink, fixture.Clock,
            NullLogger<RouteHourStatisticsCapture>.Instance);
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;
        var at = new DateTime(2026, 10, 1, 12, 10, 0, DateTimeKind.Utc);

        foreach (var city in new[] { "atlanta", "boston" })
        {
            var cycle = capture.BeginCycle(city, catalog)!;
            Assert.True(capture.RecordEligibleVehicle(cycle, "same-vehicle-id", "shape-a"));
            capture.CompleteCycle(cycle, true, true, false, at, catalog);
        }
        capture.FlushPending();

        Assert.Equal(new[] { "atlanta", "boston" }, fixture.Sink.Batches.Select(batch => batch.CitySlug).Order(StringComparer.Ordinal));
        Assert.All(fixture.Sink.Batches.SelectMany(batch => batch.Rows), row => Assert.Equal(1, row.ActiveVehicleCountSum));
    }
}
