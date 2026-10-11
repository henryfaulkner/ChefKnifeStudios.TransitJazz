using System.Collections.Immutable;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

/// <summary>Resource and cadence limits for route-hour capture. Collection mode and city scope belong to the host.</summary>
public sealed class RouteHourHistoryOptions
{
    public const string SectionName = "RouteHours";

    public int MaxObservationGapSeconds { get; set; } = 30;
    public Dictionary<string, int> CityMaxObservationGapSeconds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int QueueCapacity { get; set; } = 16;
    public int MaxRoutesPerCityHour { get; set; } = 4096;
    public int MaxTrackedVehiclesPerCity { get; set; } = 25_000;
    public int MaxVehicleRouteMembershipsPerCityHour { get; set; } = 50_000;
    public int MaxRowsPerCommand { get; set; } = 128;
    public int CommandTimeoutSeconds { get; set; } = 5;
    public int WriteAttemptTimeoutSeconds { get; set; } = 30;
    public int MaxWriteAttempts { get; set; } = 3;
    public int ShutdownDrainSeconds { get; set; } = 15;

    public void Validate(int cycleIntervalSeconds, IEnumerable<string>? selectedCities = null)
    {
        if (cycleIntervalSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(cycleIntervalSeconds));
        ValidateGap(MaxObservationGapSeconds, cycleIntervalSeconds, nameof(MaxObservationGapSeconds));
        if (CityMaxObservationGapSeconds is null) throw new ArgumentNullException(nameof(CityMaxObservationGapSeconds));

        var selected = selectedCities?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (city, seconds) in CityMaxObservationGapSeconds)
        {
            if (string.IsNullOrWhiteSpace(city) || selected is not null && !selected.Contains(city))
                throw new ArgumentException("Cadence overrides must target a selected, nonblank city.", nameof(CityMaxObservationGapSeconds));
            ValidateGap(seconds, cycleIntervalSeconds, nameof(CityMaxObservationGapSeconds));
        }

        if (QueueCapacity is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(QueueCapacity));
        if (MaxRoutesPerCityHour is < 1 or > 16_384) throw new ArgumentOutOfRangeException(nameof(MaxRoutesPerCityHour));
        if (MaxTrackedVehiclesPerCity is < 1 or > 100_000) throw new ArgumentOutOfRangeException(nameof(MaxTrackedVehiclesPerCity));
        if (MaxVehicleRouteMembershipsPerCityHour is < 1 or > 250_000) throw new ArgumentOutOfRangeException(nameof(MaxVehicleRouteMembershipsPerCityHour));
        if (MaxRowsPerCommand is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(MaxRowsPerCommand));
        if (CommandTimeoutSeconds is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(CommandTimeoutSeconds));
        if (WriteAttemptTimeoutSeconds < CommandTimeoutSeconds || WriteAttemptTimeoutSeconds > 120)
            throw new ArgumentOutOfRangeException(nameof(WriteAttemptTimeoutSeconds));
        if (MaxWriteAttempts is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(MaxWriteAttempts));
        if (ShutdownDrainSeconds is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(ShutdownDrainSeconds));
    }

    public int GapLimitFor(string city) => CityMaxObservationGapSeconds.TryGetValue(city, out var seconds)
        ? seconds
        : MaxObservationGapSeconds;

    static void ValidateGap(int seconds, int cycleIntervalSeconds, string name)
    {
        if (seconds <= cycleIntervalSeconds || seconds > 60)
            throw new ArgumentOutOfRangeException(name, "Observation gap must exceed the worker cycle interval and be at most 60 seconds.");
    }
}

public enum RouteHourCaptureMode { Disabled, DryRun, Persistence }

/// <summary>Immutable projection of the existing host history controls and resolved city selection.</summary>
public sealed record RouteHourCaptureRuntimeOptions(
    RouteHourCaptureMode Mode,
    ImmutableHashSet<string> SelectedCities,
    RouteHourHistoryOptions Limits)
{
    public bool IsEnabledFor(string city) => Mode != RouteHourCaptureMode.Disabled && SelectedCities.Contains(city);
}
