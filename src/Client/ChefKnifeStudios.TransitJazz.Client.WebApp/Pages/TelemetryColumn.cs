using ChefKnifeStudios.TransitJazz.Shared.TelemetryData;

namespace ChefKnifeStudios.TransitJazz.Client.WebApp.Pages;

/// <summary>One sortable-or-not column header for a <see cref="TelemetryTable"/>.
/// SortColumn is null for columns with no server-side sort mapping (e.g. a CSV blob).</summary>
public sealed record TelemetryColumn(string Label, TelemetrySortColumn? SortColumn);
