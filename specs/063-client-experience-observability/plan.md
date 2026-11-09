# Implementation Plan: Client Experience Observability

**Branch**: `065-client-experience-observability` | **Date**: 2026-11-08 | **Spec**: [spec.md](spec.md)  
**Input**: [Client experience and health observability design](../../docs/CLIENT_TOAST_OBSERVABILITY_DESIGN_PLAN.md) and [feature specification](spec.md). Planning only; no application code, deployment, alert routing, migration application, or commits.

## Summary

Measure actual visitor success and degradation at the current-scope capability owner, independently of notification policy. Capture eligible attempt starts and outcomes, requested capability states and bounded observed time, verified recovery, and accepted toast presentations. A passive browser reporter preserves bounded observations across offline periods and submits them to a validated, idempotent WebAPI intake backed by the existing PostgreSQL database. Reviewed SQL views and correction-aware daily aggregates supply an operator dashboard and guarded Grafana alerts. Independent probes and browser checks expose collection and pre-bootstrap failures. Release the compatible server before emitting clients; baseline provisional alert thresholds before operational routing.

## Technical Context

**Language/Version**: Existing C#/.NET 10, Razor/Blazor WebAssembly, JavaScript ES modules, SQL, and JSON; no new runtime language.  
**Primary Dependencies**: Existing ASP.NET Core minimal APIs, EF Core/Npgsql, OpenTelemetry, SignalR client, MatBlazor notification path, Grafana PostgreSQL data source; native browser IndexedDB. No new telemetry vendor or runtime project.  
**Storage**: Bounded IndexedDB outbox with memory fallback; append-only `client_experience_events`, correction-aware `client_experience_daily`, and maintenance watermark in existing PostgreSQL.  
**Testing**: Existing xUnit Client.Shared.Tests and Server.WebAPI.Tests; WebAPI TestHost, controlled time/transport, opt-in disposable PostgreSQL integration, real browser/multi-tab and supported-device audio checks, seeded dashboard/alert fixtures, Release builds.  
**Target Platform**: Existing Azure Static Web App browser client and WebAPI Container App; configured PostgreSQL and Grafana. Production CORS/proxy and existing contact-point access require rollout verification.  
**Project Type**: Existing client + WebAPI + database + operator artifacts; Worker behavior unchanged.  
**Performance Goals**: No per-note/frame/vehicle reporting; one foreground scheduler and one sender per page; at most one full duration window per minute; 20 events/64 KiB per batch initially; measure actual client and database overhead before broad enablement.  
**Constraints**: No frontend secret or durable visitor identity; fixed typed catalog and safe fields; 24-hour attempt correlation, seven-day delivery horizon, 30-day raw and 13-month daily retention; queue 1,000 events/4 MiB; server never falsely acknowledges failed persistence.  
**Scale/Scope**: Bootstrap/city/live/audio journeys, explicit settings/stop/navigation actions, four requested capabilities, all 26 existing notification issue codes, feedback/collection views, guarded alerts, independent checks. No formal SLO or general Web Vitals in this feature.

## Constitution Check

*GATE: Constitution v3.3.2 checked before Phase 0 and again after Phase 1. All applicable gates pass; deployed/browser behavior remains release evidence.*

| Principle / rule | Pre-research / post-design | Design response |
| --- | --- | --- |
| I. Three-unit Azure architecture | Pass / Pass | Reuse existing WebAssembly client, WebAPI and database; Worker remains a separate unchanged deployable unit. No new service or vendor. |
| II. No frontend secrets | Pass / Pass | Anonymous bounded intake, public API base URL only; rate/concurrency limits and server-side environment/identity controls. |
| III and VI. Transit pipeline and route identity | Pass / Pass | Observe accepted current-city owner outcomes; preserve SignalR/Worker data contracts and `RouteJoinKey` semantics. No route IDs in telemetry. |
| IV. Structured observability | Pass / Pass | Persist experience observations for audited cohorts; add fixed-label collection metrics and structured logs through existing telemetry path. No unbounded labels or raw payload logging. |
| V. CI/CD | Pass / Pass | Preserve GitHub Actions and existing two release artifacts; add migration verification and tests to the server/client gates. |
| VII-X. Map, music, selection, controls | Pass / Pass | Capture coarse confirmed outcomes only; do not change map layers, notes, filtering, zoom controls, or playback behavior. |
| XI. Overlay timing | Pass / Pass | Toast capture occurs after accepted presentation; no UI timing or overlay behavior change. |
| XII. Localization | Pass / Pass | No new visitor copy planned; any later visitor-facing text must use existing RouteFilterResources and language variants. Existing feature-061 language-control gap remains outside scope. |
| XIII. Light/dark parity | Pass / Pass | No new visitor surface planned; any added surface must ship both themes. |
| `AGENTS.md` Git rule | Pass / Pass | Optional before/after-plan commit hooks are skipped; no commit commands. |

No constitution exception or unresolved technical clarification remains. Release gates for browser behavior, production routing, query grants, alert delivery, and overhead are documented in [quickstart.md](quickstart.md).

## Project Structure

### Documentation (this feature)

```text
specs/063-client-experience-observability/
  spec.md
  checklists/requirements.md
  plan.md
  research.md
  data-model.md
  contracts/client-experience-v1.md
  contracts/operator-metrics-v1.md
  quickstart.md
  tasks.md                         # next workflow; not created by planning
```

### Source Code (existing seams; proposed additions named descriptively)

```text
src/ChefKnifeStudios.TransitJazz.Shared/
  ApiEndpoints.cs
  ClientExperience/                  # fixed codes, versioned batch/event DTOs [new]
src/Client/ChefKnifeStudios.TransitJazz.Client.Core/
  Services/{SignalRNotificationService,LiveDeliveryState}.cs
  ClientExperience/                  # passive capture interface [new]
src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/
  ViewModels/ApplicationViewModel.cs
  Components/{Map.razor.cs,MapReadinessState.cs}
  Services/Audio/AudioCapabilityHealthTracker.cs
  Services/{SettingsChangeController,CityNavigationController}.cs
  Services/Notifications/{NotificationCoordinator,NotificationIssue}.cs
  ClientExperience/                  # reporter, attempt/window coordinator [new]
  wwwroot/js/                        # shared bootstrap/application outbox module [new]
src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/
  Program.cs
  App.razor
  Pages/TransitMap.razor.cs
  wwwroot/index.html                 # early bootstrap handoff
src/Server/ChefKnifeStudios.TransitJazz.Server.Data/
  AppDbContext.cs
  Models/, Configurations/, Migrations/
  ClientExperience/                  # raw store, reviewed views, daily rollup [new]
src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/
  EndpointGroups/{StatsRateLimitingExtensions,StatsEndpoints}.cs
  EndpointGroups/ClientExperienceEndpoints.cs [new]
  ClientExperience/                  # ingestion validation, maintenance, metrics/probe [new]
  Program.cs
src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/
src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/
observability/grafana/{dashboards,alerts}/
observability/synthetics/client-experience/check.js [new]
docs/observability/
```

**Structure Decision**: The stable transport contract belongs in Shared; Core reports facts through a passive interface without a Client.Shared presentation dependency. The WebAPI owns validation/rate policy; Server.Data owns append-only records, logical views, and correction-aware aggregates. One browser outbox serves early bootstrap and the application. Existing Grafana/SQL and structured metrics remain separate experience and collection evidence. No generic event bus or new deployment unit.

## Design Details

### 1. Measurement contract and owner capture

Implement the [capture contract](contracts/client-experience-v1.md) before wiring owners. The fixed catalog must map every accepted owner outcome and all notification issue codes. Assign page, city, attempt, episode and event IDs at accepted transitions; keep `event_id` stable through retries. Preserve one terminal result per attempt, 24-hour correlation, current-generation rejection, separate effect and persistence attempts under one parent settings action, and the distinct source/destination page scopes of city navigation.

Instrument `ApplicationViewModel` for route and city readiness, `SignalRNotificationService`/`LiveDeliveryState` for confirmed current-city delivery, map readiness at accepted generation, native audio unlock and `AudioCapabilityHealthTracker` for requested voice health, `SettingsChangeController` and stop outcome owner for explicit effects, and the navigation controller for source outcomes. Capture before notification-specific gating. The existing audio unlock completion path must use `AudioUnlockAttemptState.CompleteAttempt` acceptance, not a local result that may have become stale after mute/retry. Settings capture must use fixed action/outcome codes, never `ConfirmedValue`.

`NotificationCoordinator.PresentationCommitted` already fires only after successful presentation and revalidation. Snapshot city, accepted time, severity, resource keys, and copies of issue/explained arrays at commit under its lock; report outside the lock. Keep the application-lifetime capture subscriber separate from `NotificationHost`, which can remount. Remove unused legacy warning/error toast entry points after catalog and coordinator coverage is verified. No notification policy or timing change.

### 2. Browser observation and delivery

A minimal pre-Blazor script creates `pageSessionId`, buffered `session_started`, and the shared outbox. Register the application reporter before `App.razor` starts `ApplicationViewModel.InitializeAsync`; do not resolve scoped presentation services from root DI. One foreground scheduler drives deadlines and non-overlapping 60-second observation windows, with partial windows at city/visibility changes. A callback gap beyond 15 seconds is unobserved. Reassess owner state on foreground return. Deliberate pause/mute/hidden exclusions do not exclude the explicit action that changes them. Verified recovery uses current-scope owner evidence; loss, dismissal, and scope end never imply recovery.

Use native IndexedDB transactional per-event writes/deletes, bounded memory fallback, one sender per page, 20-event/64-KiB batches, prompt first send, retry/backoff and acknowledgement-only deletion. Preserve original page/city IDs across reload and offline replay. A reporting exception is contained and cannot alter playback, map, controls or toast timing. Browser visibility listeners and module ownership need explicit disposal; avoid a growing subscription graph. No unload dependency or per-frame/per-note callbacks.

### 3. Intake, persistence, and reconciliation

Create the explicit `/api/client-experience/batch` endpoint and shared route constant, with strict typed DTO validation, body-size enforcement even for unknown `Content-Length`, bounded catalog/string/array/duration rules, per-member permanent rejection and safe response codes. Transactionally insert accepted UUID events with server receipt/environment. Acknowledge only committed or already-present IDs; on persistence failure acknowledge none. Log fixed result categories, not payload. Make the existing Stats rejection callback endpoint-aware and add a dedicated per-source/global limit, bounded concurrency, timeout and fixed-label collection meter. Feature disabled by default; enabled without `TransitJazzDB` fails startup validation. Verify actual production CORS/proxy path and early-bootstrap API URL.

Use `AppDbContext`/Npgsql migration for raw/daily/maintenance entities. Reviewed SQL views reconcile result-before-start, unresolved and excluded outcomes, logical conflicts, overlapping windows, episodes, and presentations. Receipt time drives operational cohorts; occurrence time is separately labeled and skew-checked. An hourly advisory-locked job recomputes affected daily buckets (recent ten days plus late correlation targets), atomically replaces rows including zero/reclassified values, resumes from persisted watermark after downtime, and cleans raw data only after aggregate coverage. Daily aggregates retain mergeable latency buckets and correctly labeled session-day counts; no averaging of daily percentages or percentiles.

### 4. Operator evidence and alert behavior

Build the dashboard and SQL alert rules from the same reviewed views per [operator contract](contracts/operator-metrics-v1.md). Separate journey outcomes, capability health, feedback, and collection health. Show eligible-start cohorts, S/F/D/U/X and sample size, observed-state duration and gaps, verified recovery, affected sessions, readiness latency, presentation severity and issue arrays, delay/loss/quality diagnostics, and long-term weighted trends. Zero denominator is `N/A`; valid zero counts, no data, and query errors remain distinct. Compare only compatible measurement versions/time bases.

Initial health rules use provisional source thresholds and require fresh non-skewed evidence, traffic minima, unresolved/gap guards, and explicit fixed city series. A fresh confirmed audio-stop failure alerts at low traffic even with zero toasts. Delayed backlog does not page as a current outage; missing city series/no-data cannot silently resolve an incident. Provide collection failure/maintenance rules and an ingest/database probe without fake visitor events. Use a scheduled Grafana Cloud Synthetic Monitoring k6 browser check every five minutes for pre-capture startup/map/live failures. The check runner redirects client-experience submissions to a dedicated server-controlled synthetic intake using a runner-held secret, so controlled sessions are excluded from real denominators. Verify tenant access, probe cost, request interception, and actual routing in staging. Exercise Grafana contact-point routing/recovery before operational routing. The runbook records definitions, coverage limits and release evidence.

### 5. Verification and rollout order

Use meaningful unit tests for attempt/current-generation acceptance, eligibility/deadline/gap arithmetic, toast commit classification, outbox replay, and fixed catalog coverage. Use WebAPI TestHost for envelope/body/status/rate behavior and disposable PostgreSQL for concurrent duplicate insert, rollback, lost response, logical conflicts, aggregation corrections, watermark recovery and cleanup. Seed view/dashboard/alert fixtures for zero/unknown/late/skew/low-traffic conditions. Prove bootstrap handoff, IndexedDB multi-tab, trusted audio gesture, map readiness, notification preservation, and capture overhead in real browsers. Run affected client/server suites and Release builds. Details and commands are in [quickstart.md](quickstart.md).

Deploy migration and compatible server intake before client emission. Verify CORS/proxy, grants, synthetic isolation, query plans and event volume under healthy sessions. Enable capture gradually; observe coverage and loss before trusting rates. Baseline thresholds for at least two representative weeks before formal objectives or primary operational routing. Disabling capture stops new reports; disabling intake returns retriable failure without false acknowledgement. No deployment or commit is part of this planning command.

## Phase 0: Research Results

[research.md](research.md) records decisions, rationale, alternatives, repository seams, and primary technical references. It resolves all planning unknowns. Actual deployed grants/routing and measured traffic remain implementation/release checks.

## Phase 1: Design Outputs

[data-model.md](data-model.md) defines identities, state transitions, raw/daily entities and reconciliation. [Capture/intake contract](contracts/client-experience-v1.md) defines versioned events, validation, delivery and response behavior. [Operator contract](contracts/operator-metrics-v1.md) defines cohorts, dashboard, alerts and independent checks. [quickstart.md](quickstart.md) gives verification and rollout sequence. The AGENTS SPECKIT marker is updated directly because no agent-context update script is installed. Post-design constitution gates above pass.

## Phase 2: Task Handoff

The next `/speckit-tasks` run should order: measurement/catalog inventory and tests; server schema/intake/reconciliation; shared browser bootstrap/outbox; first useful owner capture with success denominators and toast presentations; capability windows/recovery and complete action coverage; dashboard/alert/synthetic; browser/database/monitoring proof and staged rollout. Keep verified owner outcomes independent of notification flags, and require red/green evidence for substantive behavior. No `tasks.md` is created in this phase.
