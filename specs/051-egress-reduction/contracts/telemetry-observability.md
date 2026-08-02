# Contract: Telemetry & Observability (Phase 0)

Binds spec FR-001, FR-002, FR-003 / US1. Additive only — no existing telemetry column, log statement, or bicep output changes meaning.

## C1. `batch_wire_bytes` telemetry column

- **Producer**: `Worker.ProcessSpatialReconciliationAsync`, measured on the exact `List<EventEnvelope>` passed to `PublishBatchAsync`, via one `MessagePackSerializer.Serialize` into a pooled/reused buffer. Measured only when the city `EmitsTelemetry` and a non-empty envelope list is published.
- **Row placement**: PerCityCycle rows carry the per-city value; FullCycle rows carry the sum across cities that tick (consistent with `tones_emitted` et al.).
- **Null semantics**: `null` when the tick published nothing for that city (empty batch, unhealthy tick, publish returned false). MUST NOT emit `0` for "didn't publish" — `0` is unreachable and a query reading `batch_wire_bytes > 0` must equal "published ticks".
- **Frozen name**: `batch_wire_bytes` — snake_case, becomes the parquet column name verbatim (Parquet.Net rule). Never rename.

### Accept/verify vectors
| Scenario | Expectation |
|---|---|
| Healthy tick, marta publishes 60 KB envelope list | PerCityCycle row: `batch_wire_bytes` ≈ 61440 ± envelope overhead; > 0 |
| Feed hiccup, zero entities → no publish | `batch_wire_bytes` IS NULL on that row |
| FullCycle row, 6 cities published | equals the sum of that tick's six PerCityCycle values |
| MCP query `dataset=telemetry`, filter `batch_wire_bytes > 50000` | accepted by validator, returns rows |

## C2. Go validator sync (same change, non-negotiable)

`tools/telemetry-mcp/internal/validate/validate.go`: add `batch_wire_bytes` to the numeric-kind column allow-list. Contract test vector: the filter string `batch_wire_bytes > 100000` MUST validate; `batch_wire_bytes = 'x'` MUST be rejected (numeric kind, unquoted numbers only).

## C3. Log Analytics wiring (bicep)

- New module `bicep/modules/logAnalytics.bicep`: `Microsoft.OperationalInsights/workspaces`, sku `PerGB2018`, outputs `customerId`; shared key retrieved via `listKeys` at the `main.bicep` call site (secure param flow into `cae`).
- `main.bicep`: pass `logAnalyticsCustomerId` + `logAnalyticsSharedKey` into the existing `cae` module invocation. `containerAppsEnvironment.bicep` is NOT edited — its conditional (`empty(logAnalyticsCustomerId) ? null : {...}`) starts taking the populated branch.
- **Postcondition**: `ContainerAppConsoleLogs_CL` (or the environment's configured table) receives server container stdout within minutes of deploy; a KQL query for a known `Worker` log line returns rows.

## C4. Static Web App plan

- `bicep/modules/staticWebApp.bicep`: `sku { name: 'Standard', tier: 'Standard' }`. No other property changes; custom domains unaffected (Standard is a superset).
- **Postcondition**: portal shows Standard; monthly 100 GB bandwidth cap no longer applies.

## Explicitly out of contract
No OTEL exporter changes, no new log statements, no dashboard authoring, no alerting. The existing `ParquetLoggingService` flush cadence (5 min) is untouched.
