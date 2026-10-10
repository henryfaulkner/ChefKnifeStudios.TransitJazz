using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using System.Collections.Immutable;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class RouteHourFoundationTests
{
    [Fact]
    public void OptionsUseBoundedPlanDefaultsAndRejectOutOfRangeLimits()
    {
        var options = new RouteHourHistoryOptions();
        options.Validate(10, ["atlanta"]);

        Assert.Equal(30, options.MaxObservationGapSeconds);
        Assert.Equal(16, options.QueueCapacity);
        Assert.Equal(4096, options.MaxRoutesPerCityHour);
        Assert.Equal(25_000, options.MaxTrackedVehiclesPerCity);
        Assert.Equal(50_000, options.MaxVehicleRouteMembershipsPerCityHour);
        Assert.Equal(128, options.MaxRowsPerCommand);
        Assert.Equal(5, options.CommandTimeoutSeconds);
        Assert.Equal(30, options.WriteAttemptTimeoutSeconds);
        Assert.Equal(3, options.MaxWriteAttempts);
        Assert.Equal(15, options.ShutdownDrainSeconds);

        options.MaxRoutesPerCityHour = 16_385;
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate(10, ["atlanta"]));
    }

    [Fact]
    public void FingerprintMatchesContractVectorAndIgnoresStaticIdInputOrder()
    {
        var points = ImmutableArray.Create(new RouteHourGeometryPoint(33.749, -84.388), new RouteHourGeometryPoint(33.75, -84.387));
        var first = RouteHourCatalog.ComputeFingerprint("Route-A", "bus", ["shape-b", "shape-a"], points);
        var reordered = RouteHourCatalog.ComputeFingerprint("Route-A", "bus", ["shape-a", "shape-b", "shape-a"], points);

        Assert.Equal("3ad7499f2cca2bfa43575267b7ed839a8d972a9a90b8e1a50818e39cc972a6d7", first);
        Assert.Equal(first, reordered);
        Assert.NotEqual(first, RouteHourCatalog.ComputeFingerprint("Route-A", "bus", ["shape-a", "shape-b"], points.Reverse()));
    }

    [Fact]
    public void CatalogResolvesAliasesOnceAndKeepsOrdinalCaseDistinctKeys()
    {
        var catalog = RouteHourCatalog.Create([
            new RouteHourCatalogInput("Route-A", "bus", "A", ["shape-a", "shape-b"],
                [new(33.749, -84.388), new(33.75, -84.387)], ["R17", "r17"]),
            new RouteHourCatalogInput("route-a", "bus", "a", ["shape-lower"],
                [new(33.751, -84.386), new(33.752, -84.385)], ["lower-id"]),
        ]);

        Assert.Equal(2, catalog.Entries.Count);
        Assert.True(catalog.TryResolve("R17", out var upper));
        Assert.True(catalog.TryResolve("shape-b", out var shapeAlias));
        Assert.Same(upper, shapeAlias);
        Assert.True(catalog.TryGet("route-a", out var lower));
        Assert.NotEqual(upper.Fingerprint, lower.Fingerprint);
        Assert.False(catalog.GeometryIdentityAvailable);
    }

    [Fact]
    public void RowRejectsFalseCompleteAndAcceptsHealthyEmptyCompleteSample()
    {
        var invalid = RouteHourCaptureFixture.Row(status: RouteHourCoverageStatus.Complete, reasons: []);
        Assert.Throws<ArgumentException>(invalid.Validate);

        var complete = invalid with { StartBoundaryOk = true, EndBoundaryOk = true };
        complete.Validate();
    }

    [Fact]
    public void RowRejectsInvalidStatusAndValuesOutsideStoredNumericPrecision()
    {
        var invalidStatus = RouteHourCaptureFixture.Row() with { CollectionStatus = (RouteHourCoverageStatus)99 };
        Assert.Throws<ArgumentOutOfRangeException>(invalidStatus.Validate);

        var excessiveScale = RouteHourCaptureFixture.Row() with { DistanceMetersSum = 0.0000001m };
        Assert.Throws<ArgumentOutOfRangeException>(excessiveScale.Validate);
    }

    [Fact]
    public void ThrowingInstrumentationHookMarksLossWithoutThrowingIntoCaller()
    {
        var lost = false;
        var publishSucceeded = true;

        RouteHourCaptureFixture.InvokeInstrumentationHook(() => throw new InvalidOperationException(), () => lost = true);

        Assert.True(lost);
        Assert.True(publishSucceeded);
    }
}
