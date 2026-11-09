# Research: Client Experience Observability

**Date**: 2026-11-08  
**Sources**: [feature spec](spec.md), [source design](../../docs/CLIENT_TOAST_OBSERVABILITY_DESIGN_PLAN.md), repository inspection, and linked primary documentation. Research resolves planning choices; deployed access and measurement thresholds remain release verification, not unanswered design questions.

## R1. Measurement boundary and catalog

**Decision**: Capture accepted current-generation outcomes at capability owners before notification policy. Use a fixed, versioned catalog for journey, action, state, reason, and the existing 26 notification issue codes. One accepted user attempt receives one ID and one terminal result; internal retries stay within it. A separately committed toast receives one presentation event.

**Rationale**: `LiveDeliveryState`, route and map readiness, audio health, and settings effect owners already distinguish evidence. `NotificationCoordinator.PresentationCommitted` fires after the presenter succeeds and current state is rechecked. That hook reports actual presentation, not underlying health. A healthy or inline-failing session can have zero toasts. Keep direct attempt/episode associations when known and avoid timing-based causation.

**Alternatives considered**: Count toast calls or displayed warnings as health; rejected because suppression and local feedback alter counts. Capture all browser exceptions; rejected because exceptions do not establish user impact and may expose raw data.

## R2. Browser lifetime and early start

**Decision**: A small pre-application bootstrap assigns one page-session ID and bounded start event. A single application-lifetime reporter accepts typed owner events, a city-activation ID, monotonic elapsed time, and the bootstrap handoff. Reporters subscribe once and dispose explicitly; capture errors are swallowed into bounded diagnostics.

**Rationale**: The current application starts from `Client.WebApp/wwwroot/index.html`, while the owners span Client.Core and Client.Shared. A passive abstraction in Core and stable DTOs in Shared preserve dependency direction. The first failed application load can still be reported after the bootstrap script starts. A failure before that script needs an independent synthetic check.

**Repository constraint**: `Client.WebApp/Program.cs` registers singleton application services, while `App.razor` starts initialization. Register the reporter and bootstrap handoff before `App` initializes, without resolving a scoped component graph from the root service provider. `Blazored.LocalStorage` is present but cannot supply the proposed atomic multi-tab outbox.

**Alternatives considered**: Subscribe from the toast host; rejected because host remount or failure would lose owner telemetry. Use a global exception handler as bootstrap success/failure; rejected because arbitrary exceptions do not establish startup outcome.

## R3. Eligible time and observation gaps

**Decision**: Use accepted state transitions and one foreground scheduler to write at most one non-overlapping observation window per minute, plus partial windows on scope/visibility changes. Track eligible, excluded, and uncertain-gap durations separately. A callback delayed beyond the initial 15-second suspension tolerance closes observation rather than extending the prior state. Initial deadlines are 60/30/15 seconds of eligible foreground time by journey; late confirmed readiness becomes recovery, not a second terminal result.

**Rationale**: A state transition alone cannot prove that an hour of active use followed. Delayed timers and page suspension make continuous interpolation misleading. Stop/unlock action eligibility differs from subsequent health eligibility. Window boundaries and sequence allow duplicate and overlap detection.

**Alternatives considered**: Derive duration from state-change timestamps alone; rejected because silent tabs and abrupt termination would manufacture time. Report one event per capability per frame; rejected for cost and coupling.

## R4. Delivery contract

**Decision**: Use a bounded native browser outbox shared by bootstrap and application capture: initially 1,000 events or 4 MiB per origin, seven-day age limit, 20 events or 64 KiB per batch, fixed IDs, acknowledgement-based deletion, and bounded memory fallback. Retry network/429/5xx failures with backoff and `Retry-After`; invalid events are removed with a loss/quality count. Do not preferentially retain failures.

**Rationale**: Multiple tabs and reloads must preserve original scope and event identity; per-event transactional writes prevent one tab from replacing another tab's queue. The browser's IndexedDB API has transactional semantics and allows one read/write transaction on a store at a time, subject to blocked upgrades across open tabs ([MDN IndexedDB guide](https://developer.mozilla.org/en-US/docs/Web/API/IndexedDB_API/Using_IndexedDB)). Acknowledged replay is at least once in transport and once in durable totals within retention; visitor exit or cleared storage remains a stated coverage limit.

**Alternatives considered**: Unload-only send, in-memory-only queue, or fire-and-forget posting; rejected because full city navigation and offline periods lose observations. Retain failures before successes; rejected because it biases success rates.

## R5. Server intake and durable identity

**Decision**: Add one anonymous, size-limited batch endpoint with typed versioned payloads and per-event validation. Commit valid events in one transaction using event UUID as an immutable unique key; acknowledge only durably stored or already stored IDs. Return safe per-event rejections for invalid members, 400/413/429/503 for envelope, size, rate, and persistence failures, and `Cache-Control: no-store`. Scope duplicate handling to event ID; reject or quarantine conflicting logical attempt/window records from derived metrics.

**Rationale**: The existing WebAPI uses endpoint groups, `ApiEndpoints`, `RegisterDataServices`, `IDbContextFactory<AppDbContext>`, and a rate-limiting middleware policy. PostgreSQL `ON CONFLICT` supports scoped duplicate handling; targeting the event key avoids masking unrelated constraints ([PostgreSQL INSERT](https://www.postgresql.org/docs/current/sql-insert.html)). The current Stats rate-limit rejection callback records Stats metrics for every rejected request, so it must become endpoint-aware before adding the client policy. Separate rate and concurrency limits protect an anonymous endpoint without a browser secret ([Microsoft ASP.NET Core rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)).

**Repository constraint**: Existing endpoint constants map at root, so define the full `/api/client-experience/batch` route explicitly and test whether the deployed proxy preserves it. Current `Program.cs` CORS origins are local only. Telemetry defaults disabled when no database is configured and fails configuration validation if enabled without `TransitJazzDB`.

**Alternatives considered**: A client API key; rejected because a secret cannot live in WebAssembly. Acknowledge before commit; rejected because a failed transaction would permanently lose events. Reject a result before its start; rejected because delivery order may reverse.

## R6. Experience store, cohorts, and retention

**Decision**: Keep append-only `client_experience_events` raw observations, reviewed SQL views for correlated attempts/windows/episodes/presentations, and `client_experience_daily` aggregates. Cohort journey outcomes by matched attempt start, preserving succeeded, failed, deadline, unresolved, and excluded counts. Use server receipt time for operational cohorts, separate trusted occurrence time for labeled historical views, and do not mix measurement versions. Recompute affected daily buckets atomically after late arrivals, track a watermark, and delete 30-day detail only after aggregate coverage; retain daily trends 13 months.

**Rationale**: Result-arrival buckets would shift a failure to the wrong denominator. Distinct event UUIDs do not by themselves prevent two logical results or overlapping windows from inflating totals. Counts and latency histogram buckets can be combined across days; percentages, percentiles, and distinct daily sessions cannot be summed. Existing Server.Data migrations and statistics stores provide the persistence home.

**Alternatives considered**: Derive all dashboards from raw data forever; rejected due bounded detail retention and query cost. Increment daily totals on every incoming event; rejected because late results and duplicate logical records require correction. Use client-supplied environment or timestamp as operational authority; rejected because the browser is untrusted and may be skewed.

## R7. Operator views and alerts

**Decision**: Use the existing PostgreSQL Grafana source for reviewed experience views and daily aggregates, and existing server telemetry for collection health. Separate experience, feedback, and collection panels. SQL alert rules return a time column and numeric series with fixed environment/city/journey labels; configured city series remain present with explicit denominator/coverage values. Validate the documented data-source UID and grants at deployment. Do not use dashboard variables in alert queries.

**Rationale**: Grafana's PostgreSQL data source supports alerting with time-series results ([Grafana PostgreSQL data source](https://grafana.com/docs/grafana/latest/datasources/postgres/)); the time-series query must contain a sorted `time` column ([query editor](https://grafana.com/docs/grafana/latest/datasources/postgres/query-editor/)). Grafana may resolve a disappearing label series as `MissingSeries`, so no-data and missing-series behavior need explicit tests and collection-health routing ([Grafana missing data](https://grafana.com/docs/grafana/latest/alerting/guides/missing-data/)). A zero bad count with adequate denominator is healthy; zero denominator is unevaluated. Fresh urgent stop failures alert below normal traffic minimums; delayed backlog updates history only.

**Alternatives considered**: Treat zero toasts as healthy, use client telemetry alone as collection proof, or let empty queries return zero. Each can hide a real outage or collection failure.

## R8. Independent checks and rollout

**Decision**: Add a bounded ingestion/database probe without fake user events. Use the existing Grafana Cloud Synthetic Monitoring k6 browser check capability at a five-minute frequency for shell, city map, and confirmed live join; keep its script in the repository and verify account access and probe cost before provisioning. Route its client-experience requests to a dedicated server-controlled synthetic intake using a check-runner secret held outside the browser bundle, with real-session denominators excluding that environment. Exercise audio gesture and voices in supported browsers during staging/release. Deploy the server contract before emitting client versions, then enable capture gradually and baseline alert thresholds before operational routing.

**Rationale**: Browser telemetry cannot report a failure before its own bootstrap. The existing Grafana Cloud service supports scheduled k6 browser checks and 300-second frequency ([Grafana browser checks](https://grafana.com/docs/grafana-cloud/observe-and-act/testing/synthetic-monitoring/create-checks/checks/k6-browser/)). The k6 browser route API can override a matching request's URL and headers ([Grafana k6 route API](https://grafana.com/docs/k6/latest/javascript-api/k6-browser/route/continue/)), so the runner can direct telemetry to a server-authenticated synthetic path without adding a secret to application code. Verify that the configured probe channel supports this behavior and that the synthetic route is isolated. Quiet feeds and headless audio may not supply valid health evidence. The source plan's alert thresholds are provisional, and representative traffic/coverage must precede formal reliability objectives.

**Alternatives considered**: Browser-supplied `synthetic` flag; rejected because arbitrary clients can set it. A scheduled GitHub Actions browser job; rejected as the primary five-minute monitor because scheduled runs can be delayed or dropped ([GitHub Actions troubleshooting](https://docs.github.com/en/actions/how-tos/troubleshoot-workflows)). Interpret a quiet production feed or headless audio failure as an outage; rejected because neither proves user impact.

## R9. Constitution and scope

**Decision**: Extend the existing three deployable units and existing database/monitoring stack. Use Core-facing passive capture, Shared wire DTOs, existing resource file if any visitor text is added, and structured server logs/metrics with bounded labels. Keep map/audio behavior, route identity, overlay timing, and dark-mode presentation unchanged. No new runtime project, vendor, worker broadcast, or browser secret.

**Rationale**: This meets Constitution v3.3.2 principles I, II, IV, V, VI, VII-XIII. The existing English/Spanish UI gap noted in feature 061 remains outside this observability feature; no new visitor-facing UI is planned. The repository's `AGENTS.md` forbids commits, including optional Spec Kit hooks.

**Alternatives considered**: New telemetry vendor or cross-service event bus; rejected as unnecessary to meet the spec and a needless new deployment surface.

## Verification remaining for implementation

The research resolves design choices. Release verification must check production CORS/proxy routing, bootstrap configuration, database grants, realistic successful-session volume and query plans, multi-tab browser behavior, supported-browser audio evidence, synthetic isolation, Grafana contact-point delivery, and measured client overhead. These are explicit gates, not speculative success claims.
