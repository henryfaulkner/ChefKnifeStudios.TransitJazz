# Data Model: Centralized Structured Logging

**Feature**: [Centralized Structured Logging](spec.md)  
**Scope**: Versioned event schema, bounded investigation inputs, release evidence, and the legacy-to-central transition.

## 1. Structured Log Event

One sparse application event written through `ILogger`. It is not a replacement for a per-city or full-cycle metric record.

| Field | Required | Rules | Purpose |
|---|---:|---|---|
| `EventName` | Yes | One value from the event-name contract | Stable event discriminator. |
| `EventVersion` | Yes | Positive integer; initial version is `1` | Allows additive, reviewed schema evolution. |
| `EventId` | Yes | Unique non-secret identifier | Retrieves one emitted event. |
| `CycleId` | Yes for worker-cycle-derived events | One unique ID generated at full worker-tick start | Correlates city and worker events without becoming a metric label. |
| `City` | City events only | Canonical configured city slug | Narrows an investigation and aligns with Grafana city context. |
| `Outcome` | Yes | Bounded value: `Succeeded`, `Partial`, or `Failed` | Communicates the event result. |
| `ReasonCode` | When explanatory classification exists | Stable bounded code; never prose | Supports machine-queryable cause analysis. |
| `DurationMs` | When duration is relevant | Non-negative integer | Adds exceptional timing context. |
| `DeploymentRevision` | When platform context is available | Bounded platform revision | Separates evidence across deployments. |
| `ExceptionType` | On exception events | Type name only | Preserves category without unbounded or secret-bearing exception data. |
| Exceptional counters | Event-specific | Integers only; present only when evidence is useful | Explains an anomaly without recreating metrics. |
| Publish state | Publish/anomaly events | `PublishAttempted`, `PublishSucceeded` | Shows publication outcome. |

### Event-specific diagnostic counters

`CityCycleAnomaly` may add `TonesEmitted`, `VehiclesProcessed`, `FeedFreshnessSeconds`, `CrossingsEmitted`, `CrossingsSuppressedFirstSeen`, `CrossingsSuppressedDeltaLeq0`, `CrossingsSuppressedTeleport`, `CrossingsSuppressedTransfer`, and `BatchWireBytes`. All are bounded scalar evidence; transit entity arrays, feed bodies, and raw metric snapshots are prohibited.

### Event names and reason codes

| Family | Event names | Key reason codes |
|---|---|---|
| Worker lifecycle | `WorkerStarted`, `WorkerStopped`, `WorkerCycleFailed`, `WorkerCycleRecovered` | Event-specific stable operational codes only |
| City input | `CityInputFailed`, `CityInputPartial`, `CityInputEmpty` | Bounded fetch/input classifications |
| Route/index | `RouteIndexUnavailable` | `ROUTE_INDEX_UNAVAILABLE` where used for an anomaly |
| City anomaly | `CityCycleAnomaly` | `NO_VEHICLES`, `STALE_FEED`, `DUPLICATE_FEED`, `ROUTE_INDEX_UNAVAILABLE`, `NO_CROSSINGS`, `ALL_CROSSINGS_SUPPRESSED`, `INPUT_FAILED`, `PUBLISH_FAILED` |
| Publishing | `PublishFailed`, `PublishRecovered` | Bounded publish classifications |

## 2. Event Emission State

The state machine applies to a stable `(City?, EventName, ReasonCode)` key.

```text
Healthy / absent
  └─ detected failure or anomaly → Initial event (active)
                                      ├─ same condition persists → bounded periodic reminder
                                      ├─ material outcome/reason changes → transition event; active key changes
                                      └─ condition clears → recovery event → Healthy / absent
```

Rules:

- The first occurrence is always recorded.
- Repeated identical occurrences do not produce an unbounded stream.
- A material state or reason change records new evidence.
- Recovery is recorded once for an active key.
- The policy state is in-process operational state only; it is not a persistent telemetry store and must not affect worker polling or publishing.

## 3. Redaction Classification

| Classification | Permitted examples | Prohibited examples |
|---|---|---|
| Safe bounded identity | Canonical city, feed type, endpoint name, HTTP status, event/reason code, exception type | Raw full URL, free-form input, vehicle/route identifiers where not part of approved contract |
| Secret-bearing | None | Access token, API key, bearer/auth/cookie header, connection string, credential file, shared key |
| Large/sensitive payload | None | Full request/response body, feed body, transit entity array, serialized configuration |

Validation is source-side: no event builder or raw `ILogger` call used by the feature may accept prohibited values. Formatter/ingestion behavior is not treated as a redaction control.

## 4. Investigation Request

The input to `transitjazz-logs`. It is a request model, not persisted application data.

| Field | Validation | Precedence |
|---|---|---|
| `Workspace` | Approved TransitJazz alias or resource ID | Explicit input, then link, then configured default |
| `Table` | `ContainerAppConsoleLogs` or `ContainerAppSystemLogs` | Explicit input, then inferred safely |
| `UtcRange` | Explicit finite UTC start/end; default modest recent range only when omitted | Explicit input, then link/Grafana context |
| `Kql` | One approved table, explicit time predicate, Basic-compatible operations when console is Basic | Preserved exactly if conforming; otherwise rejected or refined only on request |
| `Limit` | Integer 1–100 | Explicit input or safe default |
| `Output` | `table` (default) or `json` | Explicit input or default |
| Selectors | `CycleId`, `EventId`, `City`, `DeploymentRevision`, `EventName`, `ReasonCode` | Explicit input overrides link/context |
| Grafana context | Link plus symptom | Used only through the existing Grafana workflow |

The ordered context rule is: **explicit input > Azure Logs link > Grafana panel context > selected workspace default**.

## 5. Investigation Result and Doctor Result

| Entity | Required fields | Rules |
|---|---|---|
| `InvestigationResult` | Effective workspace/table/range/KQL/limit, output rows, empty-result explanation when applicable | Human-readable table by default; JSON optional; no token, header, or connection information. |
| `DoctorResult` | First failing layer, status, secret-free next action | Stops at a persistent failure and does not retry or repair it. |
| `DoctorLayer` | Interface, identity, workspace resolution, query permission, table/plan, ingestion freshness, minimal query, empty result | Evaluated in order; `BasicQueryUnsupported` is a distinct compatibility outcome. |

An empty result is evidence only that the selected query matched no current rows. The presentation must preserve possible ingestion-delay, retention, rate-limit, log-level, and filter explanations.

## 6. Central Log Storage Contract

| Stream | Table | Plan | Retention | Notes |
|---|---|---|---|---|
| Worker/Web API stdout and stderr | `ContainerAppConsoleLogs` | Basic by default | 30-day total / fixed 30-day interactive | JSON remains in `Log`; KQL parses the captured final shape. |
| Container Apps platform events | `ContainerAppSystemLogs` | Analytics | 30-day interactive and total | May be empty when no platform event occurs. |
| Legacy history | Existing `*_CL` and Blob/Parquet paths | Existing | Existing historical policy | Read-only history only; not copied to new tables. |

## 7. Dual-Run Evidence

| Field | Validation | Purpose |
|---|---|---|
| `EvidenceId` | Unique ID | Identifies release record. |
| `StartedUtc` / `VerifiedUtc` | UTC timestamps | Proves the consecutive seven-day window. |
| `Scenario` | Canary, zero tone, input failure, publish failure, redaction, query path, retention, cost | Names required proof. |
| `LegacyEvidenceLocation` | Required until cutover | Points to Parquet evidence. |
| `CentralEvidenceLocation` | Required | Points to reproducible central-log evidence/KQL. |
| `Result` | Pass/fail/blocked | Gates Parquet disablement. |
| `Approver` / `Notes` | Release process data | Records review without credential material. |

Parquet writes may transition from **enabled** to **disabled** only when all required evidence passes. Historical blobs stay **preserved** until a separate archival/deletion decision and consumer audit authorize another transition.
