# Metrics Contract

## Publication rules

- Internal instrument names use `transitjazz.worker.*`; units are declared at creation and Prometheus-facing names are snake_case.
- OTel counter names omit `_total`; the Prometheus translation supplies it. A development scrape locks final names.
- Every applicable gauge is written each cycle. Unknown lag is an explicit `0` plus `input_timestamp_known=0`.
- City metrics carry only `transit.city=<configured ITransitCity.Name>`; worker metrics carry no city attribute.

## Worker-wide metrics

| Internal name | Instrument | Unit | Meaning |
|---|---|---|---|
| `transitjazz.worker.last_cycled` | gauge | seconds | UTC Unix time of last full tick, including idle/failed. |
| `transitjazz.worker.last_worked` | gauge | seconds | UTC Unix time of last work-producing tick only. |
| `transitjazz.worker.cycles` | counter | cycles | Completed full ticks. |
| `transitjazz.worker.cycle_interval` | gauge | seconds | Configured worker interval. |
| `transitjazz.worker.cycle_errors` | counter | errors | Unhandled non-cancellation full-tick failures. |
| `transitjazz.worker.cycle_duration` | histogram | seconds | Full tick, buckets `0.5,1,2,5,10,20,30,60,120,300`. |
| `transitjazz.worker.cycle_allocated` | gauge | bytes | Tick allocation, not live heap. |
| `transitjazz.worker.gc_heap` / `working_set` | gauge | bytes | Process-wide resource samples. |
| `transitjazz.worker.log_buffer_occupancy` | gauge | records | Existing sidecar queue state. |
| `transitjazz.worker.log_dropped_records` / `log_persist_failures` | counter | records/failures | Existing sidecar loss and persistence state. |

## City metrics

| Internal name prefix | Instrument | Unit | Meaning |
|---|---|---|---|
| `transitjazz.worker.city.last_cycled` / `last_worked` | gauge | seconds | Per-city heartbeat and work freshness. |
| `transitjazz.worker.city.healthy` | gauge | 1 | City-result health. |
| `transitjazz.worker.city.input_fetch_ok` | gauge | 1 | At least one source succeeded. |
| `transitjazz.worker.city.input_records_valid` / `has_input_records` | gauge | records/1 | Valid normalized input and presence. |
| `transitjazz.worker.city.input_lag` / `input_timestamp_known` | gauge | seconds/1 | Source freshness and explicit known state. |
| `transitjazz.worker.city.input_source_failures` | gauge | sources | Failed requests this tick. |
| `transitjazz.worker.city.cycles` / `cycle_errors` | counter | cycles/errors | City lifecycle and non-cancellation failure. |
| `transitjazz.worker.city.cycle_duration` | histogram | seconds | City tick duration with worker buckets. |
| `transitjazz.worker.city.vehicles_processed` / `tones_emitted` | gauge | vehicles/tones | Per-tick work output. |
| `transitjazz.worker.city.*_cache` / `route_index` | gauge | entries | City cache and index sizes. |
| `transitjazz.worker.city.crossings_suppressed_*` | gauge | crossings | One separate named instrument per existing reason. |
| `transitjazz.worker.city.batch_wire` | gauge | bytes | Published batch size, zero when none. |

Future reconciliation outcomes must be separate named instruments, not an outcome label. No score histogram is created until the worker has a real numeric decision model.
