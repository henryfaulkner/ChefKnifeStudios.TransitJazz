#nullable enable

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;

/// <summary>Finite mappings from wire coverage/reason codes to localized resource names.</summary>
public static class ChartLocalization
{
    public static string CoverageKey(string state) => state switch
    {
        "complete" => "StatsCoverageComplete",
        "partial" => "StatsCoveragePartial",
        "pending" => "StatsCoveragePending",
        "unavailable" => "StatsCoverageUnavailable",
        "zero" => "StatsCoverageZero",
        _ => "StatsUnavailable"
    };

    public static string ReasonKey(string reason) => reason switch
    {
        "unknown_route" => "StatsReasonUnknownRoute",
        "route_not_retained" => "StatsReasonRouteNotRetained",
        "missing_cohort" => "StatsReasonMissingCohort",
        "zero_denominator" => "StatsReasonZeroDenominator",
        "no_active_vehicles" => "StatsReasonNoActiveVehicles",
        "future" => "StatsReasonFuture",
        "pending" => "StatsReasonPending",
        "partial_cohort" => "StatsReasonPartialCohort",
        "conflict" => "StatsReasonConflict",
        "unsupported_definition" => "StatsReasonUnsupportedDefinition",
        "unsupported_cadence" => "StatsReasonUnsupportedCadence",
        "inconsistent_cohort" => "StatsReasonInconsistentCohort",
        "history_disabled" => "StatsReasonHistoryDisabled",
        "not_retained" => "StatsReasonNotRetained",
        "boundary_excluded" => "StatsReasonBoundaryExcluded",
        "no_data" => "StatsReasonNoData",
        _ => "StatsUnavailable"
    };
}
