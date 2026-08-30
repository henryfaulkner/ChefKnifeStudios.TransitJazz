# Contract: Removal Surface

**Feature**: 055-remove-parquet-sidecar
**Binds**: FR-001 … FR-004, FR-008 … FR-017, FR-023, FR-024 · SC-005 … SC-009

The exhaustive manifest of what is deleted, edited, and deliberately left alone. Every
path was verified against the working tree on 2026-08-30. `D` = delete, `E` = edit,
`K` = keep (listed where a reader might reasonably expect a delete).

---

## S1 — Worker: sidecar core

| | Path | Note |
|---|---|---|
| D | `…TransitDataWorker/Logging/ParquetLoggingService.cs` | |
| D | `…TransitDataWorker/Logging/ILoggingService.cs` | |
| D | `…TransitDataWorker/Logging/LogEventWorker.cs` | |
| D | `…TransitDataWorker/Logging/LoggingOptions.cs` | |
| D | `…TransitDataWorker/Logging/TelemetryEvent.cs` | |
| D | `…TransitDataWorker/Logging/IEventNotificationService.cs` | `IEventArgs` + bus; parquet-only |
| K | `Logging/Structured*.cs`, `IWorkerStructuredEventLogger.cs` | 054 |
| K | `Logging/CityCycleOutcome.cs`, `CityAnomalyClassifier.cs` | 054 |
| K | `Logging/WireSize.cs` | 051 |

## S2 — Worker: wiring

| | Path | Change |
|---|---|---|
| E | `…TransitDataWorker/Program.cs` | Drop `Configure<LoggingOptions>` (L94), `AddSingleton<IEventNotificationService,…>` (L102), `AddSingleton<ILoggingService, ParquetLoggingService>` (L103), and the `LogEventWorker` hosted-service registration |
| E | `…TransitDataWorker/Worker.cs` | Drop `IEventNotificationService` ctor param (L22) and `ILoggingService` ctor param (L24); drop `PostEvent` sites (L148, L191); drop sidecar self-health block (~L802) and the three sidecar args to `CycleMetrics` (~L268-270) |
| E | `…TransitDataWorker/appsettings.json` | Drop the entire `Logging:Telemetry` block (~L98-104). **Keep `Logging:Structured`.** |
| E | `…TransitDataWorker/*.csproj` | Drop `<PackageReference Include="Parquet.Net" Version="5.*" />` (L19) |
| E | `…TransitDataWorker/Cities/ITransitCity.cs` | Drop `bool EmitsTelemetry { get; }` (L9) — dead once both `PostEvent` sites go |
| E | `…TransitDataWorker/Cities/CityConfig.cs` | Drop `EmitsTelemetry` (L16) |
| E | `…TransitDataWorker/Cities/GtfsRtCity.cs` | Drop `EmitsTelemetry` (L15) |
| E | `…TransitDataWorker/Cities/MartaCity.cs` | Drop `EmitsTelemetry` (L23) |
| E | `…TransitDataWorker/Cities/NymtaCity.cs` | Drop `EmitsTelemetry` (L46) |
| E | `…TransitDataWorker/appsettings.json` | Drop 7 `"EmitsTelemetry"` entries (L11, 25, 31, 56, 62, 68, 84) |
| E | `…TransitDataWorker/appsettings.Development.json` | Drop 4 `"EmitsTelemetry"` entries (L11, 25, 31, 56). **No `Logging:Telemetry` block exists here** |
| E | `…WebAPI/appsettings.json` | Drop 7 `"EmitsTelemetry"` entries (L59, 86, 92, 117, 124, 130, 146) |
| E | `…WebAPI/appsettings.Development.json` | Drop 4 `"EmitsTelemetry"` entries (L44, 71, 77, 102). **No `Logging:Telemetry` block exists here** |

## S3 — Worker: metrics

| | Path | Change |
|---|---|---|
| E | `…TransitDataWorker/Metrics/CycleMetrics.cs` | Drop `LogBufferOccupancy`, `LogDroppedRecords`, `LogPersistFailures` (L23-25) |
| E | `…TransitDataWorker/Metrics/WorkerMetricsReporter.cs` | Drop 3 instrument fields (L23-24, L32), 2 delta trackers (L53-54), 3 `CreateCounter`/`CreateGauge` calls (L67-68, L76), and their record calls (L165-169) |
| E | `observability/grafana/dashboards/transitjazz-worker-overview.json` | Drop "Sidecar queue occupancy" and "Sidecar failure rate" panels; reflow `gridPos.y` of subsequent panels |
| K | `observability/grafana/alerts/transitjazz-worker-alerts.json` | Verified zero references — re-verify before merge |

## S4 — Worker tests

| | Path | Note |
|---|---|---|
| D | `TelemetryEventSchemaTests.cs` | parquet column contract |
| D | `PartitionPathTests.cs` | blob partition layout |
| D | `ChannelLoadSheddingTests.cs` | removed channel's `DropWrite` |
| D | `RecordEmitRulesTests.cs` | `TelemetryEvent` emission rules |
| D | `TelemetryCityNameParityTests.cs` | `city_name` parquet parity |
| D | `WireBytesTelemetryTests.cs` | `batch_wire_bytes` column |
| E | `FailureIsolationTests.cs` | L55 `ThrowingLoggingService : ILoggingService` — retarget to the structured logger; the "a sink fault must not kill a cycle" scenario stays meaningful |
| E | `StructuredLoggingVolumeTests.cs` | **Delete `LegacyTelemetryConfigurationAndWorkerMetricPathRemainPresent` (L80-97).** A 054 dual-run guard built entirely from source-text and JSON assertions — the compiler will NOT catch it. 4 of its 6 asserts break: `"eventNotifications.PostEvent"` (L90), `"EmitsTelemetry"` (L93), `"\"Telemetry\""` (L94), `"\"Enabled\": true"` (L95). Keep every other test in the file |
| E | `StructuredLoggingCityCoverageTests.cs` | **Delete `TelemetryRemainsConfiguredIndependentlyOfStructuredEvents` (L30-38).** The second dual-run guard: parses `appsettings.json` at run time and asserts `EmitsTelemetry == true` for every city plus `Logging:Telemetry:Enabled == true`. **Also not compiler-caught** — throws `KeyNotFoundException`. Keep `EveryConfiguredWorkerCityCanProduceAValidatedAnomalyEvent` (L9-28) and `AppSettingsPath()` (L40-41) |
| E | `CityLoopTests.cs` | Delete the two telemetry-gate methods (L16, L54) and the stub `EmitsTelemetry` members (L80, L87). Keep the rest — INV-1's no-name-branching rule still holds |
| K | `WireSizeTests.cs`, `CityAnomalyClassifierTests.cs`, `CityCycleOutcomeTests.cs`, all other `StructuredLogging*` / `WorkerStructuredEvent*` | See contract C1's exemption table for the only two permitted edits |

## S5 — WebAPI

| | Path | Change |
|---|---|---|
| D | `…WebAPI/EndpointGroups/TelemetryEndpoints.cs` | |
| E | `…WebAPI/Program.cs` | Drop `Configure<LoggingOptions>` (L242), `AddSingleton<IEventNotificationService,…>` (L250), `AddSingleton<ILoggingService, ParquetLoggingService>` (L251), `.MapTelemetryEndpoints()` (L288) |
| K | `…WebAPI/*.csproj` project reference to the worker | Still needed for non-logging shared types — verify at implementation; drop only if nothing else uses it |

## S6 — Shared contracts

| | Path | Change |
|---|---|---|
| D | `Shared/TelemetryData/TelemetryEventDto.cs` | |
| D | `Shared/TelemetryData/TelemetryPagingDtos.cs` | |
| E | `Shared/ApiEndpoints.cs` | Drop the `Telemetry` static class (L23-27) |

## S7 — Client

| | Path | Change |
|---|---|---|
| D | `Client.Core/Services/EndpointsServices/TelemetryEndpointsService.cs` | |
| E | Client DI registration | Drop `ITelemetryEndpointsService` registration |
| D | `Client.WebApp/Pages/Telemetry.razor` | |
| D | `Client.WebApp/Pages/TelemetryTable.razor` | |
| D | `Client.WebApp/Pages/TelemetryColumn.cs` | |
| E | `Client.WebApp/Pages/Index.razor` | Drop the `/telemetry` linktree link (L11) |

## S8 — Developer tooling

| | Path | Note |
|---|---|---|
| D | `tools/telemetry-query-tool/` | Entire Go module |
| D | `tools/telemetry-mcp/` | Entire Go module |
| D | `tools/test-telemetry-mcp.ps1` | |
| E | `.mcp.json` | Drop the `telemetry-query-bridge` server block — **contains a live Azure account key**; see `S11` |
| K | `.mcp.json` `azure-monitor` block | Used by the `transitjazz-logs` skill |
| K | `tools/transitjazz-logs-query/` | 054 tooling |

## S9 — Agent skills (all three trees: `.claude/`, `.agents/`, `.opencode/`)

| | Path | Note |
|---|---|---|
| D | `mj-data-explorer/functions/insights.md` | |
| D | `mj-data-explorer/functions/troubleshooting.md` | |
| D | `mj-data-explorer/functions/sync-schemas.md` | Synced the parquet column contract |
| D | `mj-data-explorer/references/telemetry-query-guide.md` | |
| D | `mj-data-explorer/references/telemetry-schema.md` | |
| E | `mj-data-explorer/SKILL.md` | Rewrite: drop telemetry router arms; rewrite the `description:` frontmatter, which currently leads with "explorer for … telemetry … emitted by the data worker's logging sidecar" |
| K | `mj-data-explorer/functions/gtfs-compatibility.md` | **`discover-transit-city/SKILL.md:176` depends on it** |
| K | `mj-data-explorer/references/mj-api-*.md`, `neighborhood-routes-context.md` | Live API, not telemetry |
| K | `.skill-sync-backups/**` | Historical snapshots |

**Sync obligation**: run `tools/sync-skills.ps1` and verify all three trees match. A
partial delete is resurrected by the next sync.

## S10 — Infrastructure

| | Path | Change |
|---|---|---|
| D | `bicep/modules/telemetryStorage.bicep` | Account + blob service + container + role assignment |
| E | `bicep/main.bicep` | Drop `enableLegacyTelemetry` (L63); `telemetryStorageAccountName` / `telemetryContainerName` vars (L82-83); `telemetryStorage` module block (L186-197); three `Logging__Telemetry__*` env vars (L313-325); two outputs (L385-386) |
| E | `bicep/main.json` | **Regenerate from `main.bicep`. Never hand-edit.** |
| E | `bicep/README.md` | Drop telemetry-storage documentation |

## S11 — Secret remediation

`.mcp.json` currently commits a live
`AZURE_STORAGE_CONNECTION_STRING` including `AccountName=randomstoragehenry` and a real
`AccountKey`. Removing the block clears it from `HEAD` but **not from git history**.

⚠️ **Correction**: `randomstoragehenry` is **not** the Bicep-managed telemetry account
(`mjtel{env}{hash}`) that S10 deletes. They are unrelated accounts, so storage deletion
would never have remediated this credential.

**Resolution**: the release owner **rotated the `randomstoragehenry` access key on
2026-08-30**, ahead of and independently of this feature — correctly, since rotation needs
no deploy and no evidence window. The credential is inert.

**Requirement**: record the rotation in the removal audit. Rotation, not file deletion and
not storage deletion, is what remediates a key already committed to history.

## S12 — Documentation

| | Path | Change |
|---|---|---|
| E | `docs/observability/centralized-logging-release-checklist.md` | Record the retired state (FR-022); the dual-run section becomes a completed record, not a pending gate |
| E | `docs/AZURE_CENTRALIZED_LOGGING_DESIGN_DOCUMENT.md` | One-line note that legacy retirement completed in 055 (precedent: the SUPERSEDED banner in feature 049) |
| E | `CLAUDE.md` | Update the plan pointer; note that 013/012/014 telemetry infrastructure is retired |
| K | `specs/012-*` … `specs/054-*` | Historical record — **do not edit** (FR-014) |
| K | `docs/incident reports/**`, `bloat-reports/**` | Historical record |
| K | `specs/053-worker-observability/contracts/metrics-contract.md` | Documents the 3 retired metrics, but is a shipped feature's contract — historical |
| K | `.claude/settings.local.json:26` | Stale permission string naming feature 013; harmless |

---

## Acceptance

| # | Assertion |
|---|---|
| A1 | Solution builds with zero unresolved references |
| A2 | Full test suite passes; no test asserts removed behavior |
| A3 | No `Parquet.Net` reference in any `.csproj` |
| A4 | No `Logging:Telemetry` key in any `appsettings*.json` or Bicep env var |
| A5 | No `/telemetry` route and no link to one |
| A6 | `tools/telemetry-*` absent; `.mcp.json` has no `telemetry-query-bridge` |
| A7 | All three skill trees identical after sync; `gtfs-compatibility.md` present in each |
| A8 | `bicep/main.json` regenerated from source, consistent with `main.bicep` |
| A9 | Repo search returns only the historical classes listed in research D10 |
