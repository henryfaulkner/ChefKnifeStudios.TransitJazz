using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class CategoryMovementTests
{
    [Fact]
    public void Fresh_stationary_and_multiple_vehicle_intervals_are_counted_individually()
    {
        var fixture = new CategoryStatisticsCaptureFixture();
        var seed = fixture.Begin();
        foreach (var (id, meters) in new[] { ("v1", 0d), ("v2", 100d), ("v3", 200d) })
        {
            Assert.True(fixture.Capture.RecordEligibleVehicle(seed, id, "BUS"));
            Assert.Equal(MovementRejectionReason.FirstObservation,
                fixture.Capture.ObserveMovement(seed, id, "bus", "route-1", 100, meters, 1).RejectionReason);
        }
        fixture.Complete(seed, fixture.Start);

        var update = fixture.Begin();
        Assert.True(fixture.Capture.RecordEligibleVehicle(update, "v1", "bus"));
        var stationary = fixture.Capture.ObserveMovement(update, "v1", "bus", "route-1", 101, 0, 1);
        Assert.True(stationary.Accepted);
        Assert.Equal(0m, stationary.DistanceMeters);
        fixture.Capture.RecordEligibleVehicle(update, "v2", "bus");
        Assert.Equal(10m, fixture.Capture.ObserveMovement(update, "v2", "bus", "route-1", 101, 110, 1).DistanceMeters);
        fixture.Capture.RecordEligibleVehicle(update, "v3", "bus");
        Assert.Equal(20m, fixture.Capture.ObserveMovement(update, "v3", "bus", "route-1", 101, 220, 1).DistanceMeters);
        fixture.Complete(update, fixture.Start.AddSeconds(10));

        var close = fixture.Begin();
        fixture.Complete(close, fixture.Start.AddMinutes(1));

        var minute = Assert.Single(fixture.Sink.Batches.SelectMany(x => x.Minutes), x => x.WindowStartUtc == fixture.Start.UtcDateTime);
        Assert.Equal(3, minute.DistanceIntervalCount);
        Assert.Equal(30m, minute.DistanceMetersSum);
        Assert.Equal(3, minute.DistanceRejectedCount);
    }

    [Fact]
    public void Timestamp_order_is_checked_before_route_transfer_and_geometry_reseeds()
    {
        var fixture = new CategoryStatisticsCaptureFixture();
        CategoryMovementObservation Observe(ulong? timestamp, string route, double meters, long generation = 1)
        {
            var cycle = fixture.Begin(generation);
            fixture.Capture.RecordEligibleVehicle(cycle, "vehicle", "bus");
            return fixture.Capture.ObserveMovement(cycle, "vehicle", "bus", route, timestamp, meters, generation);
        }

        Assert.Equal(MovementRejectionReason.FirstObservation, Observe(100, "route-a", 0).RejectionReason);
        Assert.Equal(MovementRejectionReason.TimestampNotIncreasing, Observe(99, "route-b", 5).RejectionReason);
        Assert.Equal(MovementRejectionReason.RouteTransfer, Observe(101, "route-b", 10).RejectionReason);
        Assert.True(Observe(102, "route-b", 2010).Accepted); // exactly 2,000 meters
        Assert.Equal(MovementRejectionReason.TimestampNotIncreasing, Observe(102, "route-c", 0).RejectionReason);
        Assert.Equal(MovementRejectionReason.RouteTransfer, Observe(103, "route-c", 0).RejectionReason);
        Assert.Equal(MovementRejectionReason.FirstObservation, Observe(104, "route-c", 0, 2).RejectionReason);
        Assert.Equal(MovementRejectionReason.MissingTimestamp, Observe(null, "route-c", 0, 2).RejectionReason);
        Assert.Equal(MovementRejectionReason.FirstObservation, Observe(105, "route-c", 0, 2).RejectionReason);
    }

    [Fact]
    public void Only_first_eligible_joined_identity_in_a_cycle_is_the_representative()
    {
        var fixture = new CategoryStatisticsCaptureFixture();
        var cycle = fixture.Begin();

        Assert.True(fixture.Capture.RecordEligibleVehicle(cycle, "vehicle-1", "BUS"));
        Assert.False(fixture.Capture.RecordEligibleVehicle(cycle, "vehicle-1", "rail"));

        var snapshot = cycle.ToSnapshot(fixture.Start.UtcDateTime, activityEligible: true, publicationKnown: true, cycleFailed: false);
        Assert.Single(snapshot.ActivityVehiclesByCategory["bus"]);
        Assert.False(snapshot.ActivityVehiclesByCategory.ContainsKey("rail"));
    }

    [Fact]
    public void Invalid_and_geometry_cleared_timestamp_watermarks_prune_independently()
    {
        var fixture = new CategoryStatisticsCaptureFixture();
        var invalid = fixture.Begin();
        Assert.Equal(MovementRejectionReason.InvalidGeometry,
            fixture.Capture.ObserveMovement(invalid, "invalid-only", "bus", "route", 100, double.NaN, 1).RejectionReason);
        Assert.Equal(1, TimestampWatermarkCount(fixture.Capture));

        var baseline = fixture.Begin();
        Assert.Equal(MovementRejectionReason.FirstObservation,
            fixture.Capture.ObserveMovement(baseline, "geometry-cleared", "bus", "route", 200, 10, 1).RejectionReason);
        fixture.Capture.InvalidateGeometry(CategoryStatisticsCaptureFixture.City, 2);
        Assert.Equal(2, TimestampWatermarkCount(fixture.Capture));

        fixture.Clock.SetUtcNow(fixture.Start.AddMinutes(21));
        fixture.Capture.PruneMovement(fixture.Start.AddMinutes(1).UtcDateTime);
        Assert.Equal(0, TimestampWatermarkCount(fixture.Capture));

        var afterPrune = fixture.Begin(2);
        Assert.Equal(MovementRejectionReason.FirstObservation,
            fixture.Capture.ObserveMovement(afterPrune, "geometry-cleared", "bus", "route", 200, 10, 2).RejectionReason);
    }

    [Fact]
    public void Catalog_refresh_rejects_old_cycle_movement_without_seeding_new_generation_watermark()
    {
        var fixture = new CategoryStatisticsCaptureFixture();
        var oldCycle = fixture.Begin(1);
        fixture.Capture.RecordEligibleVehicle(oldCycle, "vehicle", "bus");
        fixture.Capture.InvalidateGeometry(CategoryStatisticsCaptureFixture.City, 2);

        var observation = fixture.Capture.ObserveMovement(oldCycle, "vehicle", "bus", "route", 100, 20, 2);

        Assert.Equal(MovementRejectionReason.GeometryChanged, observation.RejectionReason);
        Assert.Equal(0, TimestampWatermarkCount(fixture.Capture));
    }

    static int TimestampWatermarkCount(CityCategoryStatisticsCapture capture)
    {
        var cities = typeof(CityCategoryStatisticsCapture).GetField("_cities", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(capture)!;
        var state = cities.GetType().GetMethod("get_Item")!.Invoke(cities, [CategoryStatisticsCaptureFixture.City])!;
        var watermarks = state.GetType().GetField("_timestampWatermarks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(state)!;
        return (int)watermarks.GetType().GetProperty("Count")!.GetValue(watermarks)!;
    }
}
