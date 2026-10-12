#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;

/// <summary>Immutable bubble evidence; rows with no valid point remain available to the table.</summary>
public sealed class BubbleChartModel
{
    public const double MaximumMarkerRadiusPixels = 24;
    public const double MinimumMarkerRadiusPixels = 3;

    public BubbleChartModel(IReadOnlyList<BubbleChartRow> rows) => Rows = Array.AsReadOnly(rows.Select(row => row with
    {
        TooltipLines = row.TooltipLines is null ? null : Array.AsReadOnly(row.TooltipLines.ToArray())
    }).ToArray());

    public IReadOnlyList<BubbleChartRow> Rows { get; }

    public void Validate()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Rows)
        {
            if (string.IsNullOrWhiteSpace(row.Key) || string.IsNullOrWhiteSpace(row.Label)
                || string.IsNullOrWhiteSpace(row.CoverageState))
                throw new ArgumentException("Every bubble row needs a key, label, and coverage state.");
            if (!keys.Add(row.Key))
                throw new ArgumentException("Bubble keys must be unique.");
            ValidateNonnegativeFinite(row.X, "Bubble X");
            ValidateNonnegativeFinite(row.Y, "Bubble Y");
            ValidateNonnegativeFinite(row.MeanActiveVehicles, "Bubble area measure");
            ValidateNonnegativeFinite(row.DistanceKilometers, "Bubble distance");
            ValidateNonnegativeFinite(row.RouteVehicleHours, "Bubble route hours");
            ValidateNonnegativeFinite(row.ActiveClockHours, "Bubble active clock hours");
            ValidateNonnegativeFinite(row.ExpectedCoverageHours, "Bubble expected coverage");
            ValidateNonnegativeFinite(row.AvailableCoverageHours, "Bubble available coverage");
            if (row.ActivitySampleNumerator is < 0 || row.ActivitySampleDenominator is < 0)
                throw new ArgumentException("Bubble sample counts must be nonnegative.");
            if (row.AvailableCoverageHours is { } available && row.ExpectedCoverageHours is { } expected && available > expected)
                throw new ArgumentException("Bubble available coverage cannot exceed expected coverage.");
        }
    }

    public IReadOnlyList<BubbleChartRow> GetTableRows() => Rows;

    /// <summary>Returns valid points largest first; exact coordinates and ratios are unchanged.</summary>
    public IReadOnlyList<PlottedBubblePoint> GetPlottedPoints()
    {
        var candidates = Rows
            .Select((row, index) => (Row: row, Index: index))
            .Where(item => item.Row.X.HasValue && item.Row.Y.HasValue && item.Row.MeanActiveVehicles is > 0)
            .ToArray();
        var maximum = candidates.Length == 0 ? 0 : candidates.Max(item => item.Row.MeanActiveVehicles!.Value);

        return Array.AsReadOnly(candidates
            .OrderByDescending(item => item.Row.MeanActiveVehicles)
            .ThenBy(item => item.Index)
            .Select(item => new PlottedBubblePoint(
                item.Row.Key,
                item.Row.Label,
                item.Row.X!.Value,
                item.Row.Y!.Value,
                item.Row.MeanActiveVehicles!.Value,
                Math.Max(MinimumMarkerRadiusPixels,
                    MaximumMarkerRadiusPixels * Math.Sqrt(item.Row.MeanActiveVehicles.Value / maximum)),
                item.Row.ColorKey ?? item.Row.Key,
                item.Row.TooltipLines ?? Array.Empty<string>(),
                item.Row.DistanceKilometers,
                item.Row.RouteVehicleHours,
                item.Row.ActiveClockHours,
                item.Row.ActivitySampleNumerator,
                item.Row.ActivitySampleDenominator,
                item.Row.ExpectedCoverageHours,
                item.Row.AvailableCoverageHours,
                item.Row.Color))
            .ToArray());
    }

    static void ValidateNonnegativeFinite(double? value, string name)
    {
        if (value is { } number && (!double.IsFinite(number) || number < 0))
            throw new ArgumentException($"{name} must be finite and nonnegative when present.");
    }
}

public sealed record BubbleChartRow(
    string Key,
    string Label,
    double? X,
    double? Y,
    double? MeanActiveVehicles,
    string CoverageState,
    string? OmissionReason,
    string? ColorKey = null,
    IReadOnlyList<string>? TooltipLines = null,
    double? DistanceKilometers = null,
    double? RouteVehicleHours = null,
    double? ActiveClockHours = null,
    long? ActivitySampleNumerator = null,
    long? ActivitySampleDenominator = null,
    double? ExpectedCoverageHours = null,
    double? AvailableCoverageHours = null,
    string? Color = null);

public sealed record PlottedBubblePoint(
    string Key,
    string Label,
    double X,
    double Y,
    double MeanActiveVehicles,
    double RadiusPixels,
    string ColorKey,
    IReadOnlyList<string> TooltipLines,
    double? DistanceKilometers,
    double? RouteVehicleHours,
    double? ActiveClockHours,
    long? ActivitySampleNumerator,
    long? ActivitySampleDenominator,
    double? ExpectedCoverageHours,
    double? AvailableCoverageHours,
    string? Color = null);
