namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;

/// <summary>
/// Frozen snake_case column-name constants for all three parquet datasets.
/// These names are the durable contract consumed by the telemetry-query-tool and the
/// feature-012 allow-list grammar — changing them is a breaking change.
/// </summary>
public static class TelemetryColumns
{
    // ── Shared ──────────────────────────────────────────────────────────────
    public const string CycleId = "cycle_id";
    public const string ObservationUtc = "observation_utc";
    public const string VehicleId = "vehicle_id";

    // ── Snap dataset ────────────────────────────────────────────────────────
    public const string RouteId = "route_id";
    public const string SnapOutcome = "snap_outcome";
    public const string RawLat = "raw_lat";
    public const string RawLon = "raw_lon";
    public const string SnappedLat = "snapped_lat";
    public const string SnappedLon = "snapped_lon";
    public const string SnapDistanceKm = "snap_distance_km";
    public const string SnapIndex = "snap_index";
    public const string RoutePointCount = "route_point_count";
    public const string SpeedMps = "speed_mps";
    public const string BearingDeg = "bearing_deg";
    public const string IsStale = "is_stale";

    // ── Lerp dataset ────────────────────────────────────────────────────────
    public const string PriorRouteId = "prior_route_id";
    public const string PriorSnappedLat = "prior_snapped_lat";
    public const string PriorSnappedLon = "prior_snapped_lon";
    public const string PriorObservationUtc = "prior_observation_utc";
    public const string PriorSpeedMps = "prior_speed_mps";
    public const string PriorBearingDeg = "prior_bearing_deg";
    public const string PosDeltaKm = "pos_delta_km";
    public const string SpeedDelta = "speed_delta";
    public const string BearingDelta = "bearing_delta";
    public const string TimeDeltaSec = "time_delta_sec";

    // ── Cycle dataset ────────────────────────────────────────────────────────
    public const string CycleStartUtc = "cycle_start_utc";
    public const string CycleEndUtc = "cycle_end_utc";
    public const string CycleExecutionSeconds = "cycle_execution_seconds";
    public const string BusesProcessed = "buses_processed";
    public const string BusesMoved = "buses_moved";
    public const string BusesUnchanged = "buses_unchanged";
    public const string BusesStationary = "buses_stationary";
    public const string BusesStale = "buses_stale";
    public const string BusesSkippedNoRouteId = "buses_skipped_no_route_id";
    public const string BusesSkippedUnknownRoute = "buses_skipped_unknown_route";
    public const string FeedHeaderTs = "feed_header_ts";
    public const string DuplicateFeed = "duplicate_feed";
    public const string LastUpdateCacheSize = "last_update_cache_size";
    public const string VehicleStateCacheSize = "vehicle_state_cache_size";
    public const string SidecarBufferOccupancy = "sidecar_buffer_occupancy";
    public const string SidecarDroppedRecords = "sidecar_dropped_records";
    public const string SidecarPersistFailures = "sidecar_persist_failures";
}
