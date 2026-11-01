using System.Xml.Linq;
using System.Text.RegularExpressions;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components.Stats;
using ChefKnifeStudios.TransitJazz.Client.Shared.Resources;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsResourcesTests
{
    [Fact]
    public void EveryStatsResourceHasEnglishText()
    {
        var root = FindRepositoryRoot(AppContext.BaseDirectory);
        var resources = Path.Combine(root, "src", "Client", "ChefKnifeStudios.TransitJazz.Client.Shared", "Resources");
        var english = XDocument.Load(Path.Combine(resources, "RouteFilterResources.resx"));
        var englishValues = english.Root!.Elements("data")
            .Where(IsFeatureResource)
            .ToDictionary(element => element.Attribute("name")!.Value, element => element.Element("value")?.Value);

        Assert.NotEmpty(englishValues);
        Assert.All(englishValues, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
    }

    [Fact]
    public void SnapshotFreshnessAndDatedHistoryHaveExplicitResourceCopy()
    {
        var root = FindRepositoryRoot(AppContext.BaseDirectory);
        var path = Path.Combine(root, "src", "Client", "ChefKnifeStudios.TransitJazz.Client.Shared", "Resources", "RouteFilterResources.resx");
        var values = XDocument.Load(path).Root!.Elements("data")
            .Where(IsFeatureResource)
            .ToDictionary(element => element.Attribute("name")!.Value, element => element.Element("value")?.Value);

        Assert.Equal("Snapshot updated {0}", values["StatsSnapshotUpdatedAt"]);
        Assert.Equal("No newer snapshot was received after the refresh boundary.", values["StatsOverdueNotice"]);
        Assert.Equal("Historical daily evidence from {0}", values["StatsHistoricalSnapshotDate"]);
        Assert.Equal("Today's values are not available in this older snapshot.", values["StatsHistoricalSnapshotExplanation"]);
        Assert.Equal("Distance on {0}", values["StatsDistanceOnDate"]);
        Assert.Equal("Vehicles reported on {0}", values["StatsReportedVehiclesOnDate"]);
    }

    static bool IsFeatureResource(XElement element) =>
        element.Attribute("name")?.Value.StartsWith("Stats", StringComparison.Ordinal) == true
        || element.Attribute("name")?.Value is "InfoOverlayTitle"
            or "RouteSelectCategory" or "RouteClearCategory" or "RouteFilterAccessibleName";

    [Fact]
    public void EveryChartResourceReferenceAndFiniteStateMappingExistsInEnglish()
    {
        var root = FindRepositoryRoot(AppContext.BaseDirectory);
        var componentDirectory = Path.Combine(root, "src", "Client", "ChefKnifeStudios.TransitJazz.Client.Shared", "Components");
        var englishPath = Path.Combine(root, "src", "Client", "ChefKnifeStudios.TransitJazz.Client.Shared", "Resources", "RouteFilterResources.resx");
        var english = ResourceKeys(englishPath);
        var sourceFiles = Directory.EnumerateFiles(componentDirectory, "*.*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".razor" or ".cs");
        var staticReferences = sourceFiles
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), "(?:Loc\\[|L\\[|T\\.Get\\(|localize\\()\\s*\\\"(Stats[^\\\"]+)\\\"")
                .Select(match => match.Groups[1].Value));
        var finiteMappings = new[]
        {
            "complete", "partial", "pending", "unavailable", "zero"
        }.Select(ChartLocalization.CoverageKey).Concat(new[]
        {
            "unknown_route", "route_not_retained", "missing_cohort", "zero_denominator", "no_active_vehicles", "future", "pending", "unrecognized"
        }.Select(ChartLocalization.ReasonKey)).Concat(
            StatsOverlayErrorResources.ProblemCodes.Select(StatsOverlayErrorResources.ResourceKey));

        foreach (var key in staticReferences.Concat(finiteMappings).Distinct(StringComparer.Ordinal))
        {
            Assert.Contains(key, english);
        }
    }

    [Fact]
    public void CompiledChartResourcesAndNumberFormattingStayEnglishUnderAnotherCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization();
            using var provider = services.BuildServiceProvider();
            IStringLocalizer<RouteFilterResources> localizer = provider.GetRequiredService<IStringLocalizer<RouteFilterResources>>();
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es");
            var text = new StatsChartText(localizer);

            Assert.Equal("View data table", text.Get("StatsChartTable"));
            Assert.Equal("kilometers", text.Get("StatsDistanceUnit"));
            Assert.Equal("Partial coverage", text.Get("StatsCoveragePartial"));
            Assert.Equal("0.001", text.Format(0.001, "0.###"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    static HashSet<string> ResourceKeys(string path) => XDocument.Load(path).Root!.Elements("data")
        .Select(element => element.Attribute("name")!.Value)
        .ToHashSet(StringComparer.Ordinal);

    static string FindRepositoryRoot(string path)
    {
        var current = new DirectoryInfo(path);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "specs", "058-stats-overlay", "tasks.md"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate TransitJazz repository root from the test output directory.");
    }
}
