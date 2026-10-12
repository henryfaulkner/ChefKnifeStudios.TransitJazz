#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;

public enum ChartLoadState
{
    Loading,
    Ready,
    Error
}

public enum NumericAxisLabelMode
{
    ElapsedHours,
    LocalClockOffset
}

/// <summary>Immutable, app-owned chart data. Values are raw per-period contributions.</summary>
public sealed class SeriesChartModel
{
    public SeriesChartModel(
        string xAxisTitle,
        string yAxisTitle,
        IReadOnlyList<string> labels,
        IReadOnlyList<double>? numericXValues,
        IReadOnlyList<ChartSeries> series,
        bool isStacked,
        IReadOnlyList<ChartPointEvidence?>? pointEvidence = null,
        NumericAxisLabelMode numericAxisLabelMode = NumericAxisLabelMode.ElapsedHours,
        bool enableSeriesVisibility = false)
    {
        XAxisTitle = xAxisTitle;
        YAxisTitle = yAxisTitle;
        Labels = Snapshot(labels);
        NumericXValues = numericXValues is null ? null : Snapshot(numericXValues);
        Series = Array.AsReadOnly(series.Select(item => new ChartSeries(item.Key, item.Label, item.Values, item.KnownValues, item.PointEvidence)).ToArray());
        IsStacked = isStacked;
        PointEvidence = pointEvidence is null ? null : SnapshotEvidence(pointEvidence);
        NumericAxisLabelMode = numericAxisLabelMode;
        EnableSeriesVisibility = enableSeriesVisibility;
    }

    public string XAxisTitle { get; }
    public string YAxisTitle { get; }
    public IReadOnlyList<string> Labels { get; }
    public IReadOnlyList<double>? NumericXValues { get; }
    public IReadOnlyList<ChartSeries> Series { get; }
    public bool IsStacked { get; }
    public IReadOnlyList<ChartPointEvidence?>? PointEvidence { get; }
    public NumericAxisLabelMode NumericAxisLabelMode { get; }
    public bool EnableSeriesVisibility { get; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(XAxisTitle) || string.IsNullOrWhiteSpace(YAxisTitle))
            throw new ArgumentException("Chart axes must have visible titles.");

        if (Labels.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Chart labels must be non-empty.");

        if (NumericXValues is not null)
        {
            if (NumericXValues.Count != Labels.Count)
                throw new ArgumentException("Numeric X values must align with chart labels.");

            double previous = double.NegativeInfinity;
            foreach (var value in NumericXValues)
            {
                if (!double.IsFinite(value) || value <= previous)
                    throw new ArgumentException("Numeric X values must be finite and strictly increasing.");
                previous = value;
            }
        }

        if (PointEvidence is not null && PointEvidence.Count != Labels.Count)
            throw new ArgumentException("Point evidence must align with chart labels.");

        if (NumericAxisLabelMode == NumericAxisLabelMode.LocalClockOffset
            && (NumericXValues is null || Series.Any(item => item.PointEvidence is null)))
            throw new ArgumentException("Local-clock numeric axes need aligned per-series point evidence.");
        if (EnableSeriesVisibility && (IsStacked || Series.Any(item => !item.Key.StartsWith("city:", StringComparison.Ordinal))))
            throw new ArgumentException("Legend visibility is available only for unstacked city series.");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Series)
        {
            if (string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.Label))
                throw new ArgumentException("Each chart series needs a stable key and visible label.");
            if (!keys.Add(item.Key))
                throw new ArgumentException("Chart series keys must be unique.");
            if (item.Values.Count != Labels.Count)
                throw new ArgumentException("Every chart series must align with the chart labels.");
            if (item.KnownValues is not null && item.KnownValues.Count != Labels.Count)
                throw new ArgumentException("Known chart values must align with the chart labels.");
            if (item.PointEvidence is not null && item.PointEvidence.Count != Labels.Count)
                throw new ArgumentException("Per-series point evidence must align with chart labels.");
            if (item.Values.Any(value => value is { } number && !double.IsFinite(number))
                || item.KnownValues?.Any(value => value is { } number && !double.IsFinite(number)) == true)
                throw new ArgumentException("Chart values must be finite numbers or unavailable.");
        }
    }

    static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.ToArray());
    }

    static IReadOnlyList<ChartPointEvidence?> SnapshotEvidence(IReadOnlyList<ChartPointEvidence?> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return Array.AsReadOnly(evidence.Select(item => item is null
            ? null
            : item with { TooltipLines = Array.AsReadOnly(item.TooltipLines.ToArray()) }).ToArray());
    }
}

/// <summary>Localized, exact point details used by chart tooltips and the data table.</summary>
public sealed record ChartPointEvidence(
    string LocalLabel,
    string IntervalStartUtc,
    string IntervalEndUtc,
    string UtcOffset,
    string CoverageState,
    double? CombinedPlottedSubtotal,
    double? HourlyKnownKilometers,
    double? KnownCumulativeKilometers,
    IReadOnlyList<string> TooltipLines,
    string? Reason = null);

/// <summary>
/// One stable series. <see cref="Values"/> is used unchanged by the canvas and table;
/// nullable known values preserve observed subtotals when a plotted point is a gap.
/// </summary>
public sealed class ChartSeries
{
    public ChartSeries(
        string key,
        string label,
        IReadOnlyList<double?> values,
        IReadOnlyList<double?>? knownValues = null,
        IReadOnlyList<ChartPointEvidence?>? pointEvidence = null)
    {
        Key = key;
        Label = label;
        Values = Array.AsReadOnly(values.ToArray());
        KnownValues = knownValues is null ? null : Array.AsReadOnly(knownValues.ToArray());
        PointEvidence = pointEvidence is null ? null : Array.AsReadOnly(pointEvidence.Select(item => item is null
            ? null
            : item with { TooltipLines = Array.AsReadOnly(item.TooltipLines.ToArray()) }).ToArray());
    }

    public string Key { get; }
    public string Label { get; }
    public IReadOnlyList<double?> Values { get; }
    public IReadOnlyList<double?>? KnownValues { get; }
    public IReadOnlyList<ChartPointEvidence?>? PointEvidence { get; }
    public IReadOnlyList<double?> CanvasValues => Values;
    public IReadOnlyList<double?> TableValues => Values;
}
