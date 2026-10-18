using ChefKnifeStudios.TransitJazz.Server.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

/// <summary>Opt-in disposable database fixture; never targets the configured database directly.</summary>
public sealed class ReportedVehicleDatabaseFixture : IDbContextFactory<AppDbContext>, IAsyncDisposable
{
    public const string ConnectionEnvironmentVariable = "REPORTED_VEHICLE_HISTORY_DISPOSABLE_CONNECTION";
    readonly string _adminConnection;
    readonly string _databaseName;
    readonly DbContextOptions<AppDbContext> _options;
    bool _disposed;

    ReportedVehicleDatabaseFixture(string adminConnection, string databaseName, string testConnection)
    {
        _adminConnection = adminConnection;
        _databaseName = databaseName;
        _options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(testConnection).Options;
    }

    public static async Task<ReportedVehicleDatabaseFixture> CreateAsync(
        CancellationToken cancellationToken = default, string? targetMigration = null)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Set {ConnectionEnvironmentVariable} to an explicitly provisioned disposable PostgreSQL database.");

        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, Timeout = 5, CommandTimeout = 10 };
        if (string.IsNullOrWhiteSpace(admin.Database)
            || !admin.Database.Contains("disposable", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{ConnectionEnvironmentVariable} must target a database whose name includes 'disposable'.");

        var databaseName = "transitjazz_059_disposable_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var testConnection = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = databaseName };
        var fixture = new ReportedVehicleDatabaseFixture(admin.ConnectionString, databaseName, testConnection.ConnectionString);
        try
        {
            await using var context = fixture.CreateDbContext();
            await context.Database.MigrateAsync(targetMigration, cancellationToken);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public AppDbContext CreateDbContext() => new(_options);
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class ReportedVehiclePostgresFactAttribute : FactAttribute
{
    public ReportedVehiclePostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ReportedVehicleDatabaseFixture.ConnectionEnvironmentVariable)))
            Skip = $"Set {ReportedVehicleDatabaseFixture.ConnectionEnvironmentVariable} to an explicitly provisioned disposable PostgreSQL database.";
    }
}
