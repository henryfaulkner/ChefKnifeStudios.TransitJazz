using System.Text.RegularExpressions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

// 052-city-slug-migration contract C3: no agency slug literal may survive in CityFab.razor's
// location.hash='...' handlers — they must all resolve through CityNames.* by construction.
public class CitySlugLiteralGuardTests
{
    static readonly Regex LegacyHashLiteral = new(
        "location\\.hash='(marta|wmata|mbta|nymta|ttc|septa|rtd)'", RegexOptions.IgnoreCase);

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException("Could not locate repo root (no ancestor containing 'src') from " + AppContext.BaseDirectory);

        return dir.FullName;
    }

    [Fact]
    public void no_agency_slug_literal_remains_in_client_source()
    {
        var path = Path.Combine(RepoRoot(),
            "src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/FABs/CityFab.razor");
        var source = File.ReadAllText(path);

        var matches = LegacyHashLiteral.Matches(source);
        Assert.True(matches.Count == 0,
            $"Found {matches.Count} legacy agency-slug location.hash literal(s) in CityFab.razor: " +
            string.Join(", ", matches.Select(m => m.Value)));
    }
}
