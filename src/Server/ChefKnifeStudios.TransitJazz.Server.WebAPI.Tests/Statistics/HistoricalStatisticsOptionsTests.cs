using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class HistoricalStatisticsOptionsTests
{
    [Fact]
    public void DisabledDefaultsValidateWithoutEndpointOrAuthorization()
    {
        var options = ValidOptions();
        options.Validate();
    }

    [Theory]
    [InlineData("http://metrics.example/api/v1/query_range")]
    [InlineData("not-a-uri")]
    public void EnabledOptionsRequireHttpsEndpoint(string endpoint)
    {
        var options = ValidOptions();
        options.Enabled = true;
        options.SourceEndpoint = endpoint;
        options.ReaderAuthorization = "Basic reader";

        Assert.Throws<OptionsValidationException>(() => options.Validate());
    }

    [Fact]
    public void EnabledOptionsRejectUnsafeIntervalsAndWrongContract()
    {
        var options = ValidOptions();
        options.Enabled = true;
        options.SourceEndpoint = "https://metrics.example/api/v1/query_range";
        options.ReaderAuthorization = "Basic reader";
        options.CollectionIntervalMinutes = 2;
        options.SourceDefinitionVersion = "worker-dashboard-statistics-v0";

        var exception = Assert.Throws<OptionsValidationException>(() => options.Validate());
        Assert.Contains(exception.Failures, failure => failure.Contains("one minute", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(exception.Failures, failure => failure.Contains("v1", StringComparison.Ordinal));
        Assert.DoesNotContain(options.ReaderAuthorization, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidCityConfigurationIsRejected()
    {
        var options = ValidOptions();
        options.Cities.Add("atlanta");

        Assert.Throws<OptionsValidationException>(() => options.Validate());
    }

    [Fact]
    public void AzureUtcBackfillValuesBindAndValidate()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HistoricalStatistics:BackfillStartUtc"] = "2026-08-01T00:00:00Z",
                ["HistoricalStatistics:BackfillEndUtc"] = "2026-10-01T00:00:00Z",
            })
            .Build();
        var options = ValidOptions();
        options.Enabled = true;
        options.InitialBackfill = true;
        options.SourceEndpoint = "https://metrics.example/api/v1/query_range";
        options.ReaderAuthorization = "Basic reader";

        configuration.GetSection(HistoricalStatisticsOptions.SectionName).Bind(options);

        options.Validate();
        Assert.Equal(TimeSpan.Zero, options.BackfillStartUtc!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, options.BackfillEndUtc!.Value.Offset);
        Assert.Equal(DateTimeKind.Utc, options.BackfillStartUtc.Value.UtcDateTime.Kind);
        Assert.Equal(DateTimeKind.Utc, options.BackfillEndUtc.Value.UtcDateTime.Kind);
    }

    [Fact]
    public void BackfillRejectsNonUtcOffset()
    {
        var options = ValidOptions();
        options.BackfillStartUtc = new DateTimeOffset(2026, 8, 1, 1, 0, 0, TimeSpan.FromHours(1));
        options.BackfillEndUtc = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        var exception = Assert.Throws<OptionsValidationException>(() => options.Validate());

        Assert.Contains(exception.Failures, failure => failure.Contains("BackfillStartUtc must be UTC.", StringComparison.Ordinal));
    }

    [Fact]
    public void HostRegistersTheCollectorWithoutAddingAStatisticsEndpoint()
    {
        var root = RepoRoot();
        var program = File.ReadAllText(Path.Combine(root, "src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs"));
        var settings = File.ReadAllText(Path.Combine(root, "src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json"));

        Assert.Contains("RegisterDataServices", program, StringComparison.Ordinal);
        Assert.Contains("AddHttpClient<IHistoricalStatisticsSource, GrafanaPrometheusStatisticsSource>", program, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<HistoricalStatisticsCollector>", program, StringComparison.Ordinal);
        Assert.Contains("\"Enabled\": false", settings, StringComparison.Ordinal);
        Assert.Contains("\"DryRun\": true", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("MapHistoricalStatistics", program, StringComparison.Ordinal);
    }

    static HistoricalStatisticsOptions ValidOptions() => new()
    {
        Cities = ["atlanta", "washington-dc", "boston", "new-york-city", "toronto", "philadelphia", "denver"],
    };

    static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
