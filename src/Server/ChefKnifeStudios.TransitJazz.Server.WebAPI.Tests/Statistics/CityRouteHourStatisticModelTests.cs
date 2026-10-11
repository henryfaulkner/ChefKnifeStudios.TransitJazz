using ChefKnifeStudios.TransitJazz.Server.Data;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class CityRouteHourStatisticModelTests
{
    [Fact]
    public void ModelMapsOrdinalHourKeyAndHistoricalIndexWithoutAuditColumns()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(CityRouteHourStatistic));

        Assert.NotNull(entity);
        Assert.Equal("city_route_hour_statistics", entity!.GetTableName());
        Assert.Equal(["CitySlug", "RouteJoinKey", "HourStartUtc"], entity.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Contains(entity.GetIndexes(), index => index.Properties.Select(x => x.Name)
            .SequenceEqual([nameof(CityRouteHourStatistic.CitySlug), nameof(CityRouteHourStatistic.HourStartUtc), nameof(CityRouteHourStatistic.RouteJoinKey)]));
        Assert.DoesNotContain(entity.GetProperties(), property => property.Name is "Id" or "CreatedOnUtc" or "ModifiedOnUtc" or "IsDeleted");
        Assert.DoesNotContain(entity.GetProperties(), property => property.Name.Contains("CoveredMinute", StringComparison.Ordinal));
        Assert.Equal("numeric(20,6)", entity.FindProperty(nameof(CityRouteHourStatistic.DistanceMetersSum))!.GetColumnType());
        Assert.Equal("text[]", entity.FindProperty(nameof(CityRouteHourStatistic.IncompleteReasons))!.GetColumnType());
    }

    [Fact]
    public void ValidationRejectsOversizedOrdinalKeyAndFalseCompleteEvidence()
    {
        var row = PartialRow();
        row.RouteJoinKey = new string('é', 257);
        Assert.Throws<ArgumentException>(row.Validate);

        row = PartialRow();
        row.CollectionStatus = RouteHourCollectionStatus.Complete;
        Assert.Throws<ArgumentException>(row.Validate);
    }

    [Fact]
    public void PartialAggregateRetainsKnownZeroAndNullablePopulation()
    {
        var row = PartialRow();

        row.Validate();

        Assert.Equal(0, row.ActiveVehicleCountSum);
        Assert.Null(row.DistinctActiveVehicleCount);
        Assert.Equal(["boundary_unproven"], row.IncompleteReasons);
    }

    static CityRouteHourStatistic PartialRow() => new()
    {
        CitySlug = "atlanta",
        RouteJoinKey = "Route-A",
        HourStartUtc = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
        RouteShortName = "A",
        StaticRouteId = "shape-a",
        Category = "bus",
        RouteCatalogFingerprint = new string('a', 64),
        DefinitionVersion = CityRouteHourStatistic.CurrentDefinitionVersion,
        CaptureRunId = Guid.Parse("ed6150e0-b01d-4c16-8cf1-5c06826b1b1a"),
        CollectionStatus = RouteHourCollectionStatus.Partial,
        IncompleteReasons = ["boundary_unproven"],
        HealthyCadenceLimitSeconds = 30,
        ObservedCycleCount = 1,
        ValidActiveSampleCount = 1,
        ValidPublishCycleCount = 1,
        FirstCycleUtc = new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc),
        LastCycleUtc = new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc),
        MaxObservationGapSeconds = 10,
        ActiveVehicleCountSum = 0,
        PeakActiveVehicleCount = 0,
    };

    static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=localhost;Database=transitjazz")
        .Options);
}
