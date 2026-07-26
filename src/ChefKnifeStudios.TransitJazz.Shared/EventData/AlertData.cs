using System.Collections.Generic;

namespace ChefKnifeStudios.TransitJazz.Shared.EventData;

public sealed record AlertData(
    string? HeaderText,
    string? DescriptionText,
    string? Url,
    AlertCause Cause,
    AlertEffect Effect,
    AlertSeverity Severity,
    long? ActiveFrom,
    long? ActiveUntil,
    IReadOnlyList<string>? AffectedRouteIds,
    IReadOnlyList<string>? AffectedStopIds
);

public enum AlertCause
{
    UnknownCause = 1,
    OtherCause = 2,
    TechnicalProblem = 3,
    Strike = 4,
    Demonstration = 5,
    Accident = 6,
    Holiday = 7,
    Weather = 8,
    Maintenance = 9,
    Construction = 10,
    PoliceActivity = 11,
    MedicalEmergency = 12
}

public enum AlertEffect
{
    NoService = 1,
    ReducedService = 2,
    SignificantDelays = 3,
    Detour = 4,
    AdditionalService = 5,
    ModifiedService = 6,
    OtherEffect = 7,
    UnknownEffect = 8,
    StopMoved = 9,
    NoEffect = 10,
    AccessibilityIssue = 11
}

public enum AlertSeverity
{
    UnknownSeverity = 1,
    Info = 2,
    Warning = 3,
    Severe = 4
}
