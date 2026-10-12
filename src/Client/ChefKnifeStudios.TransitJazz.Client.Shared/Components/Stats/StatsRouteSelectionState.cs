#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ChefKnifeStudios.TransitJazz.Shared.Stats;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components.Stats;

/// <summary>Owns the ordinal, map-independent route set for one Stats city session.</summary>
public sealed class StatsRouteSelectionState
{
    readonly SortedSet<string> selected = new(StringComparer.Ordinal);

    public IReadOnlyList<string> SelectedKeys => selected.ToArray();
    public int Count => selected.Count;

    public bool Toggle(string routeJoinKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeJoinKey);
        if (selected.Remove(routeJoinKey)) return false;
        selected.Add(routeJoinKey);
        return true;
    }

    public void SelectAll(IEnumerable<StatsRouteOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        foreach (var option in options)
        {
            if (!string.IsNullOrWhiteSpace(option.RouteJoinKey))
                selected.Add(option.RouteJoinKey);
        }
    }

    public void SelectCategory(string categoryKey, IEnumerable<StatsRouteOption> options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryKey);
        ArgumentNullException.ThrowIfNull(options);
        foreach (var option in options)
            if (string.Equals(option.CategoryKey, categoryKey, StringComparison.Ordinal))
                selected.Add(option.RouteJoinKey);
    }

    public void ClearCategory(string categoryKey, IEnumerable<StatsRouteOption> options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryKey);
        ArgumentNullException.ThrowIfNull(options);
        foreach (var option in options)
            if (string.Equals(option.CategoryKey, categoryKey, StringComparison.Ordinal))
                selected.Remove(option.RouteJoinKey);
    }

    public void Clear() => selected.Clear();

    /// <summary>
    /// Keeps selected keys visible when a later unfiltered metadata union no longer contains
    /// them. It uses the exact ordinal key as fallback text and never silently clears selection.
    /// </summary>
    public IReadOnlyList<StatsRouteOption> MergeOptions(
        IEnumerable<StatsRouteOption> options,
        string citySlug,
        IReadOnlyList<StatsSelectedRouteCoverage>? selectedCoverage = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(citySlug);
        var byKey = new Dictionary<string, StatsRouteOption>(StringComparer.Ordinal);
        foreach (var option in options)
            if (string.Equals(option.CitySlug, citySlug, StringComparison.Ordinal))
                byKey.TryAdd(option.RouteJoinKey, option);

        foreach (var key in selected)
        {
            if (byKey.ContainsKey(key)) continue;
            var routeCoverage = selectedCoverage?.FirstOrDefault(coverage =>
                string.Equals(coverage.CitySlug, citySlug, StringComparison.Ordinal)
                && string.Equals(coverage.RouteJoinKey, key, StringComparison.Ordinal));
            var unavailableReason = routeCoverage?.Reasons.FirstOrDefault(
                reason => reason is "unknown_route" or "route_not_retained") ?? "route_not_retained";
            byKey.Add(key, new StatsRouteOption
            {
                CitySlug = citySlug,
                RouteJoinKey = key,
                Label = key,
                CategoryKey = "other",
                MetadataTimestampUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc),
                AvailabilityReason = unavailableReason
            });
        }

        return byKey.Values
            .OrderBy(option => option.CategoryKey, StringComparer.Ordinal)
            .ThenBy(option => option.Label, StringComparer.Ordinal)
            .ThenBy(option => option.RouteJoinKey, StringComparer.Ordinal)
            .ToArray();
    }
}
