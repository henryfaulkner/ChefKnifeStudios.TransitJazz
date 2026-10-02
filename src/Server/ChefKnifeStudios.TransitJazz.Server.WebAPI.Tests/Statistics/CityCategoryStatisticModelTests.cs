using ChefKnifeStudios.TransitJazz.Server.Data;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class CityCategoryStatisticModelTests
{
    [Fact]
    public void ModelMapsTwoAggregateTablesWithCompositeKeysAndNoIdentifiers()
    {
        using var context = CreateContext();
        var minute = context.Model.FindEntityType(typeof(CityCategoryMinuteStatistic))!;
        var hour = context.Model.FindEntityType(typeof(CityCategoryHourStatistic))!;
        Assert.Equal(["CitySlug", "Category", "StatMinuteUtc"], minute.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Equal(["CitySlug", "Category", "HourStartUtc"], hour.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Equal("city_category_minute_statistics", minute.GetTableName());
        Assert.Equal("city_category_hour_statistics", hour.GetTableName());
        Assert.Empty(minute.GetIndexes());
        Assert.Empty(hour.GetIndexes());
        Assert.DoesNotContain(minute.GetProperties(), x => x.Name is "Id" or "CreatedOnUtc" or "ModifiedOnUtc" or "IsDeleted");
        Assert.DoesNotContain(hour.GetProperties(), x => x.Name is "Id" or "CreatedOnUtc" or "ModifiedOnUtc" or "IsDeleted");
        Assert.Equal("numeric(20,6)", minute.FindProperty(nameof(CityCategoryMinuteStatistic.DistanceMetersSum))!.GetColumnType());
        Assert.Equal("bigint", hour.FindProperty(nameof(CityCategoryHourStatistic.DistinctActiveVehicleCount))!.GetColumnType());
    }

    [Fact]
    public void MinuteValidationSeparatesZeroFromUnavailableAndRequiresCompleteEvidence()
    {
        var minute = new CityCategoryMinuteStatistic
        {
            CitySlug = "atlanta", Category = "bus", StatMinuteUtc = new(2026, 9, 20, 15, 4, 0, DateTimeKind.Utc),
            CollectionStatus = CategoryCollectionStatus.Complete, HealthyCadenceLimitSeconds = 30,
            ObservedCycleCount = 1, ValidActiveSampleCount = 1, ValidPublishCycleCount = 1,
            FirstCycleUtc = new(2026, 9, 20, 15, 4, 10, DateTimeKind.Utc), LastCycleUtc = new(2026, 9, 20, 15, 4, 10, DateTimeKind.Utc),
            MaxObservationGapSeconds = 10,
        };
        minute.Validate();
        Assert.Equal(0, minute.ActiveVehicleCountSum);

        minute.ValidPublishCycleCount = 0;
        Assert.Throws<ArgumentException>(minute.Validate);
        minute.CollectionStatus = CategoryCollectionStatus.NoData;
        minute.ValidActiveSampleCount = 0;
        minute.ObservedCycleCount = 1;
        minute.ValidPublishCycleCount = 0;
        minute.Validate();
    }

    static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=transitjazz").Options);
}
