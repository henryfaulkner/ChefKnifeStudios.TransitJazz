# Worker Dashboard Statistics Source Contract v1

**Status**: Reviewed implementation contract
**Dashboard**: `transitjazz-worker-overview`
**Version stored on rows**: `worker-dashboard-statistics-v1`

## Purpose

This contract freezes the relationship between the committed worker dashboard and the one-table historical model. The collector must query only the configured read-only Prometheus-compatible Grafana endpoint, use UTC, and persist no field that is absent from this contract.

Each source range request evaluates closed minute endpoints at a `60s` step. A row whose `stat_minute_utc` is `T` describes `[T, T+1m)` and is evaluated at `T+1m`. Requests are bounded and non-overlapping: after a chunk ending at an evaluation point, the next chunk begins one minute later because range-query endpoints are inclusive.

The current source exports approximately every ten seconds. The table intentionally stores a one-minute dashboard-parity sample rather than a sum of the six likely exports in that minute.

## Rules common to all queries

- Select only the configured canonical `transit_city` labels; reject an unexpected city label or source instance cardinality.
- The query client uses literal metric names shown below. It does not derive names from C# instrument names.
- The literal trailing window for counter and histogram calculations is `[1m]`. Grafana's `$__rate_interval` macro remains unchanged in the dashboard but is never sent by the collector.
- Gauge expressions use `last_over_time(...[1m])` so a stale implicit-lookback sample is not silently treated as a fresh minute value.
- A missing matrix value is null. It is never converted to zero.
- Any source warning, information message, parse error, timeout, incomplete bucket set, or unexpected label makes the affected city minute `Partial` or `NoData`.
- The source client never logs its endpoint, authorization header, query response body, or credentials.

## Mapping

`{city}` below is the bounded configured city selector. Direct gauge expressions are evaluated independently for each returned `transit_city` label.

| Persisted field | Literal source expression | Unit / rule |
|---|---|---|
| `last_cycled_unix_seconds` | `last_over_time(transitjazz_worker_city_last_cycled_seconds{transit_city=~"{cities}"}[1m])` | Unix seconds. Derive historical cycle age from minute close. |
| `last_worked_unix_seconds` | `last_over_time(transitjazz_worker_city_last_worked_seconds{transit_city=~"{cities}"}[1m])` | Unix seconds. Derive historical work age from minute close. |
| `cycle_rate_per_second` | `rate(transitjazz_worker_city_cycles_total{transit_city=~"{cities}"}[1m])` | Per second; counter resets are handled by `rate`. |
| `cycle_error_rate_per_second` | `rate(transitjazz_worker_city_cycle_errors_total{transit_city=~"{cities}"}[1m])` | Per second; a fetch failure is distinct from this error rate. |
| `cycle_duration_p95_seconds` | `histogram_quantile(0.95, sum by (le, transit_city) (rate(transitjazz_worker_city_cycle_duration_seconds_bucket{transit_city=~"{cities}"}[1m])))` | Estimated seconds; all expected buckets are required. |
| `healthy` | `last_over_time(transitjazz_worker_city_healthy_ratio{transit_city=~"{cities}"}[1m])` | Exact 0/1 only. |
| `input_fetch_ok` | `last_over_time(transitjazz_worker_city_input_fetch_ok_ratio{transit_city=~"{cities}"}[1m])` | Exact 0/1 only. |
| `input_records_valid` | `last_over_time(transitjazz_worker_city_input_records_valid{transit_city=~"{cities}"}[1m])` | Nonnegative count. |
| `has_input_records` | `last_over_time(transitjazz_worker_city_has_input_records_ratio{transit_city=~"{cities}"}[1m])` | Exact 0/1 only. |
| `input_lag_seconds` | `last_over_time(transitjazz_worker_city_input_lag_seconds{transit_city=~"{cities}"}[1m])` | Zero with unknown timestamp means unknown, not fresh. |
| `input_timestamp_known` | `last_over_time(transitjazz_worker_city_input_timestamp_known_ratio{transit_city=~"{cities}"}[1m])` | Exact 0/1 only. |
| `input_source_failures` | `last_over_time(transitjazz_worker_city_input_source_failures{transit_city=~"{cities}"}[1m])` | Nonnegative count. |
| `vehicles_processed` | `last_over_time(transitjazz_worker_city_vehicles_processed{transit_city=~"{cities}"}[1m])` | Latest sample; not a minute total. |
| `tones_emitted` | `last_over_time(transitjazz_worker_city_tones_emitted{transit_city=~"{cities}"}[1m])` | Latest sample; not a minute total. |
| `batch_wire_bytes` | `last_over_time(transitjazz_worker_city_batch_wire_bytes{transit_city=~"{cities}"}[1m])` | Latest bytes; source uses zero for no batch. |
| `crossings_suppressed_first_seen` | `last_over_time(transitjazz_worker_city_crossings_suppressed_first_seen{transit_city=~"{cities}"}[1m])` | Latest count. |
| `crossings_suppressed_delta_leq_zero` | `last_over_time(transitjazz_worker_city_crossings_suppressed_delta_leq0{transit_city=~"{cities}"}[1m])` | Latest count. |
| `crossings_suppressed_teleport` | `last_over_time(transitjazz_worker_city_crossings_suppressed_teleport{transit_city=~"{cities}"}[1m])` | Latest count. |
| `crossings_suppressed_transfer` | `last_over_time(transitjazz_worker_city_crossings_suppressed_transfer{transit_city=~"{cities}"}[1m])` | Latest count. |
| `vehicle_state_cache` | `last_over_time(transitjazz_worker_city_vehicle_state_cache{transit_city=~"{cities}"}[1m])` | Latest count. |
| `crossing_baseline_cache` | `last_over_time(transitjazz_worker_city_crossing_baseline_cache{transit_city=~"{cities}"}[1m])` | Latest count. |
| `route_index` | `last_over_time(transitjazz_worker_city_route_index{transit_city=~"{cities}"}[1m])` | Latest count. |
| `route_trigger_point_cache` | `last_over_time(transitjazz_worker_city_route_trigger_point_cache{transit_city=~"{cities}"}[1m])` | Latest count. |

## Backfill and collection behavior

1. Preflight a 15-minute range before writing: verify authenticated read access, source retention, expected city labels, every expression, and response warnings.
2. Run a dry-run backfill from the earliest returned minute to the latest complete minute, bounded in six-hour UTC chunks with low concurrency.
3. Produce a safe report containing contract version, requested and returned ranges, row counts by outcome, gaps, warnings, and representative query-result comparisons. The report has no endpoint, authorization value, raw response, or secret.
4. Re-run the same chunks with writes enabled only after dry-run review. The database key is city and minute; an existing compatible row is unchanged, a compatible incomplete row can be filled, and an incompatible value is a discrepancy that is not silently overwritten.
5. Recurring collection begins only after the initial proof. Each minute it reads the newest closed minute after a two-minute ingestion grace plus the prior five closed minutes as an idempotent overlap.

## Reconciliation contract

For each city and persisted source field, choose at least three deterministic closed minutes when available: a normal nonzero minute, a zero/empty minute, and an edge or incident minute. Run the frozen expression at the same endpoint and compare it to the row:

- integers, booleans, and Unix timestamps match exactly;
- rates and p95 values use a documented small floating-point tolerance;
- missing source data remains a null field and `Partial`/`NoData` status;
- a failed comparison creates a safe discrepancy report and blocks acceptance of the historical import.

The catalogue is versioned in source control and is the only provenance record for v1. Rates and p95 values use a maximum absolute comparison tolerance of `0.000001`; integer counts, booleans, and Unix timestamps require exact equality. No database provenance table is introduced.
