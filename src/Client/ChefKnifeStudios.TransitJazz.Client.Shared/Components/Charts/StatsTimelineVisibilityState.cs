#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;

/// <summary>Presentation-only city visibility; it never changes the API selection or chart models.</summary>
public sealed class StatsTimelineVisibilityState
{
    readonly Dictionary<string, bool> visibility = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, bool> Values => visibility;

    public bool IsVisible(string key) => !visibility.TryGetValue(key, out var value) || value;

    public void Reconcile(IEnumerable<string> keys, bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (!enabled)
        {
            Reset();
            return;
        }

        var active = keys.ToHashSet(StringComparer.Ordinal);
        foreach (var removed in visibility.Keys.Where(key => !active.Contains(key)).ToArray())
            visibility.Remove(removed);
        foreach (var added in active.Where(key => !visibility.ContainsKey(key)))
            visibility.Add(added, true);
    }

    public void SetVisibility(string key, bool visible)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        visibility[key] = visible;
    }

    public void SetAll(IEnumerable<string> keys, bool visible)
    {
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var key in keys) SetVisibility(key, visible);
    }

    public void Reset() => visibility.Clear();
}
