#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;
using ChefKnifeStudios.TransitJazz.Client.Shared.ViewModels;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using ChefKnifeStudios.TransitJazz.Shared.Stats;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components;

/// <summary>Immutable presentation data for the shared route filter list.</summary>
public sealed record RouteFilterListItem(
    string RouteKey,
    string Label,
    string AccessibleLabel,
    string CategoryKey,
    string? Color,
    bool IsSelected,
    bool IsMuted,
    bool IsHovered,
    string? BadgeText = null,
    string? AvailabilityText = null);

public sealed record RouteFilterListGroup(
    string Key,
    string Label,
    IReadOnlyList<RouteFilterListItem> Items,
    bool HasSelection,
    string? SelectActionLabel = null,
    string? ClearActionLabel = null);

public sealed record RouteFilterListModel(
    IReadOnlyList<RouteFilterListGroup> Groups,
    int SelectedCount,
    int ItemCount,
    string? EmptyMessage = null);

/// <summary>Projects map and retained Stats data into a read-only list model.</summary>
public static class RouteFilterListProjection
{
    public static RouteFilterListModel FromMap(
        IEnumerable<RouteItem> routeItems,
        IEnumerable<string> categoryOrder,
        IEnumerable<string> selectedRouteKeys,
        IEnumerable<string> hoveredRouteKeys,
        string? hoveredCategory,
        Func<string, string> categoryLabel,
        Func<string, string> selectCategoryLabel,
        Func<string, string> clearCategoryLabel)
    {
        ArgumentNullException.ThrowIfNull(routeItems);
        ArgumentNullException.ThrowIfNull(categoryOrder);
        ArgumentNullException.ThrowIfNull(selectedRouteKeys);
        ArgumentNullException.ThrowIfNull(hoveredRouteKeys);
        ArgumentNullException.ThrowIfNull(categoryLabel);
        ArgumentNullException.ThrowIfNull(selectCategoryLabel);
        ArgumentNullException.ThrowIfNull(clearCategoryLabel);

        var source = routeItems.ToArray();
        var selected = new HashSet<string>(selectedRouteKeys, StringComparer.Ordinal);
        var hovered = new HashSet<string>(hoveredRouteKeys, StringComparer.Ordinal);
        var mutedUnselected = selected.Count > 0 || hovered.Count > 0;
        var items = source.Select(item => new RouteFilterListItem(
            item.RouteJoinKey,
            item.Label,
            item.Label,
            item.Category,
            item.Color,
            selected.Contains(item.RouteJoinKey),
            mutedUnselected && !selected.Contains(item.RouteJoinKey) && !hovered.Contains(item.RouteJoinKey),
            hovered.Contains(item.RouteJoinKey) || string.Equals(hoveredCategory, item.Category, StringComparison.Ordinal)));

        return Build(items, categoryOrder, categoryLabel, (key, label) => selectCategoryLabel(label),
            (key, label) => clearCategoryLabel(label));
    }

    public static RouteFilterListModel FromStats(
        IEnumerable<StatsRouteOption> options,
        IEnumerable<string> selectedRouteKeys,
        Func<string, string> categoryLabel,
        Func<StatsRouteOption, string?> availabilityText,
        Func<StatsRouteOption, string?> badgeText,
        Func<string, string, string> selectCategoryLabel,
        Func<string, string, string> clearCategoryLabel,
        string? emptyMessage = null,
        IReadOnlyDictionary<string, RouteShapeFeature>? routeShapes = null,
        bool isDark = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(selectedRouteKeys);
        ArgumentNullException.ThrowIfNull(categoryLabel);
        ArgumentNullException.ThrowIfNull(availabilityText);
        ArgumentNullException.ThrowIfNull(badgeText);
        ArgumentNullException.ThrowIfNull(selectCategoryLabel);
        ArgumentNullException.ThrowIfNull(clearCategoryLabel);

        var selected = new HashSet<string>(selectedRouteKeys, StringComparer.Ordinal);
        var items = options.Select(option =>
        {
            var availableText = availabilityText(option);
            var badge = badgeText(option);
            var isSelected = selected.Contains(option.RouteJoinKey);
            var color = ChartPresentation.GetRouteColor(option.CitySlug, option.RouteJoinKey, option.CategoryKey, routeShapes, isDark);
            return new RouteFilterListItem(
                option.RouteJoinKey,
                option.Label,
                string.Join(". ", new[] { option.Label, badge, availableText }.Where(text => text is not null)),
                option.CategoryKey,
                color,
                isSelected,
                selected.Count > 0 && !isSelected,
                false,
                badge,
                availableText);
        });

        return Build(items, [], categoryLabel, selectCategoryLabel, clearCategoryLabel, emptyMessage);
    }

    static RouteFilterListModel Build(
        IEnumerable<RouteFilterListItem> source,
        IEnumerable<string> requestedCategoryOrder,
        Func<string, string> categoryLabel,
        Func<string, string, string>? selectActionLabel,
        Func<string, string, string>? clearActionLabel,
        string? emptyMessage = null)
    {
        var items = source.ToArray();
        var byCategory = items.GroupBy(item => item.CategoryKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in requestedCategoryOrder)
            if (byCategory.ContainsKey(key) && seen.Add(key)) order.Add(key);
        foreach (var key in byCategory.Keys.OrderBy(key => key, StringComparer.Ordinal))
            if (seen.Add(key)) order.Add(key);

        var groups = order.Select(key =>
        {
            var categoryItems = byCategory[key];
            var label = categoryLabel(key);
            return new RouteFilterListGroup(
                key,
                label,
                categoryItems,
                categoryItems.Any(item => item.IsSelected),
                selectActionLabel?.Invoke(key, label),
                clearActionLabel?.Invoke(key, label));
        }).ToArray();

        return new RouteFilterListModel(groups, items.Count(item => item.IsSelected), items.Length,
            items.Length == 0 ? emptyMessage : null);
    }
}
