# Quickstart: Remove the Parquet Telemetry Sidecar

**Feature**: 055-remove-parquet-sidecar

Execution order and verification. Read `contracts/evidence-gate.md` first — as of
2026-08-30 the gate is **BLOCKED**, so steps 1–9 may be *built* but not *merged*.

---

## Step 0 — Gate check (blocking for merge, not for work)

```powershell
# Read the current gate state
Get-Content docs/observability/centralized-logging-release-checklist.md |
  Select-String -Pattern 'PENDING|BLOCKED|Current state'
```

**Expected today**: many `PENDING` rows and `Current state: BLOCKED`.

Proceed with implementation on the branch. Do **not** merge or deploy until every row in
`evidence-gate.md` G1–G4 passes and G6 authorization is recorded.

---

## Slice A — Stop writing

### Step 1: Delete the sidecar core

Delete the six files in `S1` of `contracts/removal-surface.md`. Keep the 054 and 051 files
in the same folder — the seam is by dependency, not by folder.

```powershell
$w = "src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Logging"
Remove-Item "$w/ParquetLoggingService.cs","$w/ILoggingService.cs","$w/LogEventWorker.cs",
            "$w/LoggingOptions.cs","$w/TelemetryEvent.cs","$w/IEventNotificationService.cs"
```

### Step 2: Unwire the worker

Edit `Program.cs` (3 DI registrations + the `LogEventWorker` hosted service), `Worker.cs`
(2 ctor params, 2 `PostEvent` sites, the self-health block, the 3 metric args),
`appsettings.json` (drop `Logging:Telemetry`, **keep `Logging:Structured`**), and the
`.csproj` (`Parquet.Net`).

### Step 3: Retire sidecar self-health metrics

`CycleMetrics.cs` (3 fields), `WorkerMetricsReporter.cs` (3 instruments, 2 delta trackers,
their record calls), and the two Grafana panels.

**Re-verify the alert precondition before proceeding:**

```powershell
Select-String -Path observability/grafana/alerts/transitjazz-worker-alerts.json `
  -Pattern 'log_buffer_occupancy|log_dropped_records|log_persist_failures'
# Expected: no output. Any hit means an alert was added since planning — remove it here.
```

### Step 4: Fix the tests

Delete the six test files in `S4`. Then repair the two edits — in particular:

```powershell
# This assertion is source TEXT, not a type reference. The compiler will not catch it.
Select-String -Path src/Server/*.Tests/StructuredLoggingVolumeTests.cs -Pattern 'PostEvent'
```

### Step 5: Build and test

```powershell
dotnet build src/ChefKnifeStudios.TransitJazz.sln
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests
```

**Gate**: green build; 054 structured-logging tests pass **unmodified** (contract C1). If
you had to edit a `StructuredLogging*` test to make it pass, the removal cut too deep —
stop and reassess.

---

## Slice B — Remove orphaned readers

### Step 6: The `/telemetry` vertical slice

Delete across four projects per `S5`–`S7`: server endpoint group + its DI/mapping, Shared
DTOs + `ApiEndpoints.Telemetry`, client service + DI, the three page files, and the
`Index.razor` link.

### Step 7: Developer tooling

```powershell
Remove-Item -Recurse tools/telemetry-query-tool, tools/telemetry-mcp
Remove-Item tools/test-telemetry-mcp.ps1
# Then edit .mcp.json: drop the telemetry-query-bridge block (keep azure-monitor).
```

### Step 8: Carve the skill — do not delete it

Delete the five telemetry files from `mj-data-explorer` and rewrite `SKILL.md`, in **all
three trees**. Keep `functions/gtfs-compatibility.md`:

```powershell
# Prove the dependency before you are tempted to delete the whole skill
Select-String -Path .claude/skills/discover-transit-city/SKILL.md -Pattern 'mj-data-explorer'
# → SKILL.md:176 the weekly CRON routine depends on its GTFS interpretation table
```

Then sync and verify all three trees match:

```powershell
./tools/sync-skills.ps1
```

### Step 9: Verify and deploy

```powershell
dotnet build src/ChefKnifeStudios.TransitJazz.sln
dotnet test src/ChefKnifeStudios.TransitJazz.sln
```

Deploy **worker + server atomically**, then the SWA client, then the same changes on
`deploy/marta-jazz`. Client-first would leave a live page calling deleted endpoints.

**Post-deploy verification** (run over a full cycle window):

| Check | Expected |
|---|---|
| Telemetry blobs created | Zero new objects |
| Retained Grafana panels | All populate; no gap |
| Structured-log investigation | Reproduces a known anomaly (contract C6) |
| Cycle cadence | Within normal historical variation |
| `/telemetry` | Clean 404; no link on the landing page |
| Worker start/stop | No flush, upload, or credential errors |

---

## Slice C — Reclaim infrastructure

### Step 10: FR-020 decision — already made: **DISCARD**

The release owner decided on 2026-08-30 to **discard** all historical telemetry data,
including the `batch_wire_bytes` series that held the feature 051 Phase 3 egress baseline.

**No export step is required.** Transcribe the decision into the release checklist
(step 13) and proceed directly to deletion.

### Step 11: Remove the infrastructure

Delete `bicep/modules/telemetryStorage.bicep` and the six reference sites in `main.bicep`,
then regenerate `main.json`:

```powershell
az bicep build --file bicep/main.bicep --outfile bicep/main.json
```

**Known constraint**: the release checklist already records ARM regeneration as `BLOCKED`
in the restricted workspace (the CLI could not fetch the Bicep compiler). Complete this
where the compiler is available. **Never hand-edit `main.json`.**

Deploy the infra change. Confirm the worker and API run a full cycle with no permission,
credential, or missing-resource errors.

### Step 12: Confirm secret remediation

The key committed in `.mcp.json` belongs to account **`randomstoragehenry`** — **not** the
Bicep-managed telemetry account deleted in step 11. Deleting that storage account therefore
never neutralized it (research D8, corrected).

**The release owner rotated the `randomstoragehenry` access key on 2026-08-30**, ahead of
and independently of this feature, so the credential is already inert. Removing the file
entry clears `HEAD` but not git history — rotation is the remediation.

Confirm the rotation is recorded in the removal audit (T041a). No step here gates it.

### Step 13: Update the record (FR-022)

Append the authorization block from `evidence-gate.md` G6, and update the checklist so the
dual-run section reads as a completed historical record rather than a pending gate.

---

## Final verification

```powershell
# FR-024 / SC-008 — only historical matches may remain
Select-String -Path (Get-ChildItem -Recurse -File -Include *.cs,*.razor,*.json,*.bicep,*.md,*.ps1,*.go `
  | Where-Object FullName -notmatch 'worktrees|\\bin\\|\\obj\\|skill-sync-backups|\\specs\\|incident reports|bloat-reports') `
  -Pattern 'Parquet|telemetry-query|ILoggingService|TelemetryEvent'
```

**Expected**: no matches outside the historical classes named in research D10.

| # | Final gate |
|---|---|
| 1 | Solution builds; full test suite green |
| 2 | Zero telemetry objects written over a full day |
| 3 | All retained panels populate; zero alerts broken |
| 4 | Structured-log investigation reproduces a known anomaly |
| 5 | No `/telemetry` route or link |
| 6 | All three skill trees in sync; `gtfs-compatibility.md` intact |
| 7 | No standing blob write permission for the services |
| 8 | Checklist shows the retired state with approver and date |
