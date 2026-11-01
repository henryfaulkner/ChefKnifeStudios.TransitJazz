# Quickstart: StatsOverlay Retrieval Optimization

Planning handoff only. No feature implementation, test execution, benchmark, deployment, enablement or commit has occurred in this phase.

## Resolve this feature safely

Work in `C:/Projects/ChefKnifeStudios.TransitJazz`. Do not create/switch branches or run commit-producing hooks. Concurrent feature 062 may own `.specify/feature.json`; do not overwrite it. For future Spec Kit commands set the process-local override, then verify returned paths:

```powershell
$env:SPECIFY_FEATURE_DIRECTORY = 'C:/Projects/ChefKnifeStudios.TransitJazz/specs/061-stats-retrieval-optimization'
. ./.specify/scripts/powershell/common.ps1
Get-FeaturePathsEnv
```

Read spec, plan, research, data model and both contracts before task generation. Do not rerun setup-plan to inspect artifacts: it overwrites plan.md. Preserve concurrent units documents/code and no-commit instructions. AGENTS marker updates must use latest content and modify marker only.

## Implementation sequence for tasks

1. Record current warm/cold open behavior, requests/reads/size/serialization, map/audio readiness, never-opened sessions and retained/transient browser memory with named supported desktop/mobile device, browser/network/load profile and variability. Store real results in validation.md during implementation.
2. Add response metadata, startup-valid interval option and fixed subset budgets and pure schedule helper. Extend contract fixtures and update feature-058 API/read/UI contracts to remove conflicting minute-only/unfiltered-only/session ownership claims. Preserve SQL and reporting fixtures.
3. Extend existing server exact cache/shared fills, shutdown/deadline cleanup, monotonic expiry, budgets and bounded telemetry. Verify selected-route coalescing and no stale fallback.
4. Build store-only DTO state, deterministic time/jitter seams, bounded slots, atomic acceptance, per-selection retries/global 429 and fair single-request scheduler. No charts in store.
5. Register/start/stop from App/WebApp; enabled StatsOverlay always prepares city and All at startup. Verify shared map listener ordering without adding another listener. Refactor overlay to matching reads/notifications, retaining projection/focus/tables/theme behavior.
6. Run focused correctness, browser and matched-load checks; record measured gates. Deploy schedule-aware serving before the client with startup preparation. Expand only after evidence passes.

## Future local verification commands

These are implementation-stage commands, not executed during planning:

```powershell
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.csproj
dotnet test src/ChefKnifeStudios.TransitJazz.Shared.Tests/ChefKnifeStudios.TransitJazz.Shared.Tests.csproj
dotnet test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj
dotnet build src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj
```

Use existing opt-in disposable PostgreSQL configuration only for retained arithmetic/one-statement read checks; no production data mutation. Deterministic tests use project-native manual TimeProvider and controlled query/transport completions, not real sleeps. The plan's Testing Strategy and Acceptance Gates now defines exact test locations, V01–V19 coverage, test-first slices A–F and unexecuted browser/load gates G1–G4 for task generation.

## Configuration to document during implementation

```json
{
  "StatsOverlay": {
    "Enabled": true,
    "ReportedVehiclesEnabled": false,
    "RefreshIntervalMinutes": 60,
    "IngestionGraceMinutes": 2,
    "MaximumCacheEntries": 33,
    "MaximumCacheBytes": 67108864
  },
  "AppSettings": {
    "FeatureFlags": {
      "StatsOverlay": true
    }
  }
}
```

These are the repository defaults: 60-minute schedule, two-minute ingestion grace, 33 entries/67108864 estimated bytes total, and fixed internal caps of 8 filtered entries/16777216 estimated bytes, clamped to those totals. Server limits remain four concurrent fills, a five-second whole-fill deadline, existing request/response caps and endpoint rate limits. `CacheSeconds` is removed; expiration follows the response's fixed refresh boundary or earlier root-city day boundary. Enabled `StatsOverlay` always prepares city and All at startup and refreshes them while visible. `StatsOverlay.RefreshIntervalMinutes` (default 60) is the only configuration added by this feature; filtered retention uses fixed internal budgets of eight entries/16 MiB. Client snapshot retention remains three slots/33554432 estimated bytes.

Before changing the refresh interval, grace or cache bounds, apply the same settings to every API replica and use the existing restart/redeploy path. Verify the resolved configuration on all replicas before comparing hit/coalescing metrics; mixed policy replicas can return different original generation times. A 15-minute interval needs no client rebuild because clients adopt valid schedule metadata from accepted responses. Browser clocks more than 30 seconds ahead of the server generation can produce `invalid_response`; do not manufacture freshness.

## Release evidence and rollback

Record every profile/result/exception, not just pass labels. Demonstrate no open-triggered request/full loader for admitted not-due matching baseline; target binding <=100 ms excluding chart drawing; warm endpoint p95 <=250 ms and original read budgets; zero extra DB reads on valid hits; one fill per exact cold burst per replica. Record same-scope and diverse selections, pressure/nonfit, cancellation/shutdown, replicas, shared-IP limits, 500 visible apps and synchronized boundaries. Measure compressed egress, serialization CPU, actual retained/transient heap, snapshot age, overdue lag, loaders and map/audio variability including never-opened sessions. ~1,000 responses/hour excludes startup/navigation/retries/local days and is not peak capacity proof.

Browser walkthrough covers initial cold opening, warm city/All, repeated close/reopen, rapid routes/navigation, success then failure, offline, hidden/resume, clock jumps, midnight, mixed old/new server, resource/theme/focus/accessibility and disposal. Units/theme presentation performs no requests; this feature does not add units control.

Disable client presentation with `AppSettings.FeatureFlags.StatsOverlay=false`; disable server reads independently with `StatsOverlay.Enabled=false`, which returns `stats_disabled`. Each setting uses its existing app configuration restart/redeploy path. Preserve endpoint semantics, telemetry and SQL. Legacy clients keep their 60-second fallback behavior until reload; measure their request rate separately before claiming total traffic reduction. No distributed/persistent cache, conditional requests, cross-tab coordination, stats push or worker/schema work is introduced.
