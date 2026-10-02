using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class CategoryActivityQueryTests
{
    [CategoryPostgresFact]
    public async Task RepeatedTwoZeroFourSamplesHaveMeanTwoAndExposeTheSampleCount()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        // Each complete minute has the three eligible counts 2, 0, 4: 6/3 = 2.
        await CategoryQueryData.WriteHourAsync(database, samplesPerMinute: 3, activeCountSumPerMinute: 6);

        var result = Assert.Single(await CategoryQueryFixture.ReadAsync(database, "activity-insights.sql"));

        Assert.Equal(360m, result["active_vehicle_count_sum"]);
        Assert.Equal(180m, result["valid_active_sample_count"]);
        Assert.Equal(2m, result["avg_active_vehicles"]);
        Assert.Equal(1L, result["complete_hour_count"]);
        Assert.Equal("observed vehicles", result["unit"]);
    }

    [CategoryPostgresFact]
    public async Task PeriodActivityWeightsUnderlyingSamplesInsteadOfHourlyMeans()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await CategoryQueryData.WriteHourAsync(database, samplesPerMinute: 1, activeCountSumPerMinute: 2);
        await CategoryQueryData.WriteHourAsync(database, start: CategoryStatisticsTestDatabase.HourStart.AddHours(1), samplesPerMinute: 3, activeCountSumPerMinute: 12);

        var result = Assert.Single(await CategoryQueryFixture.ReadAsync(database, "activity-insights.sql",
            toUtc: CategoryStatisticsTestDatabase.HourStart.AddHours(2)));

        Assert.Equal(840m, result["active_vehicle_count_sum"]);
        Assert.Equal(240m, result["valid_active_sample_count"]);
        Assert.Equal(3.5m, result["avg_active_vehicles"]);
        Assert.Equal(2L, result["complete_hour_count"]);
    }

    [CategoryPostgresFact]
    public async Task NoCompleteHistoryDisclosesZeroDenominatorAndUnavailableMean()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();

        var result = Assert.Single(await CategoryQueryFixture.ReadAsync(database, "activity-insights.sql"));

        Assert.Equal(0L, result["complete_hour_count"]);
        Assert.Equal(0m, result["valid_active_sample_count"]);
        Assert.Null(result["avg_active_vehicles"]);
        Assert.Null(result["first_collectible_utc"]);
        Assert.Null(result["contributing_from_utc"]);
        Assert.Null(result["contributing_to_utc"]);
    }

    [CategoryPostgresFact]
    public async Task HealthyEmptyHourReturnsZeroActivityWithPositiveDenominator()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await CategoryQueryData.WriteHourAsync(database, activeCountSumPerMinute: 0, distinctVehicles: 0, distance: 0, intervals: 0, crossings: 0);

        var result = Assert.Single(await CategoryQueryFixture.ReadAsync(database, "activity-insights.sql"));

        Assert.Equal(0m, result["avg_active_vehicles"]);
        Assert.Equal(60m, result["valid_active_sample_count"]);
        Assert.Equal(1L, result["complete_hour_count"]);
    }
}

internal static class CategoryQueryData
{
    public static async Task WriteHourAsync(CategoryStatisticsTestDatabase database, DateTime? start = null,
        string category = "bus", int samplesPerMinute = 1, int activeCountSumPerMinute = 2,
        decimal distance = 1200m, long intervals = 60, long crossings = 60, long distinctVehicles = 3,
        int cadence = 30, string definitionVersion = "observed-city-category-statistics-v1")
    {
        var at = start ?? CategoryStatisticsTestDatabase.HourStart;
        var minutes = CategoryStatisticsTestDatabase.Minutes();
        decimal allocatedDistance = 0;
        long allocatedIntervals = 0;
        foreach (var minute in minutes)
        {
            var index = minute.StatMinuteUtc.Minute;
            minute.StatMinuteUtc = at.AddMinutes(index);
            minute.FirstCycleUtc = minute.StatMinuteUtc.AddSeconds(5);
            minute.LastCycleUtc = minute.FirstCycleUtc.Value.AddSeconds((samplesPerMinute - 1) * 10);
            minute.Category = category; minute.DefinitionVersion = definitionVersion;
            minute.HealthyCadenceLimitSeconds = cadence;
            minute.ObservedCycleCount = minute.ValidActiveSampleCount = minute.ValidPublishCycleCount = samplesPerMinute;
            minute.ActiveVehicleCountSum = activeCountSumPerMinute;
            minute.DistanceIntervalCount = intervals / 60 + (index < intervals % 60 ? 1 : 0);
            allocatedIntervals += minute.DistanceIntervalCount;
            minute.DistanceMetersSum = minute.DistanceIntervalCount == 0 ? 0
                : allocatedIntervals == intervals ? distance - allocatedDistance
                : decimal.Round(distance * minute.DistanceIntervalCount / intervals, 6);
            allocatedDistance += minute.DistanceMetersSum;
            minute.CrossingsPublishedCount = index == 0 ? crossings : 0;
        }
        var hour = CategoryStatisticsTestDatabase.Hour();
        hour.HourStartUtc = at; hour.Category = category; hour.DefinitionVersion = definitionVersion;
        hour.HealthyCadenceLimitSeconds = cadence;
        hour.FirstCycleUtc = minutes[0].FirstCycleUtc; hour.LastCycleUtc = minutes[^1].LastCycleUtc;
        hour.ObservedCycleCount = hour.ValidActiveSampleCount = hour.ValidPublishCycleCount = samplesPerMinute * 60;
        hour.ActiveVehicleCountSum = activeCountSumPerMinute * 60;
        hour.DistanceMetersSum = distance; hour.DistanceIntervalCount = intervals;
        hour.CrossingsPublishedCount = crossings; hour.DistinctActiveVehicleCount = distinctVehicles;
        var report = await database.Store.WriteAsync(minutes, [hour]);
        Assert.Equal(61, report.Inserted);
        Assert.Equal(0, report.BackingMinutesUnavailable);
    }
}
