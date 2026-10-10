using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

public readonly record struct RouteHourGeometryPoint(double Latitude, double Longitude);

/// <summary>One grouped canonical route before catalog validation and fingerprinting.</summary>
public sealed record RouteHourCatalogInput(
    string RouteJoinKey,
    string Category,
    string? RouteShortName,
    ImmutableArray<string> StaticRouteIds,
    ImmutableArray<RouteHourGeometryPoint> Geometry,
    ImmutableArray<string> Aliases);

public sealed record RouteHourCatalogEntry(
    string RouteJoinKey,
    string Category,
    string? RouteShortName,
    string? StaticRouteId,
    ImmutableArray<string> ContributingStaticRouteIds,
    ImmutableArray<RouteHourGeometryPoint> Geometry,
    string Fingerprint)
{
    public static RouteHourCatalogEntry Create(RouteHourCatalogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateKey(input.RouteJoinKey);
        if (string.IsNullOrWhiteSpace(input.Category) || input.Category.Length > 64 || input.Category != input.Category.ToLowerInvariant())
            throw new ArgumentException("Route category must be normalized and at most 64 characters.", nameof(input));
        if (input.Geometry.IsDefaultOrEmpty || input.Geometry.Any(point => !double.IsFinite(point.Latitude) || !double.IsFinite(point.Longitude)))
            throw new ArgumentException("Route geometry must contain finite ordered points.", nameof(input));

        var ids = input.StaticRouteIds.IsDefault ? ImmutableArray<string>.Empty : input.StaticRouteIds;
        if (ids.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Static route IDs cannot be blank.", nameof(input));
        ids = ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        var fingerprint = RouteHourCatalog.ComputeFingerprint(input.RouteJoinKey, input.Category, ids, input.Geometry);
        return new(input.RouteJoinKey, input.Category, input.RouteShortName,
            ids.Length == 1 ? ids[0] : null, ids, input.Geometry, fingerprint);
    }

    internal static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) > 512)
            throw new ArgumentException("Canonical route key must be nonblank and at most 512 UTF-8 bytes.", nameof(key));
    }
}

/// <summary>Immutable ordinal catalog and alias map built from the same grouped geometry as live routing.</summary>
public sealed class RouteHourCatalog
{
    public const string EncodingVersion = "route-catalog-v1";
    readonly ImmutableDictionary<string, RouteHourCatalogEntry> _entries;
    readonly ImmutableDictionary<string, string> _aliases;

    RouteHourCatalog(
        ImmutableDictionary<string, RouteHourCatalogEntry> entries,
        ImmutableDictionary<string, string> aliases,
        ImmutableArray<string> ambiguousAliases,
        bool geometryIdentityAvailable,
        string contentFingerprint)
    {
        _entries = entries;
        _aliases = aliases;
        AmbiguousAliases = ambiguousAliases;
        GeometryIdentityAvailable = geometryIdentityAvailable;
        ContentFingerprint = contentFingerprint;
    }

    public IReadOnlyDictionary<string, RouteHourCatalogEntry> Entries => _entries;
    public ImmutableArray<string> AmbiguousAliases { get; }
    public bool GeometryIdentityAvailable { get; }
    public string ContentFingerprint { get; }

    public bool TryGet(string canonicalKey, out RouteHourCatalogEntry entry) => _entries.TryGetValue(canonicalKey, out entry!);

    public bool TryResolve(string routeIdOrKey, out RouteHourCatalogEntry entry)
    {
        entry = null!;
        if (_entries.TryGetValue(routeIdOrKey, out entry!)) return true;
        return _aliases.TryGetValue(routeIdOrKey, out var canonical)
            && _entries.TryGetValue(canonical, out entry!);
    }

    public static RouteHourCatalog Create(IEnumerable<RouteHourCatalogInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var entries = ImmutableDictionary.CreateBuilder<string, RouteHourCatalogEntry>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguousAliases = ImmutableArray.CreateBuilder<string>();

        foreach (var input in inputs)
        {
            var entry = RouteHourCatalogEntry.Create(input);
            if (!entries.TryAdd(entry.RouteJoinKey, entry))
                throw new ArgumentException("Catalog inputs must already be grouped by canonical route key.", nameof(inputs));
            AddAlias(entry.RouteJoinKey, entry.RouteJoinKey);
            foreach (var staticId in entry.ContributingStaticRouteIds) AddAlias(staticId, entry.RouteJoinKey);
            if (!input.Aliases.IsDefault)
                foreach (var alias in input.Aliases.Where(value => !string.IsNullOrWhiteSpace(value))) AddAlias(alias, entry.RouteJoinKey);
        }

        var immutableEntries = entries.ToImmutable();
        var caseGroups = immutableEntries.Keys.GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).SelectMany(group => group).Order(StringComparer.Ordinal).ToArray();
        var content = ComputeContentFingerprint(immutableEntries.Values);
        return new RouteHourCatalog(immutableEntries, aliases.ToImmutableDictionary(StringComparer.Ordinal),
            ambiguousAliases.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray(),
            caseGroups.Length == 0, content);

        void AddAlias(string alias, string canonical)
        {
            if (ambiguousAliases.Contains(alias, StringComparer.Ordinal)) return;
            if (!aliases.TryAdd(alias, canonical) && !string.Equals(aliases[alias], canonical, StringComparison.Ordinal))
            {
                aliases.Remove(alias);
                ambiguousAliases.Add(alias);
            }
        }
    }

    public static string ComputeFingerprint(string routeJoinKey, string category,
        IEnumerable<string> contributingStaticRouteIds, IEnumerable<RouteHourGeometryPoint> geometry)
    {
        RouteHourCatalogEntry.ValidateKey(routeJoinKey);
        ArgumentNullException.ThrowIfNull(category);
        var ids = contributingStaticRouteIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var points = geometry.ToArray();
        using var stream = new MemoryStream();
        WriteString(stream, EncodingVersion);
        WriteString(stream, routeJoinKey);
        WriteString(stream, category);
        WriteCount(stream, ids.Length);
        foreach (var id in ids) WriteString(stream, id);
        WriteCount(stream, points.Length);
        foreach (var point in points)
        {
            if (!double.IsFinite(point.Latitude) || !double.IsFinite(point.Longitude))
                throw new ArgumentException("Route geometry coordinates must be finite.", nameof(geometry));
            WriteString(stream, point.Latitude.ToString("R", CultureInfo.InvariantCulture));
            WriteString(stream, point.Longitude.ToString("R", CultureInfo.InvariantCulture));
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    static string ComputeContentFingerprint(IEnumerable<RouteHourCatalogEntry> entries)
    {
        using var stream = new MemoryStream();
        WriteString(stream, "route-hour-cohort-v1");
        var ordered = entries.OrderBy(entry => entry.RouteJoinKey, StringComparer.Ordinal).ToArray();
        WriteCount(stream, ordered.Length);
        foreach (var entry in ordered)
        {
            WriteString(stream, entry.RouteJoinKey);
            WriteString(stream, entry.Fingerprint);
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteCount(stream, bytes.Length);
        stream.Write(bytes);
    }

    static void WriteCount(Stream stream, int value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        Span<byte> bytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);
        stream.Write(bytes);
    }
}
