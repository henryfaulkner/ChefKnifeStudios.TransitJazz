using ChefKnifeStudios.TransitJazz.Server.Data;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class CityMinuteStatisticModelTests
{
    [Fact]
    public void ModelContainsOnlyTheWideStatisticEntityAndCompositeKey()
    {
        using var context = CreateContext();
        var model = context.Model;
        var entity = model.FindEntityType(typeof(CityMinuteStatistic));

        Assert.NotNull(entity);
        Assert.Single(model.GetEntityTypes());
        Assert.Equal(["CitySlug", "StatMinuteUtc"], entity!.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Empty(entity.GetIndexes());
        Assert.DoesNotContain(entity.GetProperties(), property => property.Name is "Id" or "CreatedOnUtc" or "ModifiedOnUtc" or "IsDeleted");
    }

    [Fact]
    public void MappingUsesExplicitPostgresTypesAndNullableSourceColumns()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(CityMinuteStatistic))!;

        Assert.Equal("city_minute_statistics", entity.GetTableName());
        Assert.Equal("timestamp with time zone", entity.FindProperty(nameof(CityMinuteStatistic.StatMinuteUtc))!.GetColumnType());
        Assert.Equal("numeric(20,6)", entity.FindProperty(nameof(CityMinuteStatistic.CycleRatePerSecond))!.GetColumnType());
        Assert.Equal("bigint", entity.FindProperty(nameof(CityMinuteStatistic.VehiclesProcessed))!.GetColumnType());
        Assert.Equal("boolean", entity.FindProperty(nameof(CityMinuteStatistic.Healthy))!.GetColumnType());
        Assert.All(entity.GetProperties().Where(property => property.Name is not nameof(CityMinuteStatistic.CitySlug) and not nameof(CityMinuteStatistic.StatMinuteUtc)
            and not nameof(CityMinuteStatistic.SourceDefinitionVersion) and not nameof(CityMinuteStatistic.CollectionStatus)),
            property => Assert.True(property.IsNullable));
    }

    [Fact]
    public void ValidationPreservesZeroNullAndMinuteSemantics()
    {
        var statistic = new CityMinuteStatistic
        {
            CitySlug = "atlanta",
            StatMinuteUtc = new DateTime(2026, 9, 20, 15, 4, 0, DateTimeKind.Utc),
            CollectionStatus = CollectionStatus.Partial,
            Healthy = false,
            InputRecordsValid = 0,
        };

        statistic.Validate(["atlanta", "boston"]);

        Assert.False(statistic.Healthy);
        Assert.Equal(0, statistic.InputRecordsValid);
        Assert.Null(statistic.TonesEmitted);
    }

    [Fact]
    public void ValidationRejectsNonMinuteAndInvalidRatioValues()
    {
        var statistic = new CityMinuteStatistic
        {
            CitySlug = "atlanta",
            StatMinuteUtc = new DateTime(2026, 9, 20, 15, 4, 12, DateTimeKind.Utc),
            CollectionStatus = CollectionStatus.Partial,
            Healthy = true,
        };

        Assert.Throws<ArgumentException>(() => statistic.Validate());

        statistic.StatMinuteUtc = new DateTime(2026, 9, 20, 15, 4, 0, DateTimeKind.Utc);
        statistic.InputRecordsValid = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => statistic.Validate());
    }

    static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=transitjazz")
            .Options);
}
