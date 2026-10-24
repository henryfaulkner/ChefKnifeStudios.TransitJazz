using System;
using System.Collections.Generic;
using System.Linq;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

public sealed class StatsOverlayOptions
{
    public const string SectionName = "StatsOverlay";
    static readonly Dictionary<string, string> DisplayNameKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["atlanta"] = "StatsCityAtlanta",
        ["washington-dc"] = "StatsCityWashingtonDc",
        ["boston"] = "StatsCityBoston",
        ["new-york-city"] = "StatsCityNewYorkCity",
        ["toronto"] = "StatsCityToronto",
        ["philadelphia"] = "StatsCityPhiladelphia",
        ["denver"] = "StatsCityDenver",
        ["seattle"] = "StatsCitySeattle"
    };

    public bool Enabled { get; set; }
    public bool ReportedVehiclesEnabled { get; set; }
    public int IngestionGraceMinutes { get; set; } = 2;
    public int MaximumRequestBytes { get; set; } = 4 * 1024 * 1024;
    public int MaximumResponseBytes { get; set; } = 8 * 1024 * 1024;
    public int MaximumRouteKeyBytes { get; set; } = 512;
    public int MaximumSelectedRoutes { get; set; } = 4096;
    public int MaximumCities { get; set; } = 32;
    public int MaximumCategories { get; set; } = 32;
    public int MaximumCacheEntries { get; set; } = 33;
    public long MaximumCacheBytes { get; set; } = 64L * 1024 * 1024;
    public int CacheSeconds { get; set; } = 60;
    public int MaximumConcurrentReads { get; set; } = 4;
    public int ReadTimeoutSeconds { get; set; } = 5;
    public List<StatsOverlayCityOptions> Cities { get; set; } = [];

    /// <summary>Copies the public city registrations and resolved route-hour cadence into the bounded read policy.</summary>
    public void PopulateConfiguredCityPolicy(
        IEnumerable<CityConfig> configuredCities,
        RouteHourHistoryOptions routeHours,
        bool requireLocalizedDisplayNames = false)
    {
        ArgumentNullException.ThrowIfNull(configuredCities);
        ArgumentNullException.ThrowIfNull(routeHours);
        var resolved = new List<StatsOverlayCityOptions>();
        foreach (var city in configuredCities.OrderBy(value => value.Name, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(city.Name))
                throw new ArgumentException("Configured StatsOverlay cities require canonical names.", nameof(configuredCities));
            if (!DisplayNameKeys.TryGetValue(city.Name, out var displayNameKey))
            {
                if (requireLocalizedDisplayNames)
                    throw new ArgumentException($"StatsOverlay requires a localized display-name key for configured city {city.Name}.", nameof(configuredCities));
                displayNameKey = "StatsCityUnknown";
            }
            resolved.Add(new StatsOverlayCityOptions
            {
                CitySlug = city.Name,
                DisplayNameKey = displayNameKey,
                TimeZoneId = city.TimeZoneId,
                HealthyCadenceLimitSeconds = routeHours.GapLimitFor(city.Name)
            });
        }
        Cities = resolved;
    }

    public void Validate(bool requireCities = false)
    {
        if (IngestionGraceMinutes is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(IngestionGraceMinutes));
        if (MaximumRequestBytes is < 1024 or > 4 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(MaximumRequestBytes));
        if (MaximumResponseBytes is < 1024 or > 8 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(MaximumResponseBytes));
        if (MaximumRouteKeyBytes is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(MaximumRouteKeyBytes));
        if (MaximumSelectedRoutes is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(MaximumSelectedRoutes));
        if (MaximumCities is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(MaximumCities));
        if (MaximumCategories is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(MaximumCategories));
        if (MaximumCacheEntries is < 1 or > 33) throw new ArgumentOutOfRangeException(nameof(MaximumCacheEntries));
        if (MaximumCacheBytes is < 1024 * 1024 or > 64L * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(MaximumCacheBytes));
        if (CacheSeconds is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(CacheSeconds));
        if (MaximumConcurrentReads is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(MaximumConcurrentReads));
        if (ReadTimeoutSeconds is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(ReadTimeoutSeconds));
        if (Cities is null || Cities.Count > MaximumCities || requireCities && Cities.Count == 0)
            throw new ArgumentException("StatsOverlay must name a bounded configured public city set.", nameof(Cities));

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var city in Cities)
        {
            if (string.IsNullOrWhiteSpace(city.CitySlug) || city.CitySlug.Length > 64 || !identities.Add(city.CitySlug))
                throw new ArgumentException("StatsOverlay city slugs must be unique canonical identifiers.", nameof(Cities));
            if (!requireCities) continue;
            if (string.IsNullOrWhiteSpace(city.TimeZoneId))
                throw new ArgumentException($"StatsOverlay city {city.CitySlug} requires an IANA time zone.", nameof(Cities));
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId);
            }
            catch (TimeZoneNotFoundException exception)
            {
                throw new ArgumentException($"StatsOverlay city {city.CitySlug} has an unknown time zone.", nameof(Cities), exception);
            }
            catch (InvalidTimeZoneException exception)
            {
                throw new ArgumentException($"StatsOverlay city {city.CitySlug} has an invalid time zone.", nameof(Cities), exception);
            }
            if (city.HealthyCadenceLimitSeconds is < 1 or > 60)
                throw new ArgumentOutOfRangeException(nameof(Cities), "City route-hour cadence must be between one and 60 seconds.");
        }
    }
}

public sealed class StatsOverlayCityOptions
{
    public string CitySlug { get; set; } = string.Empty;
    public string DisplayNameKey { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public int HealthyCadenceLimitSeconds { get; set; } = 30;
}
