# Contract: Structured Log Event v1

## Transport

The worker emits a meaningful occurrence once through `ILogger` using UTC, one-line JSON console output. Azure Container Apps stores it in `ContainerAppConsoleLogs`; application fields are structured formatter state inside `Log`. KQL recipes must parse the captured Azure shape, not assume table columns.

This contract does not authorize per-city/full-cycle log rows, a metric exporter change, or an application-to-workspace client.

## Envelope

| Property | Required | Values / validation |
|---|---:|---|
| `EventName` | Yes | Exact v1 name below. |
| `EventVersion` | Yes | Positive integer; `1` initially. |
| `EventId` | Yes | Fresh non-secret opaque ID. |
| `CycleId` | Worker-cycle events | Fresh non-secret opaque ID once per outer worker tick. |
| `Outcome` | Yes | `Succeeded`, `Partial`, or `Failed`. |
| `ReasonCode` | Classification events | Stable bounded uppercase code; never prose. |
| `City` | City events | One configured canonical city slug. |
| `DurationMs` | When relevant | Non-negative integer. |
| `LoadAttempt`, `CityCount`, `RouteCount` | Route-index load events | Non-negative scalar loading evidence only. |
| `DeploymentRevision` | When available | Safe platform revision identity only. |
| `ExceptionType` | Exception events | Type name only; never message, stack, URI, or exception data. |

`CityCycleAnomaly` may add only scalar `TonesEmitted`, `VehiclesProcessed`, `FeedFreshnessSeconds`, `CrossingsEmitted`, `CrossingsSuppressedFirstSeen`, `CrossingsSuppressedDeltaLeq0`, `CrossingsSuppressedTeleport`, `CrossingsSuppressedTransfer`, `BatchWireBytes`, `PublishAttempted`, and `PublishSucceeded` fields when relevant.

## Event names

| Family | Required v1 events | Semantics |
|---|---|---|
| Lifecycle | `WorkerStarted`, `WorkerStopped` | Process lifecycle only. |
| Input | `CityInputFailed`, `CityInputPartial`, `CityInputEmpty` | Derived from `CityFetchResult`; city scoped. |
| Route index | `RouteIndexUnavailable`, `RouteIndexLoadFailed`, `RouteIndexLoaded` | Distinct from input failure; load events carry bounded attempt/count/duration evidence. |
| City anomaly | `CityCycleAnomaly` | Exactly one coalesced row with city/cycle/reason/counts/publish state. |
| Publishing | `PublishFailed`, `PublishRecovered` | City/cycle scoped when applicable. |
| Worker cycle | `WorkerCycleFailed`, `WorkerCycleRecovered` | Worker-level transition with a cycle when available. |

The only v1 missing-tone reasons are `NO_VEHICLES`, `STALE_FEED`, `DUPLICATE_FEED`, `ROUTE_INDEX_UNAVAILABLE`, `NO_CROSSINGS`, `ALL_CROSSINGS_SUPPRESSED`, `INPUT_FAILED`, and `PUBLISH_FAILED`.

## Emission rules

- The first active `(City?, EventName, ReasonCode)` condition emits one event.
- Material outcome/reason changes emit new evidence; the same active condition emits a reminder no more than once per configurable 15-minute interval.
- Clearing an active condition emits recovery once.
- Normal successful cycles and metric samples emit no informational event merely to duplicate Grafana data.
- Policy state is in-process only and must never block polling, reconciliation, publishing, or metrics.

## Security and compatibility

- Properties may not contain credentials, secret-bearing URLs, headers/cookies, connection strings, credential files, request/response/feed bodies, entity arrays, configuration, or arbitrary exception strings.
- The emitter accepts allow-listed values only. Existing unstructured worker logs must be audited before routing; exception objects are not a redaction mechanism.
- New names, reason codes, meaning changes, or required fields need a reviewed versioned contract update. Additive v1 fields remain optional; consumers ignore unknown fields.
- `CycleId`, `EventId`, revision, and city are log-query values only and must not become Grafana metric labels.
