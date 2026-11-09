#nullable enable
using System;
using System.Linq;
namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.ClientExperience;

public sealed class ClientExperienceOptions
{
    public const string SectionName = "ClientExperience";
    public bool Enabled { get; set; }
    public bool CaptureEnabled { get; set; }
    public bool SyntheticIntakeEnabled { get; set; }
    public string SyntheticIntakeToken { get; set; } = string.Empty;
    public bool MaintenanceEnabled { get; set; }
    public int MaximumRequestBytes { get; set; } = 65_536;
    public int MaximumEventsPerBatch { get; set; } = 20;
    public int PerSourceBurst { get; set; } = 10;
    public int PerSourcePerMinute { get; set; } = 30;
    public int GlobalPerSecond { get; set; } = 100;
    public int MaximumConcurrentWrites { get; set; } = 16;
    public int RequestTimeoutSeconds { get; set; } = 10;
    public int MaintenanceIntervalMinutes { get; set; } = 60;
    public int RawRetentionDays { get; set; } = 30;
    public int DailyRetentionMonths { get; set; } = 13;
    public int MaximumCleanupRowsPerRun { get; set; } = 50_000;
    public string[] AllowedCitySlugs { get; set; } = [];

    public void Validate(string? connectionString)
    {
        if (CaptureEnabled && !Enabled)
            throw new InvalidOperationException("ClientExperience:CaptureEnabled requires ClientExperience:Enabled.");
        if (SyntheticIntakeEnabled && !Enabled)
            throw new InvalidOperationException("ClientExperience:SyntheticIntakeEnabled requires ClientExperience:Enabled.");
        if (SyntheticIntakeEnabled && (SyntheticIntakeToken is null || SyntheticIntakeToken.Length is < 32 or > 256
            || SyntheticIntakeToken.Any(char.IsWhiteSpace)))
            throw new InvalidOperationException("ClientExperience:SyntheticIntakeToken must be 32-256 characters and contain no whitespace when synthetic intake is enabled.");
        if (MaintenanceIntervalMinutes is < 5 or > 1440 || RawRetentionDays is < 1 or > 365
            || DailyRetentionMonths is < 1 or > 60 || MaximumCleanupRowsPerRun is < 1000 or > 1_000_000)
            throw new InvalidOperationException("Client experience maintenance settings are outside their supported bounds.");
        if (MaximumRequestBytes is < 1024 or > 1_048_576)
            throw new InvalidOperationException("ClientExperience:MaximumRequestBytes must be between 1024 and 1048576.");
        if (MaximumEventsPerBatch is < 1 or > 20)
            throw new InvalidOperationException("ClientExperience:MaximumEventsPerBatch must be between 1 and 20.");
        if (PerSourceBurst < 1 || PerSourcePerMinute < PerSourceBurst || GlobalPerSecond < 1 || MaximumConcurrentWrites is < 1 or > 128)
            throw new InvalidOperationException("ClientExperience rate and concurrency limits must be positive and bounded.");
        if (RequestTimeoutSeconds is < 1 or > 30)
            throw new InvalidOperationException("ClientExperience:RequestTimeoutSeconds must be between 1 and 30.");
        if (AllowedCitySlugs is null || AllowedCitySlugs.Any(city => string.IsNullOrWhiteSpace(city) || city.Length > 64 || city.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))))
            throw new InvalidOperationException("ClientExperience:AllowedCitySlugs contains an invalid city slug.");
        if ((Enabled || MaintenanceEnabled) && string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Enabled client experience intake or maintenance requires ConnectionStrings:TransitJazzDB.");
    }
}
