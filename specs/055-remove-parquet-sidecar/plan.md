# Implementation Plan: Remove the Parquet Telemetry Sidecar

**Branch**: `055-remove-parquet-sidecar` | **Date**: 2026-08-30 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/055-remove-parquet-sidecar/spec.md`

## Summary

Feature 013 built an in-process telemetry sidecar that buffers worker events onto a private
notification bus and writes Parquet part-files to blob storage; features 012/014 built a Go
query tool, an MCP bridge, and an exploration skill to read them; a `/telemetry` page reads
them through the WebAPI. Feature 054 replaced the diagnostic purpose of all of this with
centralized structured logging, and deliberately left the old path running behind an
evidence-gated dual run.

This plan retires the whole path in three deployable slices, ordered so the reversible work
lands before the irreversible: **(A)** stop writing — sidecar, bus, options, `Parquet.Net`,
and the two Grafana panels that monitor the sidecar itself; **(B)** remove the orphaned
readers — the `/telemetry` vertical slice across four projects, both Go tools, the MCP
bridge registration, and the telemetry half of `mj-data-explorer`; **(C)** reclaim
infrastructure — the storage account, container, role assignment, and the
`enableLegacyTelemetry` toggle.

The work is pure deletion. No new capability is built, and the two surviving observability
surfaces — Grafana metrics and 054's structured logs — are untouched apart from the panels
whose subject no longer exists.

## Technical Context

**Language/Version**: C# / .NET 10 (worker, WebAPI, Blazor WASM client, Shared); Go 1.2x (the two telemetry tools, both deleted); Bicep (infra); Markdown (skills)
**Primary Dependencies**: `Parquet.Net 5.*` — **removed**; `Azure.Storage.Blobs` + `Azure.Identity` — retained (used elsewhere); OpenTelemetry — retained; `mcp-go` — removed with the bridge
**Storage**: Azure Blob container `parquet` on account `mjtel{env}{hash}` — **removed**
**Testing**: xUnit (`TransitDataWorker.Tests`, `Shared.Tests`); 6 test files deleted, 3 repaired (`FailureIsolationTests`, `StructuredLoggingVolumeTests`, `StructuredLoggingCityCoverageTests`) plus `CityLoopTests`'s two gate methods — see research D9 and contract C1's exemption table
**Target Platform**: Azure Container Apps (worker + API), Azure Static Web Apps (client)
**Project Type**: Multi-project web solution (11 projects) + standalone dev tools + IaC + agent skills
**Performance Goals**: No regression — worker cycle cadence unchanged; removal drops per-tick buffering and a 5-minute blob flush, so a small improvement is expected but not required
**Constraints**: Evidence gate must pass before shipping (FR-018); code deploys before infra (FR-021); skills triplicated across three trees must stay in sync
**Scale/Scope**: ~6 source files + 6 test files deleted and 3 repaired in the worker; `EmitsTelemetry` removed from 5 code sites + 22 config entries across 4 files; 4-project vertical slice removed; 2 Go modules deleted; 1 Bicep module deleted; 5 skill files deleted + 1 rewritten × 3 trees

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against `.specify/memory/constitution.md` v3.3.2.

| Principle | Assessment |
|---|---|
| **I. Decoupled Cloud Architecture** | **PASS — improved.** Removes a worker→blob write dependency and a WebAPI→blob read dependency. Fewer couplings, no new ones. |
| **II. No Frontend Secrets** | **PASS — materially improved.** Deletes a committed Azure account key from `.mcp.json` (research D8). The key is for `randomstoragehenry`, a different account from the one Slice C deletes, so storage deletion never neutralized it — it was remediated by key rotation on 2026-08-30, ahead of this feature. |
| **III. Two-Pass Pipeline** | **PASS — untouched.** The removal is observability-only. No change to snap/lerp passes, `Worker.cs` processing logic, or cycle structure. |
| **IV. OpenTelemetry Observability** | **PASS with a documented narrowing.** Three metric instruments and two dashboard panels are removed, but all three measure the *sidecar itself* — they have no subject after removal. Every metric describing transit processing survives. Zero alert rules affected (verified). This is the principle's intent preserved, not weakened: the constitution mandates observability of the system, and the system loses a component. |
| **V. GitHub Actions CI/CD** | **PASS.** Existing lanes unchanged; the standard worker+server-atomic then client ordering applies (see Deployment Sequence). |
| **VI. GTFS ID Mapping** | **N/A.** No join-key, `RouteJoinKey`, or GTFS identity surface touched. |
| **VII. OSM Cartography** | **N/A.** No map, layer, or basemap change. |
| **VIII. Generative Transit Music** | **N/A.** No synth, tone, or trigger-point change. |
| **IX–XIII (UX principles)** | **PASS.** The only UI change is deleting a developer-facing diagnostic page and its link. No interaction model, zoom behavior, overlay, localization, or dark-mode surface is altered — the removed page is not part of the map experience these principles govern. |

**Gate result: PASS.** No violations. Complexity Tracking section omitted as unused.

**Post-Phase-1 re-check: PASS.** The design phase surfaced one narrowing (D3: carve
`mj-data-explorer` rather than delete it, because `discover-transit-city` depends on its
non-telemetry GTFS function). That narrowing *reduces* blast radius and introduces no new
constitutional concern.

## Project Structure

### Documentation (this feature)

```text
specs/055-remove-parquet-sidecar/
├── spec.md              # Phase -1 output (/speckit.specify)
├── plan.md              # This file
├── research.md          # Phase 0 output — D1..D10
├── data-model.md        # Phase 1 output — removal inventory
├── quickstart.md        # Phase 1 output — gated execution + verification
├── checklists/
│   └── requirements.md  # Spec quality checklist
├── contracts/
│   ├── retained-observability.md   # What MUST still work afterward
│   ├── removal-surface.md          # Exhaustive delete/edit manifest
│   └── evidence-gate.md            # Preconditions authorizing the merge
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

Paths below are the **actual** touched surface, verified against the working tree.

```text
src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
├── Logging/
│   ├── ParquetLoggingService.cs        DELETE   blob writer
│   ├── ILoggingService.cs              DELETE   sink abstraction
│   ├── LogEventWorker.cs               DELETE   channel drain + flush timer
│   ├── LoggingOptions.cs               DELETE   binds Logging:Telemetry:*
│   ├── TelemetryEvent.cs               DELETE   snake_case parquet row
│   ├── IEventNotificationService.cs    DELETE   parquet-only bus (D2)
│   ├── StructuredEventEmitter.cs       KEEP     054
│   ├── StructuredLogEvent.cs           KEEP     054
│   ├── StructuredEventPolicy.cs        KEEP     054
│   ├── StructuredLogRedactor.cs        KEEP     054
│   ├── StructuredLoggingOptions.cs     KEEP     054
│   ├── IWorkerStructuredEventLogger.cs KEEP     054
│   ├── CityCycleOutcome.cs             KEEP     054 anomaly input
│   ├── CityAnomalyClassifier.cs        KEEP     054 anomaly classifier
│   └── WireSize.cs                     KEEP     051 measurement (D5)
├── Metrics/
│   ├── CycleMetrics.cs                 EDIT     drop 3 sidecar fields (L23-25)
│   └── WorkerMetricsReporter.cs        EDIT     drop 3 instruments + record calls
├── Worker.cs                           EDIT     drop bus ctor param, 2 PostEvent sites,
│                                                sidecar self-health block, metric args
├── Program.cs                          EDIT     drop 3 DI registrations (L94,102,103)
├── appsettings.json                    EDIT     drop Logging:Telemetry block
└── *.csproj                            EDIT     drop Parquet.Net PackageReference

src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/
├── TelemetryEventSchemaTests.cs        DELETE
├── PartitionPathTests.cs               DELETE
├── ChannelLoadSheddingTests.cs         DELETE
├── RecordEmitRulesTests.cs             DELETE
├── TelemetryCityNameParityTests.cs     DELETE
├── WireBytesTelemetryTests.cs          DELETE
├── FailureIsolationTests.cs            EDIT     retarget stub to structured logger
└── StructuredLoggingVolumeTests.cs     EDIT     L90 source-text assert WILL FAIL (D9)

src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/
├── EndpointGroups/TelemetryEndpoints.cs DELETE
├── Program.cs                           EDIT    drop L242,250,251 + L288 MapTelemetryEndpoints

src/ChefKnifeStudios.TransitJazz.Shared/
├── TelemetryData/TelemetryEventDto.cs   DELETE
├── TelemetryData/TelemetryPagingDtos.cs DELETE
└── ApiEndpoints.cs                      EDIT    drop Telemetry class (L23-27)

src/Client/ChefKnifeStudios.TransitJazz.Client.Core/
└── Services/EndpointsServices/TelemetryEndpointsService.cs  DELETE (+ DI registration)

src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/
├── Telemetry.razor                      DELETE
├── TelemetryTable.razor                 DELETE
├── TelemetryColumn.cs                   DELETE
└── Index.razor                          EDIT    drop /telemetry linktree link (L11)

tools/
├── telemetry-query-tool/                DELETE  entire Go module
├── telemetry-mcp/                       DELETE  entire Go module
└── test-telemetry-mcp.ps1               DELETE

bicep/
├── modules/telemetryStorage.bicep       DELETE  account+container+role
├── main.bicep                           EDIT    L63,82-83,186-197,313-325,385-386
└── main.json                            REGEN   build artifact, never hand-edited

observability/grafana/dashboards/
└── transitjazz-worker-overview.json     EDIT    drop 2 sidecar panels

.mcp.json                                EDIT    drop telemetry-query-bridge (+ live key)

.claude/skills/ + .agents/skills/ + .opencode/skills/   (all three trees, kept in sync)
└── mj-data-explorer/
    ├── functions/insights.md             DELETE
    ├── functions/troubleshooting.md      DELETE
    ├── functions/sync-schemas.md         DELETE
    ├── references/telemetry-query-guide.md  DELETE
    ├── references/telemetry-schema.md       DELETE
    ├── functions/gtfs-compatibility.md      KEEP  discover-transit-city depends on it
    └── SKILL.md                             REWRITE  drop telemetry router arms + description

docs/observability/centralized-logging-release-checklist.md  EDIT  record retired state (FR-022)
CLAUDE.md                                EDIT    replace plan pointer; note 013/012/014 retired
```

**Structure Decision**: No new structure. This feature only deletes from, and edits within,
the existing 11-project solution, the `tools/` standalone-tool area, the `bicep/` IaC tree,
the `observability/` config tree, and the three mirrored skill trees. The one structural
subtlety is that `Logging/` is **not** deleted wholesale — it is split along the 013/054
seam established in research D1.

## Deployment Sequence

Ordering is load-bearing (FR-021, and the wire-deploy constraint that spans three CI lanes).

1. **Gate** — confirm the 054 dual-run evidence record is complete and passing for its full
   window, with recorded approver and date. Nothing below ships until this holds.
2. **Slice A + B code** — worker and server deploy **atomically** (they share the `Logging`
   types and the Shared contracts), then the SWA client separately. Deploying the client
   first would leave a page calling deleted endpoints.
3. **`deploy/marta-jazz`** — the same changes must land on the MartaJazz branch, per the
   established multi-lane constraint.
4. **Verify** — a full cycle window with no telemetry objects written, all retained panels
   populated, structured-log investigation working.
5. **Slice C infra** — only after step 4 passes. Removing storage earlier would strip
   `Logging__Telemetry__BlobServiceUri` and the blob write role from containers still
   running `ParquetLoggingService`, causing repeated credential failures.
6. **Record** — update the release checklist to the retired state (FR-022).

## Risks & Mitigations

| Risk | Mitigation |
|---|---|
| `StructuredLoggingVolumeTests.cs:90` asserts a source-code string; the compiler cannot catch it | Called out explicitly in research D9 and in the removal-surface contract; repaired in the same change |
| Deleting `mj-data-explorer` outright would break the weekly `discover-transit-city` CRON routine | Research D3 narrows to a surgical carve; `gtfs-compatibility.md` retained |
| Skill trees are triplicated; a partial delete gets resurrected by the next sync | All three trees edited together; `tools/sync-skills.ps1` run and verified |
| 051 Phase 3's `batch_wire_bytes` baseline exists only in parquet | Raised as an explicit export-or-discard checkpoint under FR-020 before storage deletion |
| Live Azure key in `.mcp.json` remains in git history after the file edit | The key is for `randomstoragehenry`, a **different** account from the Bicep-managed `mjtel{env}{hash}` deleted in Slice C — storage deletion would never have neutralized it (research D8, corrected). **Remediated by key rotation on 2026-08-30**, ungated and ahead of this feature |
| `bicep/main.json` hand-edited and drifting from `main.bicep` | Regenerated from source, never edited directly |
