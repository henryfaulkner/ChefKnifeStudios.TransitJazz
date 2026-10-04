using Microsoft.Extensions.Configuration;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

public sealed class CityCategoryInsightsOptions
{
    public const string SectionName = "CityCategoryInsights";

    public bool Enabled { get; set; }
    public List<string> DisabledCities { get; set; } = [];
    public int MaxObservationGapSeconds { get; set; } = 30;
    public Dictionary<string, int> CityMaxObservationGapSeconds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int QueueCapacity { get; set; } = 256;
    public int MaxBatchRows { get; set; } = 128;
    public int CommandTimeoutSeconds { get; set; } = 5;
    public int MaxWriteAttempts { get; set; } = 3;
    public int ShutdownDrainSeconds { get; set; } = 15;

    public static CityCategoryInsightsOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var options = section.Get<CityCategoryInsightsOptions>() ?? new CityCategoryInsightsOptions();
        // Single underscores are literal keys, so bind Disabled_0 city exclusions explicitly.
        const string disabledPrefix = "Disabled_";
        foreach (var setting in section.GetChildren().Where(x => x.Key.StartsWith(disabledPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            if (!int.TryParse(setting.Key[disabledPrefix.Length..], out var index) || index < 0)
                throw new ArgumentException("City exclusions must use Disabled_ followed by a nonnegative index.", nameof(configuration));
            options.DisabledCities.Add(setting.Value ?? string.Empty);
        }
        return options;
    }

    public bool IsEnabledFor(string city) => Enabled && !DisabledCities.Contains(city, StringComparer.OrdinalIgnoreCase);

    public void Validate(int cycleIntervalSeconds)
    {
        if (cycleIntervalSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(cycleIntervalSeconds));
        if (DisabledCities.Any(string.IsNullOrWhiteSpace) || DisabledCities.Distinct(StringComparer.OrdinalIgnoreCase).Count() != DisabledCities.Count)
            throw new ArgumentException("DisabledCities must contain distinct nonblank city names.", nameof(DisabledCities));
        ValidateGap(MaxObservationGapSeconds, cycleIntervalSeconds, nameof(MaxObservationGapSeconds), Enabled);
        foreach (var (city, value) in CityMaxObservationGapSeconds)
        {
            if (string.IsNullOrWhiteSpace(city))
                throw new ArgumentException("Cadence overrides must target a nonblank city name.", nameof(CityMaxObservationGapSeconds));
            ValidateGap(value, cycleIntervalSeconds, nameof(CityMaxObservationGapSeconds), IsEnabledFor(city));
        }
        if (QueueCapacity is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(QueueCapacity));
        if (MaxBatchRows is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(MaxBatchRows));
        if (CommandTimeoutSeconds is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(CommandTimeoutSeconds));
        if (MaxWriteAttempts is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(MaxWriteAttempts));
        if (ShutdownDrainSeconds is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(ShutdownDrainSeconds));
    }

    public int GapLimitFor(string city) => CityMaxObservationGapSeconds.TryGetValue(city, out var seconds) ? seconds : MaxObservationGapSeconds;

    static void ValidateGap(int seconds, int cycleIntervalSeconds, string name, bool requireGreaterThanCycle)
    {
        if (seconds is < 1 or > 60 || (requireGreaterThanCycle && seconds <= cycleIntervalSeconds))
            throw new ArgumentOutOfRangeException(name, "Observation gap must be greater than the cycle interval and at most 60 seconds.");
    }

    public static void ValidateIanaTimeZoneId(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)
            || timeZoneId.Contains('\\')
            || timeZoneId.Contains(' ')
            || !timeZoneId.Contains('/')
            || timeZoneId.StartsWith('/')
            || timeZoneId.EndsWith('/')
            || timeZoneId.Split('/').Any(segment => segment.Length == 0 || segment == "." || segment == ".."))
            throw new ArgumentException("Time zone must be a named IANA identifier.", nameof(timeZoneId));

        _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }
}
