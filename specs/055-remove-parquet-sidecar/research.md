# Phase 0 Research: Remove the Parquet Telemetry Sidecar

**Feature**: 055-remove-parquet-sidecar
**Date**: 2026-08-30

All findings below come from direct reads of the current working tree, not from prior
specifications. Where a spec assumption proved wrong, that is stated explicitly.

---

## D1: The seam between feature 013 (remove) and feature 054 (keep)

**Decision**: The `Logging/` folder holds two unrelated subsystems. Split it by dependency
on `IEventArgs`/`ILoggingService`, not by filename.

**REMOVE** (013 parquet path):

| File | Lines | Role |
|---|---|---|
| `Logging/ParquetLoggingService.cs` | 130 | Blob writer, `Parquet.Net` serializer |
| `Logging/ILoggingService.cs` | 20 | Sink abstraction (`Accumulate`/`FlushAsync`) |
| `Logging/LogEventWorker.cs` | 158 | Bounded-channel drain + flush timer |
| `Logging/LoggingOptions.cs` | 28 | Binds `Logging:Telemetry:*` |
| `Logging/TelemetryEvent.cs` | 58 | The snake_case parquet row record |
| `Logging/IEventNotificationService.cs` | 22 | In-process bus — see D2 |

**KEEP** (054 structured path — verified independent):

`StructuredEventEmitter.cs`, `StructuredLogEvent.cs`, `StructuredEventPolicy.cs`,
`StructuredLogRedactor.cs`, `StructuredLoggingOptions.cs`, `IWorkerStructuredEventLogger.cs`.

**Rationale**: `StructuredEventEmitter` writes through `ILogger` directly — its own doc
comment says "no second transport is used." It has zero references to `ILoggingService`,
`IEventArgs`, or `Parquet`. Verified by reading the file in full.

**KEEP, with a caveat** — `CityCycleOutcome.cs`, `CityAnomalyClassifier.cs`, `WireSize.cs`
sit in the same folder but serve the 054 structured path and the 051 wire measurement.
`CityAnomalyClassifier.Classify` feeds `StructuredLogEventName.CityCycleAnomaly`. These
stay. `WireSize.Measure` is called at `Worker.cs:837` — see D5 for its fate.

---

## D2: `IEventNotificationService` is exclusively a parquet-path component

**Decision**: Remove it entirely, including its DI registration in both hosts.

**Evidence**: Every non-test caller posts exactly one payload type — `TelemetryEvent` —
at `Worker.cs:148` and `Worker.cs:191`. The only subscriber is `LogEventWorker`. When
`TelemetryEvent` and `LogEventWorker` go, the bus has no publisher and no subscriber.

**Rationale**: This is the server-side mirror of `Client.Core`'s bus, introduced by 013
purely to decouple the hot path from blob I/O. Retaining an in-process bus with no
participants would be dead infrastructure. The client-side `IEventNotificationService`
is a **different type in a different assembly** and is untouched.

**Alternative rejected**: Keeping the bus "in case something needs it later." Nothing
does; 054 deliberately chose direct `ILogger` emission over a second transport.

---

## D3: The spec's "delete mj-data-explorer" instruction must be narrowed

**Decision**: Carve the telemetry function out of `mj-data-explorer`; do **not** delete
the skill. Delete only its telemetry-bound files.

**Evidence — this contradicts a literal reading of the clarification**: the live CRON
skill `discover-transit-city` depends on it at `SKILL.md:176`:

> Apply the `mj-data-explorer` skill's `functions/gtfs-compatibility.md` interpretation
> table to produce independent bus and rail verdicts.

`gtfs-compatibility.md` is about GTFS feed structure, not telemetry — it mentions
"telemetry" twice, incidentally. Deleting the skill wholesale would break the weekly
city-discovery routine, which is out of scope for this feature.

**Resolution** (consistent with the spec's own Assumptions, which reserved the skill's
non-telemetry capabilities from scope):

| Path | Action |
|---|---|
| `functions/insights.md` | DELETE — telemetry querying |
| `functions/troubleshooting.md` | DELETE — telemetry querying |
| `functions/sync-schemas.md` | DELETE — syncs the parquet column contract |
| `references/telemetry-query-guide.md` | DELETE |
| `references/telemetry-schema.md` | DELETE |
| `functions/gtfs-compatibility.md` | KEEP — `discover-transit-city` depends on it |
| `references/mj-api-*.md`, `neighborhood-routes-context.md` | KEEP — live API, not telemetry |
| `SKILL.md` | REWRITE — drop telemetry router arms and the telemetry framing in its `description` frontmatter |

**Sync obligation**: skills are triplicated across `.claude/skills/`, `.agents/skills/`,
and `.opencode/skills/`. `tools/sync-skills.ps1` exists for this. All three trees must
match, or the next sync run will resurrect deleted files.

---

## D4: Grafana has three panels bound to sidecar self-health metrics

**Decision**: Remove the two dashboard panels and the three metric instruments together
with the sidecar.

**Evidence**: `observability/grafana/dashboards/transitjazz-worker-overview.json`

| Panel | Line | Metrics |
|---|---|---|
| "Sidecar queue occupancy" | ~307 | `transitjazz_worker_log_buffer_occupancy` |
| "Sidecar failure rate" | ~317 | `..._log_dropped_records_total`, `..._log_persist_failures_total` |

Backed in code by `Metrics/WorkerMetricsReporter.cs:67,68,76` and the
`LogBufferOccupancy` / `LogDroppedRecords` / `LogPersistFailures` fields of
`Metrics/CycleMetrics.cs:23-25`.

**This is the FR-005 / FR-006 tension, and it resolves cleanly**: FR-005 requires numeric
monitoring to survive unchanged; FR-006 requires sidecar self-health signals to go. These
three metrics measure *the sidecar itself* — they have no subject once it is removed.
Removing them satisfies FR-006 without violating FR-005, whose intent is that signals
about *transit processing* are preserved. The spec anticipated exactly this in its edge
cases: leaving them pinned at zero would read as "healthy" rather than "gone."

**Confirmed safe**: `observability/grafana/alerts/transitjazz-worker-alerts.json` contains
**zero** references to any of the three metrics. No alert rule breaks.

**Note for planning**: `specs/053-worker-observability/contracts/metrics-contract.md:22-23`
documents these metrics. It is a historical contract for a shipped feature — per FR-014 it
is **not** edited. The live dashboard is current configuration and **is** edited.

---

## D5: `batch_wire_bytes` and `WireSize` — the 051 baseline question

**Decision**: Keep `WireSize.cs` and the `Worker.cs:837` measurement call. Remove only the
`batch_wire_bytes` *parquet column*, which disappears with `TelemetryEvent`.

**Rationale**: Feature 051 Phase 3 was gated on a `batch_wire_bytes` baseline drawn from
parquet history. That history is discarded by this feature (FR-020's recorded decision is
where that is consciously accepted). But `WireSize.Measure` is a pure, unit-tested function
with its own test file (`WireSizeTests.cs`) and is cheap to retain.

**RESOLVED (2026-08-30, release owner): DISCARD.** The `batch_wire_bytes` history is
discarded with the storage account; no export is performed. If feature 051 Phase 3 is ever
revived it must re-establish its egress baseline from new measurements. Retaining
`WireSize.cs` keeps that possible — the measurement code survives, only the history goes.

---

## D6: The in-app telemetry surface spans four projects

**Decision**: Remove the full vertical slice.

| Layer | Path |
|---|---|
| Page | `Client.WebApp/Pages/Telemetry.razor` (+ any `TelemetryTable` component) |
| Link | `Client.WebApp/Pages/Index.razor:11` — the `/telemetry` linktree entry |
| Client service | `Client.Core/Services/EndpointsServices/TelemetryEndpointsService.cs` + its DI registration |
| Contracts | `Shared/TelemetryData/*` (incl. `TelemetryEventDto.cs`), `Shared/ApiEndpoints.cs:23-27` |
| Server | `WebAPI/EndpointGroups/TelemetryEndpoints.cs`, `WebAPI/Program.cs:288` `.MapTelemetryEndpoints()` |

**Notable**: `WebAPI/Program.cs:250-251` registers `IEventNotificationService` and
`ILoggingService` in the **API host** too — the API reads parquet through
`ParquetLoggingService`'s options to serve the page. Both registrations go, and with them
the WebAPI's project reference need for the worker's `Logging` types.

---

## D7: Infrastructure removal surface

**Decision**: Delete the module and every reference; deploy code first, infra second.

| Location | Item |
|---|---|
| `bicep/modules/telemetryStorage.bicep` | Entire file — account, blob service, container, `Storage Blob Data Contributor` role assignment |
| `bicep/main.bicep:63` | `enableLegacyTelemetry` param |
| `bicep/main.bicep:82-83` | `telemetryStorageAccountName`, `telemetryContainerName` vars |
| `bicep/main.bicep:186-197` | `telemetryStorage` module block |
| `bicep/main.bicep:313-325` | Three `Logging__Telemetry__*` container env vars |
| `bicep/main.bicep:385-386` | Two telemetry outputs |
| `bicep/main.json` | Regenerated build artifact — must be rebuilt, not hand-edited |

**Ordering rationale** (FR-021): the container env vars and the storage grant are removed
in the same infra deployment. If infra went first, running containers would lose
`Logging__Telemetry__BlobServiceUri` and their write role while `ParquetLoggingService`
still existed, producing repeated credential failures. Code-first makes the infra step a
no-op for the application.

---

## D8: Secret remediation — an unplanned but mandatory win

**Decision**: Removing `.mcp.json`'s `telemetry-query-bridge` block eliminates a **live,
committed Azure storage account key**.

**Evidence**: `.mcp.json` currently contains a full
`AZURE_STORAGE_CONNECTION_STRING` with `AccountName=randomstoragehenry` and a real
`AccountKey=...` in committed source.

**Implication beyond deletion**: deleting the file entry removes the key from `HEAD` but
**not from git history**.

⚠️ **Correction (analysis pass, 2026-08-30)**: this decision originally claimed the account
"is being deleted by D7, which makes the key inert." **That was wrong.** The committed key
belongs to account `randomstoragehenry`, which is **not** the Bicep-managed telemetry
account `mjtel${environment}${uniqueString(...)}` (`main.bicep:82`) that D7/Slice C deletes.
They are unrelated accounts, so storage deletion would never have neutralized this key. Had
the original chain been followed, the credential would have stayed live indefinitely behind
a seven-day gate that has not started.

**Resolution**: the release owner **rotated the `randomstoragehenry` access key on
2026-08-30**, independently of and ahead of this feature. The committed credential is now
inert. This was correctly handled as ungated work — key rotation requires no deploy, no
code change, and no evidence window.

**Recommendation carried into quickstart**: record the rotation in the removal audit
(T041a). Do not attribute this remediation to storage deletion. Rotation — never file
deletion — is what remediates a key already committed to history.

---

## D9: Test surface

**Decision**: Delete parquet-bound test files; repair tests that merely *stub* the removed
interfaces.

**DELETE** (assert removed behavior):

- `TelemetryEventSchemaTests.cs` — asserts the parquet column contract
- `PartitionPathTests.cs` — asserts `dt=YYYY-MM-DD` blob layout
- `ChannelLoadSheddingTests.cs` — asserts `DropWrite` on the removed channel
- `RecordEmitRulesTests.cs` — asserts `TelemetryEvent` emission rules
- `TelemetryCityNameParityTests.cs` — asserts `city_name` parquet column parity
- `WireBytesTelemetryTests.cs` — asserts `batch_wire_bytes` on the parquet row

**REPAIR** (reference removed types incidentally):

- `FailureIsolationTests.cs:55` — `ThrowingLoggingService : ILoggingService`. The scenario
  it protects (a sink fault must not kill a cycle) is still meaningful against the
  structured logger; retarget rather than delete.
- `StructuredLoggingVolumeTests.cs:90` — asserts the literal string
  `"eventNotifications.PostEvent"` appears in `Worker.cs` source. This **will fail** once
  the bus is removed. It is a source-text assertion, so the compiler will not catch it —
  it fails at run time. Must be updated in the same change.

**KEEP untouched**: `WireSizeTests.cs`, `CityAnomalyClassifierTests.cs`,
`CityCycleOutcomeTests.cs`, and all `StructuredLogging*`/`WorkerStructuredEvent*` tests.

---

## D10: Repository-wide reference classification (FR-024 / SC-008)

**Decision**: Define the allowed-match set precisely so SC-008 is decidable.

**MUST be clean after removal** (active surface):
`src/**` (excluding `bin`/`obj`), `bicep/**`, `tools/**`, `.mcp.json`,
`observability/grafana/dashboards/**`, `CLAUDE.md`, current `docs/` guidance,
all three skill trees.

**MUST be left intact** (historical record, per FR-014):
`specs/012-*` … `specs/054-*`, `docs/incident reports/**`, `bloat-reports/**`,
`.specify/memory/constitution.md` history block, `.skill-sync-backups/**`.

**Judgment calls**:

- `docs/AZURE_CENTRALIZED_LOGGING_DESIGN_DOCUMENT.md` — a design doc for shipped 054.
  Historical; add a one-line note that legacy retirement completed in 055, matching the
  precedent set for `DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md` in feature 049.
- `docs/observability/centralized-logging-release-checklist.md` — **current operational
  record**. FR-022 requires updating it to show the retired state.
- `.claude/settings.local.json:26` — a stale permission entry naming the 013 feature
  string. Harmless; leave it.
- `bin/`/`obj/` build outputs — regenerate on build; not edited.

---

## Resolved unknowns

Every `NEEDS CLARIFICATION` from Technical Context is closed:

| Unknown | Resolution |
|---|---|
| Does removing the sidecar break Grafana? | No. D4 — two panels and three instruments retire with it; zero alerts affected. |
| Is `IEventNotificationService` shared with 054? | No. D2 — parquet-only on the server. |
| Can `mj-data-explorer` be deleted outright? | No. D3 — `discover-transit-city` depends on its GTFS function; carve instead. |
| Does the WebAPI depend on the sidecar? | Yes. D6 — it registers both services to read parquet for the page. |
| Is the 051 `batch_wire_bytes` baseline lost? | Yes, by design. D5 — flagged as an explicit FR-020 export-or-discard checkpoint. |
| Are there hidden secrets in the removed surface? | Yes. D8 — an account key for `randomstoragehenry` in `.mcp.json`. **Not** neutralized by storage deletion (different account); remediated by key rotation on 2026-08-30. |
