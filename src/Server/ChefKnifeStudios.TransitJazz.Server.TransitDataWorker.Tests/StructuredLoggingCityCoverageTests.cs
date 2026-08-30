using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

public sealed class StructuredLoggingCityCoverageTests
{
    [Fact]
    public void EveryConfiguredWorkerCityCanProduceAValidatedAnomalyEvent()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(AppSettingsPath()));
        var cities = document.RootElement.GetProperty("Cities").EnumerateArray()
            .Select(city => city.GetProperty("Name").GetString())
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();

        Assert.NotEmpty(cities);
        Assert.Equal(cities.Length, cities.Distinct(StringComparer.Ordinal).Count());
        foreach (var city in cities)
        {
            var logEvent = StructuredLogEvent.Create(StructuredLogEventName.CityCycleAnomaly,
                StructuredLogOutcome.Failed, StructuredLogEvent.NewEventId(), "coverage-cycle", city,
                StructuredLogReasonCode.NO_VEHICLES);
            Assert.Same(logEvent, logEvent.Validate());
        }
    }

    static string AppSettingsPath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "ChefKnifeStudios.TransitJazz.Server.TransitDataWorker", "appsettings.json"));
}
