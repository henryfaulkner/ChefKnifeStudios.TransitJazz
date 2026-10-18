using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

public sealed record ReportedVehicleSourceDefinition
{
    public required string SourceKey { get; init; }
    public required string IdentityNamespace { get; init; }
    public required string IdentityKind { get; init; }
    public string IdentityRuleVersion { get; init; } = "descriptor-id-v1";
    public int ExpectedCadenceSeconds { get; init; } = 30;
    public int GapLimitSeconds { get; init; } = 60;
    public bool AllowFallbackIdentity { get; init; }

    public void Validate()
    {
        ValidateSourceKey(SourceKey);
        ValidateKey(IdentityNamespace, 64, nameof(IdentityNamespace));
        ValidateKey(IdentityKind, 64, nameof(IdentityKind));
        ValidateKey(IdentityRuleVersion, 64, nameof(IdentityRuleVersion));
        if (ExpectedCadenceSeconds is < 1 or > 300 || GapLimitSeconds <= ExpectedCadenceSeconds || GapLimitSeconds > 300)
            throw new ArgumentOutOfRangeException(nameof(GapLimitSeconds), "Gap limit must exceed cadence and be at most 300 seconds.");
    }

    public static void ValidateSourceKey(string? value) => ValidateKey(value, 64, nameof(SourceKey));

    static void ValidateKey(string? value, int maxBytes, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOf('\0') >= 0
            || Encoding.UTF8.GetByteCount(value) > maxBytes)
            throw new ArgumentException($"{name} must be nonblank, NUL-free, and at most {maxBytes} UTF-8 bytes.", name);
    }
}

public sealed record ReportedVehicleSourceCatalogEntry(string CitySlug, ImmutableArray<ReportedVehicleSourceDefinition> Sources);

/// <summary>Explicit source membership and stable fingerprints; endpoint URLs are never included.</summary>
public sealed class ReportedVehicleSourceCatalog
{
    readonly ImmutableDictionary<string, ReportedVehicleSourceCatalogEntry> _cities;

    public ReportedVehicleSourceCatalog(IEnumerable<ReportedVehicleSourceCatalogEntry> cities)
    {
        ArgumentNullException.ThrowIfNull(cities);
        var builder = ImmutableDictionary.CreateBuilder<string, ReportedVehicleSourceCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var city in cities)
        {
            if (string.IsNullOrWhiteSpace(city.CitySlug) || Encoding.UTF8.GetByteCount(city.CitySlug) > 64)
                throw new ArgumentException("Catalog city slug must be nonblank and at most 64 UTF-8 bytes.");
            if (city.Sources.IsDefaultOrEmpty || city.Sources.Length > 32)
                throw new ArgumentException("A source catalog must contain 1-32 sources per city.");
            foreach (var source in city.Sources) source.Validate();
            if (city.Sources.Select(x => x.SourceKey).Distinct(StringComparer.Ordinal).Count() != city.Sources.Length)
                throw new ArgumentException("Source keys must be unique within a city.");
            if (!builder.TryAdd(city.CitySlug, city with { Sources = city.Sources.OrderBy(x => x.SourceKey, StringComparer.Ordinal).ToImmutableArray() }))
                throw new ArgumentException("City source catalogs must be unique.");
        }
        _cities = builder.ToImmutable();
    }

    public IReadOnlyCollection<ReportedVehicleSourceCatalogEntry> Cities => _cities.Values.ToArray();
    public bool TryGet(string citySlug, out ReportedVehicleSourceCatalogEntry catalog) => _cities.TryGetValue(citySlug, out catalog!);

    public string SourceSetFingerprint(string citySlug)
    {
        var catalog = Get(citySlug);
        var material = string.Join('\n', catalog.Sources.Select(x => string.Join('|',
            x.SourceKey, x.IdentityNamespace, x.IdentityKind, x.IdentityRuleVersion, x.AllowFallbackIdentity ? "fallback" : "strict")));
        return Sha256(material);
    }

    public string CadencePolicyFingerprint(string citySlug)
    {
        var catalog = Get(citySlug);
        return Sha256(string.Join('\n', catalog.Sources.Select(x => string.Join('|',
            x.SourceKey, x.ExpectedCadenceSeconds, x.GapLimitSeconds))));
    }

    public ReportedVehicleSourceDefinition GetSource(string citySlug, string sourceKey) =>
        Get(citySlug).Sources.SingleOrDefault(x => string.Equals(x.SourceKey, sourceKey, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException("Reported-vehicle source policy is not configured.");

    ReportedVehicleSourceCatalogEntry Get(string citySlug) =>
        _cities.TryGetValue(citySlug, out var catalog) ? catalog : throw new KeyNotFoundException("Reported-vehicle city policy is not configured.");

    static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
