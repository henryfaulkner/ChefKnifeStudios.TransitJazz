# Contract: Parquet Dataset Schemas

These schemas are the **durable contract** between the logging sidecar (writer) and the `telemetry-query-tool` / feature-012 MCP bridge (reader). Column names and types are frozen here; changing them is a breaking change for the query tool's allow-list grammar.

Conventions:
- Column names: `snake_case`, matching `[a-z_][a-z0-9_]*` (DuckDB-friendly, compatible with feature 012's identifier grammar).
- Timestamps: parquet `TIMESTAMP` (UTC). `feed_header_ts` is epoch seconds as `INT64`.
- Nullable columns marked `?`.
- Compression: Snappy. One dataset per file (no mixed schemas).

---

## Dataset `snap`

```
cycle_id            STRING       NOT NULL
observation_utc     TIMESTAMP    NOT NULL
vehicle_id          STRING       NOT NULL
route_id            STRING       NOT NULL
snap_outcome        STRING       NOT NULL   -- FirstObservation|Moved|Unchanged|Stationary|Stale
raw_lat             DOUBLE       NOT NULL
raw_lon             DOUBLE       NOT NULL
snapped_lat         DOUBLE       NOT NULL
snapped_lon         DOUBLE       NOT NULL
snap_distance_km    DOUBLE       NOT NULL
snap_index          INT32        NOT NULL
route_point_count   INT32        NOT NULL
speed_mps           DOUBLE?      
bearing_deg         DOUBLE?      
is_stale            BOOLEAN      NOT NULL
```

## Dataset `lerp`

```
cycle_id              STRING      NOT NULL
observation_utc       TIMESTAMP   NOT NULL
vehicle_id            STRING      NOT NULL
prior_route_id        STRING      NOT NULL
prior_snapped_lat     DOUBLE      NOT NULL
prior_snapped_lon     DOUBLE      NOT NULL
prior_observation_utc TIMESTAMP   NOT NULL
prior_speed_mps       DOUBLE?     
prior_bearing_deg     DOUBLE?     
pos_delta_km          DOUBLE      NOT NULL
speed_delta           DOUBLE?     
bearing_delta         DOUBLE?     
time_delta_sec        DOUBLE      NOT NULL
```

## Dataset `cycle`

```
cycle_id                      STRING     NOT NULL
cycle_start_utc               TIMESTAMP  NOT NULL
cycle_end_utc                 TIMESTAMP  NOT NULL
cycle_execution_seconds       DOUBLE     NOT NULL
buses_processed               INT32      NOT NULL
buses_moved                   INT32      NOT NULL
buses_unchanged               INT32      NOT NULL
buses_stationary              INT32      NOT NULL
buses_stale                   INT32      NOT NULL
buses_skipped_no_route_id     INT32      NOT NULL
buses_skipped_unknown_route   INT32      NOT NULL
feed_header_ts                INT64?     
duplicate_feed                BOOLEAN    NOT NULL
last_update_cache_size        INT32      NOT NULL
vehicle_state_cache_size      INT32      NOT NULL
sidecar_buffer_occupancy      INT32      NOT NULL
sidecar_dropped_records       INT64      NOT NULL
sidecar_persist_failures      INT64      NOT NULL
```

---

## Accept / reject (downstream read examples)

Query tool reads one day's shard by globbing the partition:

```sql
-- ACCEPT: per-cycle health for a day
SELECT cycle_id, buses_stale, buses_skipped_unknown_route
FROM read_parquet('azure://telemetry/cycle/dt=2026-06-04/*.parquet');

-- ACCEPT: where was a vehicle snapped in a cycle
SELECT snapped_lat, snapped_lon, snap_outcome
FROM read_parquet('azure://telemetry/snap/dt=2026-06-04/*.parquet')
WHERE vehicle_id = '1234' AND cycle_id = '...';
```

A writer change that renames/removes any column above, mixes datasets in one file, or changes a type **breaks** these reads and the feature-012 allow-list (`allowedColumns`) — treat as a breaking contract change requiring a coordinated update to feature 012.
