using Npgsql;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

/// <summary>Opt-in connection helper for an explicitly provisioned feature-056 disposable database.</summary>
public sealed class CategoryStatisticsDatabaseFixture : IAsyncDisposable
{
    public const string ConnectionEnvironmentVariable = "CITY_CATEGORY_INSIGHTS_DISPOSABLE_CONNECTION";
    readonly NpgsqlConnection _connection;

    CategoryStatisticsDatabaseFixture(string connectionString) => _connection = new NpgsqlConnection(connectionString);

    public static CategoryStatisticsDatabaseFixture? TryCreate()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString)) return null;

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.Database)
            || !builder.Database.Contains("disposable", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{ConnectionEnvironmentVariable} must target a database whose name includes 'disposable'.");
        return new CategoryStatisticsDatabaseFixture(connectionString);
    }

    public async Task OpenAsync(CancellationToken cancellationToken = default) => await _connection.OpenAsync(cancellationToken);

    public async Task<string> ReadContractSqlAsync(string fileName)
    {
        if (Path.GetFileName(fileName) != fileName || !fileName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A contract SQL file name is required.", nameof(fileName));
        var path = Path.Combine(AppContext.BaseDirectory, "contracts", fileName);
        return await File.ReadAllTextAsync(path);
    }

    public NpgsqlConnection Connection => _connection;

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
