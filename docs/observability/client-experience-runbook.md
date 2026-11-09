# Client Experience Observability Runbook

Feature 063 adds a passive, bounded client observation path. Capture and intake default to disabled. The compatible server and database migration must be deployed before enabling a browser build.

## Current implementation status

- The versioned catalog, bounded DTOs, anonymous intake, validation, idempotent append store, raw/daily/maintenance migrations, early bootstrap handoff, and bounded IndexedDB outbox are implemented. Intake, visitor capture, and synthetic intake default to disabled. The API has separate capture and synthetic gates so compatible intake can be deployed before client emission.
- Owner capture covers bootstrap, route data, map readiness, joined live delivery, audio gesture/resource readiness, settings effect and persistence, audio stop, source-city navigation, and accepted notification presentations. Events use fixed codes; setting values, route and vehicle identifiers, URLs, exception text, and rendered copy are excluded.
- Attempt correlation SQL and a 19-panel provisional dashboard are present. Dashboard cohorts use start receipt time, show unresolved starts separately, exclude the server-assigned `synthetic` environment from visitor cohorts, and preserve zero denominators as `N/A`. The dashboard includes weighted daily trends, action outcomes, quality, delay/skew, intake/loss metrics, probe/maintenance outcomes, and watermark state; Grafana datasource mapping and rendered query behavior remain unverified.
- Capability duration windows, validated-window/recovery SQL, foreground owner reassessment, correction-aware daily rebuilds, a persisted maintenance watermark, bounded retention, a cancellable maintenance service, and an opt-in schema/storage probe are implemented locally. The feature's migration-backed PostgreSQL tests have been exercised against a temporary disposable PostgreSQL 17 container; staging and production databases remain untouched.
- A dedicated runner-token synthetic intake assigns `environment=synthetic`, applies a separate named per-source limiter, and works while visitor capture remains disabled. The k6 browser-check script and readiness markers are present; the check is not provisioned or tested against staging. Grafana alert rules are not implemented.
- The migration has not been applied. No disposable PostgreSQL run, real-browser acceptance, staging rollout, Grafana import, or alert delivery was available for this implementation pass.
- Local verification on 2026-11-08: Client.Shared client-experience tests passed (8/8); WebAPI client-experience tests passed (26/26) against the temporary disposable PostgreSQL 17 database, including migration-backed store, view, and daily-aggregation coverage. The Release solution build passed with 0 errors and 9 warnings. Module syntax checks for the browser reporter and k6 check, dashboard/appsettings JSON parsing, and `git diff --check` passed. The temporary database container was removed after the tests; no shared database was changed.
- Latest full-solution Release test run: Shared.Tests passed (43/43), TransitDataWorker.Tests passed (239/239), Client.Shared.Tests had 427 passed and two failures in `StatsOverlayProjectionTests` and `StatsUnitsPreferenceTests`, and WebAPI.Tests passed 339 with 87 database-gated skips. The model assertion was scoped to the `city_minute_statistics` table because this feature adds three entities to the shared EF model; all four `CityMinuteStatisticModelTests` pass. The two Client.Shared failures reproduce individually and are outside the feature's changed files. Feature database-gated tests pass when run separately against the temporary PostgreSQL container. Package vulnerability warnings remain in the Release build.
- See [capture inventory](client-experience-capture-inventory.md) for owner evidence and known limits.

## Measurement definitions

Attempt cohorts are keyed by accepted start receipt time. `S`, `F`, `D`, `U`, and `X` represent succeeded, failed, deadline-exceeded, unresolved, and cancelled/superseded starts. The eligible denominator is `S + F + D + U`; `X` is displayed separately. A zero denominator is `N/A`, not healthy. Recent cohorts can change as delayed results arrive.

Capability health uses observed foreground time and keeps eligible state durations, exclusions, and gaps separate. Capabilities can overlap. A recovered episode requires current-scope owner evidence. A hidden/suspended page, a missing terminal event, a quiet transit feed, or no toast does not establish health or recovery.

## Intake and rollout

1. Review and apply `AddClientExperienceEvents` and `AddClientExperienceDaily` to an isolated staging database.
2. Deploy a server with `ClientExperience:Enabled=true` only after the database schema is present. Keep `CaptureEnabled=false` until the application route and CORS policy are verified.
3. After route and CORS verification, enable `ClientExperience:CaptureEnabled`; then set the public `client-experience-api-base-url` and `client-experience-enabled` meta values in the web shell. Never put a secret in browser configuration.
4. Enable a small client cohort, then inspect fixed-label intake outcomes, duplicate/rejection counts, skew, delivery delay, and local loss reports.
5. Keep dashboard rates provisional until the representative client release emits complete owner outcomes and database/browser acceptance passes.

For controlled startup checks, additionally set `ClientExperience:SyntheticIntakeEnabled=true` and store a random 32+ character value in `ClientExperience:SyntheticIntakeToken` (environment variable `ClientExperience__SyntheticIntakeToken`). Put the same token in the Grafana runner's secret variable `CLIENT_EXPERIENCE_SYNTHETIC_TOKEN`; the browser script rewrites submissions to `/api/client-experience/synthetic/batch`. Never configure this token in `index.html`, client metadata, or browser-visible application settings. Synthetic rows are server-labeled and excluded from visitor cohorts.

Disable capture to stop new client reports. Disable intake to return a retriable `503` without acknowledging events. Do not remove stored data during rollback.

## Reading the current dashboard

Journey rows are receipt-time cohorts grouped by the accepted start. `S`, `F`, `D`, and `U` contribute to the eligible denominator; `X` is shown separately. A missing terminal result remains unresolved until the foreground deadline or correlation horizon closes it. Recent rows can change when late results arrive. Check sample count and affected sessions before comparing cities or releases; an empty cohort is `N/A`, not a success.

The presentation panel counts accepted `toast_presented` events. A combined toast is one presentation even when it represents several issue codes. Presentation volume is feedback evidence only: no toast can mean no incident, a suppressed presentation, an unmounted host, or incomplete capture. Owner journey events remain the health denominator.

The dashboard includes provisional state-duration, exclusion/gap, and same-scope recovery panels. Their SQL has not been executed against PostgreSQL in this environment, so treat them as unverified until the disposable database tests pass. Do not infer transit freshness from the available panels. A quiet feed is not a failure or success signal until each source has an explicit cadence contract.

When a hidden page returns to the foreground, the reporter closes the hidden interval and asks the page's route, map, live-delivery, and audio owners for their current state. It emits a transition only when the reassessed state differs from the last confirmed state. This behavior has local code coverage through the bounded state tracker; browser suspension and voice-readiness recovery still need a real-browser run.

## Collection diagnosis

The client keeps queued events until acknowledged and retries transient failures with bounded backoff. A `413` causes the browser to split a batch; `429` honors `Retry-After`; a disabled or unavailable intake returns a retriable response. Local loss reports are best effort and are not an exact accounting record. Queue age, browser storage denial, a cleared site store, and a visitor who never returns can leave collection gaps.

Use server intake counters for request outcomes and accepted, duplicate, and rejected event counts; use the schema/storage probe and maintenance metrics to distinguish collection-path failure from an empty visitor cohort. Compare intake health with page-start coverage before interpreting an empty event table. Collection alerts are not configured. The synthetic script is a local artifact only and is not an operational detector until a five-minute Grafana browser check and alert route are provisioned and exercised.

## Version and privacy changes

Add new journey, action, capability, reason, issue, or state codes only through a coordinated compatible server rollout. Increment `measurementVersion` when eligibility, deadlines, or metric interpretation changes; isolate comparisons by schema and measurement version. Never add selected settings, transit identifiers, coordinates, URLs, tokens, visitor IP/user-agent, exception strings, or rendered notification text to the event payload.

## Alert and synthetic status

Operational routing is not ready. Traffic, freshness, unresolved-attempt, gap, stop-failure, backlog, no-data, query-error, and missing-city rules have not been implemented or exercised. The runner-token intake and browser script exist locally, but the check, alert routing, and recovery have not been provisioned or tested. Keep alert routing disabled until each rule is tested against fresh failing, delayed, low-traffic, valid-zero, no-data, missing-series, and recovery cases.

## Coverage limits

Pages that fail before the bootstrap script runs are outside browser-report coverage. IndexedDB denial, browser storage clearing, abrupt termination before a queued write, and visitors who never return after an outage can lose observations. The synthetic script is not provisioned yet and therefore does not currently cover that gap. Audio evidence covers context and resource readiness, not physical sound. Live freshness remains unknown until feed cadence is explicit.

## Release evidence still required

Production `/api` proxy and allowed-origin proof, disposable PostgreSQL replay/rollback tests, multi-tab browser replay, mobile/desktop audio gesture checks, Grafana query grants, alert routing and recovery, synthetic isolation, representative traffic volume, query plans, and measured client/database overhead are pending. No deployment, migration application, or alert routing was performed as part of this implementation work.

## Implementation gate review

The repository Constitution v3.3.2 was rechecked on 2026-11-08. This implementation adds no visitor-facing surface, frontend secret, route identity change, playback behavior, or notification timing change. Capture uses fixed classifications and bounded observations; deployment and external evidence gates remain separate.

The local implementation covers versioned intake, owner attempts, capability windows, accepted presentations, correction-aware daily aggregates/retention, maintenance, the database probe, a server-controlled synthetic intake, and local dashboard/browser-check artifacts. FR-020 has local code and generated migrations but still needs PostgreSQL correction/restart/retention proof. FR-021 through FR-024 have local SQL/dashboard work but require database execution, datasource mapping, and rendered query verification. FR-025 and FR-026 alert rules remain unimplemented. FR-027 has a local k6 check and isolated intake but no provisioned five-minute check or staging proof. FR-028 has runbook content but no tested routing/recovery evidence. Success criteria SC-007 through SC-010 remain release gates; SC-001 through SC-006 have partial local unit/view coverage and have not been demonstrated end to end. The tasks checklist is the detailed status record. All changes remain uncommitted.
