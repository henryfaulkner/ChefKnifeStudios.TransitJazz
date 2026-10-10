using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class RouteHourStatisticsLocalTimeTests
{
    [RouteHourPostgresFact]
    public async Task FallBackRepeatedLocalHourKeepsBothUtcRowsAndTheirDifferentOffsets()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        await database.Store.WriteAsync([Complete("route-a", new DateTime(2026, 11, 1, 5, 0, 0, DateTimeKind.Utc))]);
        await database.Store.WriteAsync([Complete("route-a", new DateTime(2026, 11, 1, 6, 0, 0, DateTimeKind.Utc))]);
        await using var context = database.CreateDbContext();
        await context.Database.OpenConnectionAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "contracts", "route-hour-insights.sql"));
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var jsonText = await ExecuteAsync(connection, sql, new DateTime(2026, 11, 1, 5, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 1, 7, 0, 0, DateTimeKind.Utc), "America/New_York");
        using var json = JsonDocument.Parse(jsonText);

        var hours = json.RootElement.GetProperty("hourly").EnumerateArray()
            .OrderBy(row => row.GetProperty("requested_hour_utc").GetDateTime()).ToArray();
        Assert.Equal(2, hours.Length);
        Assert.All(hours, row => Assert.Equal(1, row.GetProperty("local_hour_of_day").GetInt32()));
        Assert.Equal(-14_400, hours[0].GetProperty("utc_offset_seconds").GetInt32());
        Assert.Equal(-18_000, hours[1].GetProperty("utc_offset_seconds").GetInt32());
        var typical = Assert.Single(json.RootElement.GetProperty("typical_local_hours").EnumerateArray());
        Assert.Equal(1, typical.GetProperty("local_hour_of_day").GetInt32());
        Assert.Equal(2, typical.GetProperty("contributing_utc_hour_count").GetInt64());
    }

    [RouteHourPostgresFact]
    public async Task SpringForwardSkipsLocalHourWithoutSynthesizingAResult()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        await database.Store.WriteAsync([Complete("route-a", new DateTime(2026, 3, 8, 6, 0, 0, DateTimeKind.Utc))]);
        await database.Store.WriteAsync([Complete("route-a", new DateTime(2026, 3, 8, 7, 0, 0, DateTimeKind.Utc))]);
        await using var context = database.CreateDbContext();
        await context.Database.OpenConnectionAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "contracts", "route-hour-insights.sql"));
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var jsonText = await ExecuteAsync(connection, sql, new DateTime(2026, 3, 8, 6, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 8, 8, 0, 0, DateTimeKind.Utc), "America/New_York");
        using var json = JsonDocument.Parse(jsonText);

        var hours = json.RootElement.GetProperty("hourly").EnumerateArray()
            .OrderBy(row => row.GetProperty("requested_hour_utc").GetDateTime()).ToArray();
        Assert.Equal(new[] { 1, 3 }, hours.Select(row => row.GetProperty("local_hour_of_day").GetInt32()));
        Assert.Equal(-18_000, hours[0].GetProperty("utc_offset_seconds").GetInt32());
        Assert.Equal(-14_400, hours[1].GetProperty("utc_offset_seconds").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("typical_local_hours").GetArrayLength());
    }

    static async Task<string> ExecuteAsync(NpgsqlConnection connection, string sql, DateTime fromUtc, DateTime toUtc, string zone)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = "atlanta" });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = new[] { "route-a" } });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = fromUtc });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = toUtc });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = CityRouteHourStatistic.CurrentDefinitionVersion });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = zone });
        return (string)(await command.ExecuteScalarAsync())!;
    }

    static CityRouteHourStatistic Complete(string routeKey, DateTime hour) => new()
    {
        CitySlug = "atlanta",
        RouteJoinKey = routeKey,
        HourStartUtc = hour,
        RouteShortName = "A",
        StaticRouteId = "shape-a",
        Category = "bus",
        RouteCatalogFingerprint = new string('b', 64),
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
