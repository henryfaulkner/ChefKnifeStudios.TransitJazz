using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class RouteHourStatisticsQueryTests
{
    [RouteHourPostgresFact]
    public async Task Prepared_route_hour_recipe_reports_missing_pairs_and_retained_historical_keys()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var retainedHour = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        var retained = RouteHourStatisticsStoreTestsRow("route-a", retainedHour);
        await database.Store.WriteAsync([retained]);
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "contracts", "route-hour-insights.sql"));

        await using var context = database.CreateDbContext();
        await context.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var explicitResult = await ExecuteAsync(connection, sql, ["route-a", "route-removed"]);
        var json = JsonDocument.Parse(explicitResult);
        var hourly = json.RootElement.GetProperty("hourly").EnumerateArray().ToArray();
        Assert.Equal(2, hourly.Length);
        Assert.All(hourly, row => Assert.Equal("Missing", row.GetProperty("effective_coverage").GetString()));
        var routeCoverage = json.RootElement.GetProperty("route_coverage").EnumerateArray().ToArray();
        Assert.Equal(2, routeCoverage.Length);
        Assert.Contains(routeCoverage, row => row.GetProperty("route_join_key").GetString() == "route-a"
            && row.GetProperty("earliest_retained_hour_utc").GetDateTime() == retainedHour);
        Assert.Contains(routeCoverage, row => row.GetProperty("route_join_key").GetString() == "route-removed"
            && row.GetProperty("earliest_retained_hour_utc").ValueKind == JsonValueKind.Null);
        Assert.All(json.RootElement.GetProperty("periods").EnumerateArray(), period =>
        {
            Assert.Equal(0, period.GetProperty("contributing_hour_count").GetInt64());
            Assert.Equal(JsonValueKind.Null, period.GetProperty("mean_observed_active_vehicles").ValueKind);
        });

        var allRetainedResult = await ExecuteAsync(connection, sql, null);
        using var allRetainedJson = JsonDocument.Parse(allRetainedResult);
        Assert.Equal("retained_routes", allRetainedJson.RootElement.GetProperty("request").GetProperty("selection").GetString());
        var discovered = Assert.Single(allRetainedJson.RootElement.GetProperty("hourly").EnumerateArray());
        Assert.Equal("route-a", discovered.GetProperty("requested_route_key").GetString());
        Assert.Equal("Missing", discovered.GetProperty("effective_coverage").GetString());
    }

    static async Task<string> ExecuteAsync(NpgsqlConnection connection, string sql, string[]? keys)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = "atlanta" });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text,
            Value = keys is null ? DBNull.Value : keys,
        });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc) });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = new DateTime(2026, 10, 1, 13, 0, 0, DateTimeKind.Utc) });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = CityRouteHourStatistic.CurrentDefinitionVersion });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = "America/New_York" });
        var result = await command.ExecuteScalarAsync();
        return result as string ?? throw new InvalidOperationException("Route-hour query returned no JSON result.");
    }

    static CityRouteHourStatistic RouteHourStatisticsStoreTestsRow(string routeKey, DateTime hour) => new()
    {
        CitySlug = "atlanta",
        RouteJoinKey = routeKey,
        HourStartUtc = hour,
        RouteShortName = routeKey,
        StaticRouteId = "shape-" + routeKey,
        Category = "bus",
        RouteCatalogFingerprint = new string('a', 64),
        DefinitionVersion = CityRouteHourStatistic.CurrentDefinitionVersion,
        CaptureRunId = Guid.Parse("ed6150e0-b01d-4c16-8cf1-5c06826b1b1a"),
        CollectionStatus = RouteHourCollectionStatus.Complete,
        IncompleteReasons = [],
        HealthyCadenceLimitSeconds = 30,
        ObservedCycleCount = 1,
        ValidActiveSampleCount = 1,
        ValidPublishCycleCount = 1,
        FirstCycleUtc = hour.AddMinutes(5),
        LastCycleUtc = hour.AddMinutes(5),
        MaxObservationGapSeconds = 10,
        StartBoundaryOk = true,
        EndBoundaryOk = true,
        DistinctActiveVehicleCount = 0,
    };
}
