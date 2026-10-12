#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;

/// <summary>All category evidence for a pie, including rows that do not produce a slice.</summary>
public sealed class PieChartModel
{
    public PieChartModel(IReadOnlyList<PieChartRow> rows) => Rows = Array.AsReadOnly(rows.ToArray());

    public IReadOnlyList<PieChartRow> Rows { get; }

    public void Validate()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Rows)
        {
            if (string.IsNullOrWhiteSpace(row.Key) || string.IsNullOrWhiteSpace(row.Label)
                || string.IsNullOrWhiteSpace(row.CoverageReference))
                throw new ArgumentException("Every pie row needs a key, label, and coverage reference.");
            if (!keys.Add(row.Key))
                throw new ArgumentException("Pie category keys must be unique.");
            if (row.Kilometers is { } km && (!double.IsFinite(km) || km < 0))
                throw new ArgumentException("Pie kilometers must be finite and nonnegative.");
            if (row.Percent is { } percent && (!double.IsFinite(percent) || percent < 0 || percent > 100))
                throw new ArgumentException("Pie percentages must be finite and between zero and one hundred.");
        }
    }

    public IReadOnlyList<PieChartSlice> GetPositiveSlices() => Array.AsReadOnly(
        Rows.Where(row => row.Kilometers is > 0)
            .Select(row => new PieChartSlice(row.Key, row.Label, row.Kilometers!.Value))
            .ToArray());

    public IReadOnlyList<double?> GetTableKilometers() => Array.AsReadOnly(Rows.Select(row => row.Kilometers).ToArray());
}

public sealed record PieChartRow(string Key, string Label, double? Kilometers, double? Percent, string CoverageReference);
public sealed record PieChartSlice(string Key, string Label, double Kilometers);
