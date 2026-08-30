# Centralized logging removal audit

This audit was to be completed after the seven-day dual run and one centralized-logs-only release.
It is a guard against removing a shared telemetry consumer.

> **Completed 2026-08-30 under a waiver.** The seven-day dual run was **waived** by the release
> owner, and the Azure telemetry storage was **deleted manually** rather than through a gated
> deployment. The consumer inventory below is complete and was verified against the working tree;
> the release-evidence rows are recorded as not run rather than as passed. See the 055 removal
> authorization in `centralized-logging-release-checklist.md`.
>
> **FR-020 historical-data decision (2026-08-30): DISCARD.** The original preservation default
> has been superseded by an explicit release-owner decision — see
> [Historical blob preservation](#historical-blob-preservation).

## Audit metadata

| Field | Value |
|---|---|
| Audit date (UTC) | 2026-08-30 |
| Release revision | `NOT RECORDED` — infrastructure was deleted manually rather than through a gated deployment |
| Auditor/approver | Release owner, 2026-08-30 |
| Seven-day evidence record | **WAIVED** — zero of seven days recorded. Gate G4 was set aside by the release owner, not satisfied. |
| Centralized-only normal release | `NOT RECORDED` — no gated release was performed |

## Consumer inventory

Established by feature 055 research D1–D10 and verified against the working tree during
implementation. Every surface below was found, and every one is removed by feature 055.

| Surface | Reviewed location | Consumer found | Action/owner |
|---|---|---|---|
| Worker Parquet producer/sidecar | `src/Server/...TransitDataWorker` | **Yes** — `Logging/`'s six parquet files (`ParquetLoggingService`, `ILoggingService`, `LogEventWorker`, `LoggingOptions`, `TelemetryEvent`, `IEventNotificationService`), 2 `PostEvent` sites in `Worker.cs`, the `EmitsTelemetry` gate (5 code sites), 3 sidecar self-health metrics, and the `Parquet.Net` package | **Removed** (055 Phase 4, slice A). The 054 structured-logging files in the same folder and `WireSize.cs` (051) are retained. Owner: release owner |
| Blob storage/RBAC/configuration | `bicep/`, app settings | **Yes** — `Logging:Telemetry` block in both `appsettings.json`; `bicep/modules/telemetryStorage.bicep` (account, container, `Storage Blob Data Contributor` role assignment); `enableLegacyTelemetry` toggle + 6 reference sites in `main.bicep` | Config **removed** (055 Phase 4). Azure resources **deleted manually** 2026-08-30. IaC **removed** (055 Phase 7): module file deleted, all 6 `main.bicep` sites removed, `main.json` regenerated via `az bicep build`. ⚠️ The `Storage Blob Data Contributor` role assignment on `serverIdentity` was **not independently verified as gone** (SC-009/FR-017) — it rode the manually deleted account. Owner: release owner |
| Web API telemetry route/DTOs | `src/Server/`, `src/ChefKnifeStudios.TransitJazz.Shared/` | **Yes** — `EndpointGroups/TelemetryEndpoints.cs`, `MapTelemetryEndpoints()`, `ApiEndpoints.Telemetry`, `TelemetryData/TelemetryEventDto.cs`, `TelemetryData/TelemetryPagingDtos.cs` | **Removed** (055 Phase 5). Owner: release owner |
| Client telemetry UI | `src/Client/` | **Yes** — `Pages/Telemetry.razor`, `TelemetryTable.razor`, `TelemetryColumn.cs`, `TelemetryEndpointsService`, its DI registration, and the `/telemetry` link on `Index.razor` | **Removed** (055 Phase 5). Owner: release owner |
| Query tools/MCP | `tools/` | **Yes** — `tools/telemetry-query-tool/` and `tools/telemetry-mcp/` (both Go modules), `tools/test-telemetry-mcp.ps1`, and the `telemetry-query-bridge` server block in `.mcp.json` | **Removed** (055 Phase 5). `azure-monitor` retained for the `transitjazz-logs` skill. Owner: release owner |
| Agent skills/registrations | `skills/`, `.mcp.json`, `.codex/` | **Yes** — `mj-data-explorer`'s 5 telemetry files and its telemetry router arms | **Carved, not deleted** (055 Phase 5). `functions/gtfs-compatibility.md` is retained because `discover-transit-city/SKILL.md:176` depends on it. Applied to the tracked `skills/` source and all three mirrored trees (`.claude`, `.agents`, `.opencode`); `tools/sync-skills.ps1 -Mode Check` reports all four in agreement. Owner: release owner |
| Documentation/contracts | `docs/`, `specs/` | **Yes** — `docs/LOGGING_SIDECAR_SPEC.md`, `docs/AZURE_CENTRALIZED_LOGGING_DESIGN_DOCUMENT.md` | **Bannered as retired** (055 Phase 5). Historical records under `specs/012-*`…`specs/054-*`, `docs/incident reports/`, `bloat-reports/`, and `specs/053-worker-observability/contracts/metrics-contract.md` are deliberately **untouched** (FR-014). Owner: release owner |

## Historical blob preservation

**FR-020 decision (2026-08-30, release owner): DISCARD.** All historical telemetry data is
discarded with the storage account; no export is performed.

| Storage account/container | Existing blobs verified | Deletion approval | Result |
|---|---|---|---|
| `mjtel{env}{hash}` / `parquet` (formerly Bicep-managed) | **Not enumerated before deletion** — the account was removed manually, so no blob inventory was taken | **DISCARD approved 2026-08-30** (release owner, FR-020 / evidence-gate G5) | **DELETED MANUALLY 2026-08-30** by the release owner, outside the Bicep deployment. IaC was updated to match. |

**What this forgoes**: the `batch_wire_bytes` column was the only record of the feature 051
Phase 3 egress baseline. Discarding it means 051 Phase 3, if revived, must re-establish its
baseline from new measurements. `WireSize.cs` and its unit tests are retained (live measurement
code, not history), and `BatchWireBytes` survives as a structured-log field, so a future baseline
can be re-gathered. The release owner accepted this explicitly.

## Secret remediation

| Item | Account | Status |
|---|---|---|
| Committed Azure storage account key | `randomstoragehenry` | **Rotated 2026-08-30** by the release owner, ahead of and independent of this feature. Rotation — not file deletion — is what remediates it: the key remains in git history permanently. This account sits outside the dual-run path, so rotating it disabled no gated resource. |
| `telemetry-query-bridge` MCP block carrying that key | `.mcp.json` | **Removed** (055 T041). Note `.mcp.json` is gitignored and untracked, so this file never committed the key itself; the historical exposure is via `docs/` and `specs/` copies (commits `bae1790`, `8dc83eb`, `4816191`), which are preserved as historical records under FR-014. |
| Formerly Bicep-managed telemetry storage account | `mjtel{env}{hash}` | **Deleted manually 2026-08-30.** This is a **different** account from `randomstoragehenry`; deleting it would never have neutralized the committed key. |
