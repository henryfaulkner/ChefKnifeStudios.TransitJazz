using ChefKnifeStudios.TransitJazz.Server.Data;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

/// <summary>Opt-in connection helper for an explicitly provisioned route-history disposable database.</summary>
public sealed class RouteHourStatisticsDatabaseFixture : IDbContextFactory<AppDbContext>, IAsyncDisposable
{
    public const string ConnectionEnvironmentVariable = "ROUTE_HOUR_HISTORY_DISPOSABLE_CONNECTION";
    readonly string _adminConnection;
    readonly string _databaseName;
    readonly DbContextOptions<AppDbContext> _options;

    RouteHourStatisticsDatabaseFixture(string adminConnection, string databaseName, string testConnection)
    {
        _adminConnection = adminConnection;
        _databaseName = databaseName;
        _options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(testConnection).Options;
        Store = new CityRouteHourStatisticsStore(this);
    }

    public CityRouteHourStatisticsStore Store { get; }

    public static async Task<RouteHourStatisticsDatabaseFixture> CreateAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Set {ConnectionEnvironmentVariable} to an explicitly provisioned disposable PostgreSQL database.");

        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, Timeout = 5, CommandTimeout = 10 };
        if (string.IsNullOrWhiteSpace(admin.Database)
            || !admin.Database.Contains("disposable", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{ConnectionEnvironmentVariable} must target a database whose name includes 'disposable'.");
        var databaseName = "transitjazz_057_disposable_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        var testConnection = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = databaseName };
        var fixture = new RouteHourStatisticsDatabaseFixture(admin.ConnectionString, databaseName, testConnection.ConnectionString);
        try
        {
            await using var context = fixture.CreateDbContext();
            await context.Database.MigrateAsync();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public AppDbContext CreateDbContext() => new(_options);
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{_databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class RouteHourPostgresFactAttribute : FactAttribute
{
    public RouteHourPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RouteHourStatisticsDatabaseFixture.ConnectionEnvironmentVariable)))
            Skip = $"Set {RouteHourStatisticsDatabaseFixture.ConnectionEnvironmentVariable} to an explicitly provisioned disposable PostgreSQL database.";
    }
}
