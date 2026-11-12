using ChefKnifeStudios.TransitJazz.Server.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.ClientExperience;

/// <summary>Creates an isolated database only from an explicitly named disposable PostgreSQL target.</summary>
public sealed class ClientExperienceDatabaseFixture : IDbContextFactory<AppDbContext>, IAsyncDisposable
{
    public const string ConnectionEnvironmentVariable = "CLIENT_EXPERIENCE_DISPOSABLE_CONNECTION";
    readonly string _adminConnection;
    readonly string _databaseName;
    readonly DbContextOptions<AppDbContext> _options;

    ClientExperienceDatabaseFixture(string adminConnection, string databaseName, string testConnection)
    {
        _adminConnection = adminConnection;
        _databaseName = databaseName;
        _options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(testConnection).Options;
    }

    public static async Task<ClientExperienceDatabaseFixture?> TryCreateAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, Timeout = 5, CommandTimeout = 10 };
        if (string.IsNullOrWhiteSpace(admin.Database) || !admin.Database.Contains("disposable", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{ConnectionEnvironmentVariable} must target a database whose name includes 'disposable'.");

        var databaseName = "tj_client_experience_disposable_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        var testConnection = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = databaseName };
        var fixture = new ClientExperienceDatabaseFixture(admin.ConnectionString, databaseName, testConnection.ConnectionString);
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

public sealed class ClientExperiencePostgresFactAttribute : Xunit.FactAttribute
{
    public ClientExperiencePostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ClientExperienceDatabaseFixture.ConnectionEnvironmentVariable)))
            Skip = $"Set {ClientExperienceDatabaseFixture.ConnectionEnvironmentVariable} to an explicitly provisioned disposable PostgreSQL database.";
    }
}
