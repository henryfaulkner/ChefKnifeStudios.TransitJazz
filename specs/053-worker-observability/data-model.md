# Data Model: Worker Observability

## Configuration

| Entity | Fields and validation |
|---|---|
| `MetricsOptions` | `Enabled` defaults false and is read once; `ExportIntervalMilliseconds` is positive and no greater than worker interval; Cloud endpoint is HTTPS and ends `/v1/metrics`; authorization comes only from secret configuration; service/environment names are stable; `LocalPrometheusEnabled` is rejected in production. |
| `WorkerOptions` | `CycleIntervalSeconds` is positive and defaults to 10, replacing the hard-coded timer period and publishing the liveness basis. |

## Runtime entities

### `CityFetchResult`

| Field | Meaning |
|---|---|
| `Feed` | Existing normalized `FeedMessage`. |
| `Outcome` | Closed enum: `Success`, `Empty`, `PartialFailure`, `Failure`; never a metric label. |
| `ValidRecordCount` | Non-negative normalized input count. |
| `SourceTimestampUtc` | Nullable trustworthy source timestamp. |
| `SuccessfulSourceCount` / `FailedSourceCount` | Non-negative multi-source fetch result counts. |

`Success` has input, `Empty` is a successful zero-record response, `PartialFailure` has both successes and failures, and `Failure` has no usable source. These states remove the current empty-feed/fetch-failure ambiguity.

### Cycle summaries and reporter

`CityCycleMetrics` is one immutable summary per configured city per tick. It contains city name, completion time, fetch result, existing `CityTickResult`, city duration, `DidWork`, and city-error state. `WorkerCycleMetrics` is one full-tick summary emitted in an outer finally with completion time, duration, did-any-work, interval, allocations, process-wide resources, sidecar health, city count, and aggregate error state.

| `IWorkerMetricsReporter` operation | Required invocation |
|---|---|
| `InitializeCities` | Startup, using the complete configured city registry. |
| `ReportCityCycle` | Exactly once per city result, independent of `EmitsTelemetry`. |
| `ReportCityError` | City non-cancellation exception path. |
| `ReportWorkerCycleCompleted` | Every full tick from outer finally. |
| `FlushAsync` | Best-effort orderly host shutdown. |

The null reporter implements no-ops. `Worker` references the interface only, while the sealed concrete reporter owns OpenTelemetry instruments.

## Dimensions, cardinality, and lifecycle

City-attributable metrics carry only `transit.city`, with values exactly equal to startup `ITransitCity.Name` values. Worker metrics do not carry city. No other metric attributes are allowed. `service.instance.id` distinguishes replicas but must not be used to aggregate or alert.

```text
worker series × maximum replicas
+ city series × configured cities × maximum replicas
+ histogram buckets × their label combinations
< 1,000 internal budget
```

The lifecycle is: bind options; construct reporter; initialize city series; run each city fetch/result/report; record city error in catch; write worker heartbeat in full-cycle finally; force flush best-effort during shutdown. Existing Parquet `TelemetryEvent`, DTO, and query contract remain unchanged.
