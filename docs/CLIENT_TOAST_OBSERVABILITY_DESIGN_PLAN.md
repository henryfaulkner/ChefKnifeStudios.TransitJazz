# Client experience and health observability

Created: 2026-11-07. Expanded: 2026-11-08. Status: proposed implementation plan. This change is documentation only. Keep this filename so existing references continue to resolve; the proposed endpoint, table, and dashboard names below supersede the original notification-only names.

## Outcome and scope

Measure whether visitors can open a city, see usable transit data, start listening, continue using the experience, and successfully change or stop it. Persist bounded client observations in the existing server-side PostgreSQL database and expose success rates, readiness latency, degraded time, recovery, and notification presentations in Grafana. Capture qualifying reliability events without sampling, including successful outcomes and eligible attempts from the first release.

The primary monitoring boundary is the confirmed outcome at the capability owner, before notification suppression and presentation. Toast observations remain a supporting signal for the feedback the application presented. Fewer toasts alone must never be interpreted as better health: grouping, grace periods, suppression, host failure, and copy/policy changes can change presentations without changing the underlying experience.

Use three complementary views:

1. **User journeys:** attempts, confirmed success/failure, and time to readiness for map startup, live delivery, audio unlock/resume, and explicit settings/stop actions.
2. **Capability health:** requested/eligible state, confirmed degradation, observed duration, and recovery for routes, map, live delivery, and audio.
3. **Feedback and collection:** committed toasts, bounded fatal-runtime signals, telemetry delivery/loss, and independent synthetic checks.

This follows the principle of defining reliability around critical user journeys and measurable successful outcomes. The concrete definitions and thresholds below are TransitJazz design choices, not claims about current production performance. [Google SRE: Implementing SLOs](https://sre.google/workbook/implementing-slos/)

A **presentation** is one successfully accepted, current-scope toast after the notification coordinator's suppression, grouping, and stale-result checks. This is evidence that the application presented a notification, not proof that a person read it. A combined toast is one presentation with multiple associated issue codes. A later warning-to-error escalation or re-armed recurrence is another presentation. Suppressed incidents, failed/stale presentations, success toasts, and inline-only feedback do not count.

Persist the full bounded classification of each toast: severity, participating and explained issue codes, capability, safe message/action resource keys, city, and release identifier. Inline-only failures, especially audio unlock, produce journey outcomes even though they produce no toast event. Raw exceptions remain diagnostic logs at their existing owners; this is not a general browser exception collector or session replay system. The existing 26 notification issue codes are useful classifications, but do not define the entire health catalog. Conditional notifications are captured whenever enabled and actually presented; health capture depends on verified evidence, independently of notification feature flags.

No per-note, per-vehicle, per-frame telemetry, durable visitor identity, clickstream, or new observability vendor is required. Keep collection passive and bounded. This plan does not change playback, notification timing, route identity, or background execution policy.

## Measurement definitions

### Journeys and evidence

| Journey / capability | Eligible attempt or observation | Confirmed success / health evidence | Failure and interpretation |
| --- | --- | --- | --- |
| Application bootstrap | One document start observed by a small script before WASM startup | Application reports that initialization reached its usable shell | Explicit bootstrap failure or observed readiness deadline; documents that never execute the script remain outside browser coverage |
| City map startup | One current-city activation; route and map loading requested | Current-city usable route geometry plus map initialization and required domain layers confirmed | Owner failure or readiness deadline; a loading spinner, HTTP 200, or isolated tile error does not establish readiness or total map failure |
| Live delivery start/resume | One explicit delivery activation; automatic internal connection retries stay within that attempt | Connected transport AND confirmed current-city membership while delivery is requested | Start/join/version/resume failure or readiness deadline; connection alone is insufficient |
| Live data freshness | Foreground, delivery requested, with evidence that updates are expected | Valid current-city batch accepted/processed; retain coarse ages and processing outcomes | Missing batches alone mean unknown freshness until a source cadence/heartbeat contract exists; a quiet feed is not an outage |
| Audio unlock/resume | One accepted user gesture attempt; duplicate callbacks share one attempt | Native audio context confirmed running for that attempt | Inline failure, suspended result, or readiness deadline; continuing muted is an explicit exclusion, not a successful unlock |
| Requested audio health | Foreground and playback requested after unlock | Context running and required voice resources confirmed ready | Required voice/engine failure or confirmed suspension; partial voice availability is degraded. Readiness is not proof that sound reached the listener's speakers |
| Settings / map style / audio filter | One explicit accepted change, classified by a fixed action code | Effect owner confirms applied state; persistence has a separate outcome if applicable | Failed effect, failed compensation, or session-only persistence. No raw setting values or route selections are collected |
| Stop audio | One explicit stop/mute request | Owner confirms stop/master mute took effect | Confirmed still-playing failure is urgent even if no toast is shown; missing confirmation is unknown/deadline-exceeded, not proof of continued sound |
| City navigation | Source action accepted and, when observable, target city activation | Target reaches the city map startup outcome | Source navigation error or target startup failure; do not claim an end-to-end navigation success from assigning a URL |

Use the existing full-reload behavior to start a new page session on city navigation. Initial delivery measures source navigation outcomes and destination startups separately; it does not infer a cross-document journey without an explicit correlation contract. No route/vehicle IDs enter the telemetry contract.

### Attempts, sessions, eligibility, and recovery

- A **page session** is a random UUID per document load. A **city activation** has its own UUID within that session, even if future navigation becomes in-place. Multiple tabs and reloads are separate sessions, never unique people. A bounded context event records foreground/background and requested delivery/audio changes.
- An **attempt** has one UUID generated at its accepted start, one journey code, and at most one terminal result: `succeeded`, `failed`, `deadline_exceeded`, `cancelled`, or `superseded`. Automatic retries do not create new attempts; a fresh accepted user retry does. Preserve current-scope/generation checks before accepting any result. Duplicate start/result callbacks must reuse their original event IDs.
- Bound attempt correlation to 24 hours of wall time. A resumed page beyond that horizon closes the old observation as superseded/observation-expired and starts fresh measurement only when eligible. Expired or missing observations do not establish product failure; results arriving beyond the supported correlation horizon remain diagnostics.
- Initial measurement deadlines: 60 foreground seconds for bootstrap/city startup, 30 for live start/resume, 15 for audio unlock/resume, and 15 for settings/stop actions. These are configurable measurement budgets, not new product timeouts or claims of failure causes. A deadline result ends that measurement attempt; later confirmed readiness is a capability recovery, not a second successful result for the same attempt.
- Use monotonic elapsed time, separate from client UTC. Foreground eligibility excludes hidden/suspended intervals and deliberately paused delivery; audio eligibility also requires requested, unmuted playback. Record deliberate exclusion reasons. At foreground return, reassess current owner state before restarting eligible time. Do not interpret a delayed browser timer as continuous foreground execution.
- Apply those requested-state exclusions to ongoing capability health, not to the action trying to change that state: a stop attempt stays eligible when mute is requested, and an unlock attempt stays eligible while the context is still locked. A modal may block a toast without excluding otherwise active capability observations. Continuing muted before an unlock result cancels that attempt; doing so after a recorded failure does not erase the failure.
- For settings, use distinct fixed action codes for effect application and persistence, with a shared parent action UUID. Start persistence measurement only after the effect actually applied and a save was requested. This preserves one terminal result per attempt while exposing session-only changes without counting them as failed effects.
- A capability is `pending`, `healthy`, `degraded`, `unavailable`, `unknown`, or `not_requested`. State changes carry a bounded reason code. Only new evidence establishes health or recovery; dismissal, toast expiry, an online event, scope disposal, and absence of errors do not.
- Recovery latency is observed eligible time from confirmed degradation to confirmed healthy state within the same city/capability episode. A scope ending or observation gap leaves an incomplete episode; report it separately instead of inventing recovery or counting time after observation stopped.
- Register capture before operations start. An initial state snapshot after late registration is marked as such; it cannot invent an attempt start or elapsed history. The small bootstrap script creates the page ID and buffered start event, then hands ownership to the application reporter without duplication.

### Denominators and time windows

Primary journey reports use **attempt-start cohorts**, joined by attempt UUID, environment, page session, and city activation. Never divide results received this minute by starts received this minute. Use the start's server receipt time for operational cohorts and trusted client start time for explicitly labeled historical experience views. Results received before their start are retained and reconciled when the start arrives; unmatched results are coverage diagnostics and excluded from primary rates.

For a cohort, let `S` be succeeded, `F` failed, `D` deadline-exceeded, `U` unresolved, and `X` cancelled/superseded attempts. The eligible denominator is `S + F + D + U`; show `X` separately. Show success share `S / denominator`, confirmed bad share `(F + D) / denominator`, and unresolved share `U / denominator` together. A resolved-attempt success rate `S / (S + F + D)` may also be shown, clearly labeled, but cannot hide unresolved attempts. Missing terminal events remain unknown; server aging alone must not turn them into confirmed failures. Label recent cohorts provisional and preserve late corrections within the outbox horizon.

For time-based health, show `healthy_ms / eligible_observed_ms`, with degraded, unavailable, pending, and unknown portions alongside it. Requested-state and visibility exclusions are outside that denominator. Collection gaps are a separate coverage measure, not healthy time. Do not sum capability times into a single app availability percentage because routes, map, live, and audio overlap. Show both attempt-weighted outcomes and affected page-session counts so repeated retries by one session are visible.

Rates with a zero denominator are `N/A`, never 100% healthy. Counts, sample size, collection coverage, measurement version, and release must accompany comparisons. These metrics describe observed browser attempts and time, not all visitors. Initial alerts use provisional thresholds; choose formal reliability objectives after at least two representative weeks of traffic and coverage review.

## Repository findings

- `SignalRNotificationService.DeliveryStateChanged` and `LiveDeliveryState.IsReady` distinguish connected transport, desired delivery, and confirmed city membership. `ApplicationViewModel.RouteLoadOutcome` distinguishes pending, usable, unavailable, and cancelled routes.
- `MapReadinessState`, `AudioUnlockAttemptState`, and `AudioCapabilityHealthTracker` already model coarse browser outcomes. Capture where their owners accept current-generation outcomes, before `CoreCapabilityNotificationOwner` applies notification-specific gating. Existing state classes are seams, not evidence that telemetry hooks or all desired measurements already exist.
- `Client.WebApp/wwwroot/index.html` currently starts Blazor and includes Umami. Add minimal early bootstrap measurement using the same page-session contract; do not use Umami page counts as this pipeline's denominator because capture coverage and identity differ.
- `NotificationCoordinator.EvaluateAsync` calls `NotifyPresentationCommitted` after the presenter succeeds and the coordinator revalidates state. `PresentationCommitted` currently supplies a local presentation ID and `NotificationSummary`.
- `NotificationSummary` contains severity, capabilities, issue codes, explained issue codes, resource keys, and offline qualification. Its payload lacks an immutable city/time snapshot, so add that at commit time rather than consulting current navigation state later.
- `ToastService.PresentAsync` checks that MatBlazor accepted the toast. `NotificationHost` subscribes to committed presentations for reading time and announcements. Capture should have application lifetime and register before presentation can begin, independent of host remounts.
- Legacy `IToastService.ShowWarning/ShowError` methods still exist, but the production source search found no callers. Remove these unused warning/error entry points and update test doubles as part of implementation so future warning/error toasts must enter the typed coordinator path. Inventory direct `IMatToaster` usage again before release.
- WebAPI already uses `RegisterDataServices`, `IDbContextFactory<AppDbContext>`, EF Core/Npgsql migrations, JSON console logging, and OpenTelemetry metrics. Database registration is conditional today; telemetry enabled without `TransitJazzDB` must fail configuration validation.
- `observability/grafana/insights/README.md` documents an existing PostgreSQL source (`cg086lcj6z6yob`); worker dashboards and alert exports already live under `observability/grafana/`. This is repository evidence, not a fresh check of deployed access or grants.
- `StatsRateLimitingExtensions` installs a global rejection callback that records Stats metrics. Adding another policy must make that callback endpoint-aware, so experience-event rejections do not become Stats requests.

## Architecture

```text
Bootstrap + capability owners accept current-scope outcomes
  -> attempt results, state transitions, bounded observation windows
NotificationCoordinator commits a presentation -> supporting toast event
  -> immutable typed events -> application-lifetime reporter
  -> bounded browser outbox -> POST /api/client-experience/batch
  -> validate + idempotent PostgreSQL transaction -> acknowledge event IDs
  -> raw events + daily aggregates -> Grafana dashboard and managed alerts

Ingestion failures/latency -> existing server OpenTelemetry pipeline
Independent browser synthetic + ingestion probe -> collection/startup checks
```

Use PostgreSQL records as the authoritative source for client experience totals and alerts. Derive daily aggregates from persisted events, not a parallel client metrics write. Use existing OpenTelemetry for ingestion and aggregate-maintenance health. No message broker, general event bus, new runtime project, or public event-history endpoint is needed. Grafana supports PostgreSQL alert queries in time-series format. [Grafana PostgreSQL query editor](https://grafana.com/docs/grafana/latest/datasources/postgres/query-editor/)

## 1. Capture contract

Use a small fixed event catalog with typed payloads. Every event has an immutable UUID generated when the observation is accepted, before enqueueing. No arbitrary event names, property bags, or exception payloads.

| Event kind | Required contents and ownership |
| --- | --- |
| `session_started` | Once per page, emitted by early bootstrap; shares page ID with WASM |
| `context_changed` | City activation and bounded visibility/requested-state snapshot; accepted context transitions only |
| `attempt_started` | Attempt UUID, journey/action code, monotonic start offset, eligibility, configured deadline |
| `attempt_finished` | Same attempt identity, terminal result, safe reason, elapsed foreground/wall durations; immutable once emitted |
| `capability_changed` | Capability, previous/current state, bounded cause, episode UUID and accepted operation generation; initial snapshots explicitly marked |
| `observation_window` | Non-overlapping observed interval, durations by capability/state/eligibility, fixed coarse counters and freshness ages; one payload for all capabilities |
| `toast_presented` | Immutable committed summary, severity, issue/explained codes, resource keys, and bounded associated episode IDs where the owner relationship is known |
| `runtime_failed` | Fixed category such as bootstrap failure or observed Blazor fatal UI, once per category per activation; no raw message/stack |
| `performance_summary` | Optional later phase: bounded supported performance metrics and measurement identity; absence means unsupported/unobserved |

Subscribe once to authoritative owner outcomes and suppress identical state reports before emission. This is separate from toast deduplication: repeated failed attempts remain distinct, while repeated reports of the same unchanged capability do not. Preserve explicit associations between attempts, episodes, and toasts; never infer causation solely from temporal proximity. Multiple capability episodes can share a cause; capability breakdowns overlap and are not independent incident totals.

For `toast_presented`, snapshot city, UTC time, presentation ID, and copied summary arrays under the coordinator's state lock, then invoke the reporter outside the lock. Keep presentation IDs local; event UUIDs identify retries. Suppressed/stale/failed presentations still produce no toast event, even when the underlying owner outcome was recorded.

Register one reporter before capability operations and host presentation, with explicit startup/disposal and no duplicate subscriptions. A reporting/storage failure must never block rendering, escape into application behavior, or create a toast. Use a dedicated HTTP client path without user-facing failure reporting and without depending on working SignalR or a successful protocol handshake. Early bootstrap must be able to buffer/report a bounded failure without .NET; if that script also fails, coverage relies on the independent synthetic.

The shared transport DTO belongs in `src/ChefKnifeStudios.TransitJazz.Shared`, with stable string wire codes, a schema version, and a separate measurement-definition version. Do not make WebAPI depend on `Client.Shared`, MatBlazor, or localization assemblies. Define an explicit shared telemetry catalog/mapping covering owner outcomes and all notification issue codes. Unknown future codes require a compatible server rollout before the emitting client. Do not compare changed eligibility/deadline definitions across measurement versions as if they were identical.

| Field | Meaning / proposed constraint |
| --- | --- |
| `schemaVersion` | Integer, initially `1` |
| `measurementVersion` / `eventKind` | Versioned measurement definitions and fixed event discriminator |
| `eventId` | UUID, immutable across every attempt |
| `pageSessionId` | Random UUID per page load; retained inside queued events; no durable user identity |
| `cityActivationId` / `sequence` | UUID for current activation, nullable only before city scope; monotonic per-page sequence for ordering/gap diagnostics |
| `occurredAtUtc` / `pageElapsedMs` | Client event time and bounded monotonic page offset; preserve separately from server receipt time |
| `citySlug` | Registered city slug or bounded `unknown` for missing scope |
| `attemptId` / `episodeId` | Kind-specific UUIDs, never metric labels; correlation restricted to matching session/activation |
| `capability` / `journey` / `reason` | Kind-specific fixed catalog values; no notification severity applied to successful outcomes |
| `severity` / `summaryKind` | Toast only: displayed warning/error and summary classification |
| `issueCodes` / `explainedIssueCodes` | Toast summary arrays; union bounded by the fixed catalog |
| `impactKey` / `actionKey` | Toast only: allowlisted resource keys, not rendered text |
| `offlineQualified` | Toast qualifier; browser online state remains a hint in context events |
| `clientRelease` | Build-supplied release identifier, at most 64 characters; `unknown` if unavailable |

Server derives capability classifications from code mappings. Preserve overall displayed severity; do not mislabel every child issue of a combined error toast as an independent error. Environment is supplied by server configuration, never trusted from the browser. Store no coordinates, route selections, full URLs, IP addresses, access tokens, raw exception strings, or raw user-agent values in event rows.

### Bounded duration and activity observations

State transitions alone cannot tell whether a tab remained active for another hour. Emit one observation window at most every 60 seconds while foreground, plus a partial window on city/visibility changes or explicit orderly shutdown. Window payloads contain all fixed capabilities, with each capability's duration partitioned into eligible states and excluded time. Accumulate locally on accepted transitions; do not send a separate duration event per capability or per frame. Flush a partial window before city attribution changes, and never overlap windows.

Use one lightweight scheduler, initially every 5 seconds while foreground, to establish observed progress and drive measurement deadlines. When callbacks are delayed beyond the configured suspension tolerance (initially 15 seconds), mark the uncertain gap unobserved instead of filling it with the previous healthy/degraded state or advancing an eligible-time deadline through it. Observation windows carry observed and gap durations separately. On resume, discard stale evidence and re-read owners. Window sequence and boundaries support overlap/gap validation; only validated non-overlapping windows contribute to duration aggregates. Early page termination can lose the final partial window, which is an explicit coverage limit.

Coarse live counters can record accepted/failed batches and the age of the last valid batch; synth counters may record scheduling successes/failures within the window when existing hooks can provide them cheaply. These counters do not prove audible playback or expected transit activity. No new per-frame or per-note .NET callbacks. Until an explicit source heartbeat/cadence and valid city attribution exist, freshness remains diagnostic and is excluded from availability alerts.

### Performance and fatal failures

First delivery includes application-specific readiness durations and bounded bootstrap/fatal-state observations. General `error` or `unhandledrejection` callbacks are not automatically fatal application failures; only an observed fatal state or a classified owner outcome can establish that impact. Do not forward arbitrary exception text.

A later, separately enabled performance slice may add LCP, INP, and CLS with a standards-conformant maintained collector after dependency review. These complement application readiness and do not establish map/audio health. Capture support and sample coverage; avoid inventing simplified metrics under standard names. Use fixed histogram boundaries and bounded final/update semantics per page/metric so repeated metric updates do not count as separate sessions. [Web Vitals guidance](https://web.dev/articles/vitals)

## 2. Browser delivery and reliability

Use a small native IndexedDB outbox shared by early bootstrap and the JS interop adapter. Start with configurable defaults: 1,000 events / 4 MiB per origin, 7-day maximum age, and 20 events / 64 KiB per batch. Re-budget against successful-session and observation-window traffic, not historical toast volume. Use atomic per-event writes/deletes so multiple tabs cannot overwrite each other's queues; duplicate sends are safe. Do not hold database transactions open across network requests.

- Enqueue promptly, persist asynchronously, and send the first batch immediately while online. One sender per page; coalesce additional events for up to 2 seconds.
- Acknowledge only IDs the server reports as durably stored or already stored. Delete only those IDs. A lost response retains the same events and UUIDs.
- On network errors, timeout, `429`, or `5xx`, retry with jittered exponential backoff from 2 seconds up to 5 minutes. Honor `Retry-After`. Bound each request to 10 seconds. Retry also on startup, online, and foreground transitions; `navigator.onLine` is only a hint.
- Retain work through full city navigation/reload by persisting under the original city and page-session IDs. Deletion from one tab must never remove newly enqueued events from another.
- Return per-event permanent rejections for valid batch envelopes; remove those events and record bounded local diagnostics so one invalid event cannot poison the queue. Split batches on `413`; treat an unsupported schema as a permanent rejection. Do not spin on persistent configuration/CORS failures.
- If IndexedDB is unavailable, fall back to the bounded memory queue. On age/capacity limits, discard oldest events with loss counts by fixed event kind. Carry bounded loss-count summaries on subsequent successful requests and expose them as ingestion-health telemetry, separately from experience counts. No preferential retention of failures over successes: that would bias outcome rates. Loss reporting remains best effort and is not a complete loss audit.
- No unload request is required for correctness. A best-effort flush must not delete an unacknowledged event; a persisted event can be retried at the next visit.

Delivery guarantee: at-least-once delivery attempts with one persisted row per acknowledged event UUID within retention. No sampling during normal operation. Absolute capture is impossible if the browser never initializes telemetry, closes before its local write, storage is unavailable/cleared, queue limits are exceeded, or the visitor never returns after an outage. Keep these limits explicit on the dashboard/runbook; receipt counts are observed reports, not a complete census of users. Event-sequence gaps and unmatched attempts/windows are additional coverage indicators, never proof of why an observation is missing.

## 3. Server endpoint and persistence

Add `POST /api/client-experience/batch` using the existing minimal endpoint-group convention and shared `ApiEndpoints` constants. This anonymous browser endpoint must not require a secret shipped in WASM. Use the configured application API base URL, available to the early bootstrap sender as non-secret build configuration; verify production CORS/proxy routing for the deployed web origin as part of rollout.

Enforce JSON body size even without `Content-Length`, maximum batch length, UUID validity, bounded string lengths, enum/catalog membership, and unique bounded issue arrays. Validate payload shape per event kind, allowed transitions, nonnegative bounded counters/durations, and capability-duration sums against observed windows. Missing causal events can arrive later; do not reject a result merely because its start has not arrived. Conflicting logical results or overlapping windows are quarantined from derived metrics and counted as quality errors, rather than arbitrarily selected as successful.

Retain valid client times but mark skewed times (future by more than 5 minutes or older than the 7-day outbox horizon) as untrusted. Default operational event counts use receipt time; attempt rates use joined start cohorts, and durations use validated monotonic measurements. Apply request timeout, bounded database concurrency, a dedicated per-source rate policy, and a global ingest ceiling. Initial proposed limits: 30 batches/minute/source with a 10-request burst and 100 batches/second globally per replica, tuned against staging load/shared-NAT traffic including successful sessions. Document replica multiplication; use edge limits if a deployment-wide ceiling is required. Trust forwarded IP headers only from configured proxies, and never use caller-supplied session IDs as the only limit.

Malformed envelopes return `400`; oversized requests `413`; rate limits `429` plus `Retry-After`; persistence failures `503` with no success acknowledgement. A successful `200` response contains acknowledged IDs and bounded rejected ID/reason pairs. Validate individual events, commit all accepted events in one transaction, then acknowledge. On transaction failure acknowledge none. Return `Cache-Control: no-store` for every response. Do not echo event payloads in errors.

Add an append-only raw table, `client_experience_events`, through the existing EF migration project:

- `event_id uuid` primary key; `page_session_id uuid`; `occurred_at_utc timestamptz`; server-assigned `received_at_utc timestamptz`; `clock_skewed boolean`.
- Common columns: `environment`, `city_slug`, `city_activation_id`, `client_release`, `schema_version`, `measurement_version`, `event_kind`, sequence, and monotonic page offset.
- Nullable typed query columns for `attempt_id`, `episode_id`, `journey`, `capability`, `outcome`, and toast severity. Store kind-specific details in bounded, schema-validated JSONB, with fixed issue arrays for toasts; no arbitrary attributes. Expose reviewed SQL views for attempts, windows, episodes, and presentations so dashboard queries share one interpretation.
- Index `received_at_utc`, `(environment, city_slug, event_kind, received_at_utc)`, and a partial correlation index on session/activation/attempt for attempt events. Add other indexes only from measured query plans. Use explicit logical deduplication in the views for attempt identity, window sequence, and episode transitions, in addition to transport UUID deduplication.
- Insert with parameterized `ON CONFLICT (event_id) DO NOTHING`; retain the original row on replay. Scope conflict handling specifically to the event primary key. Concurrent identical retries are acknowledged after the conflicting insert resolves; they never overwrite trusted receipt metadata. [PostgreSQL INSERT documentation](https://www.postgresql.org/docs/current/sql-insert.html)

### Long-term aggregates and retention

Keep raw events for 30 days by server receipt time and daily aggregates for 13 months, both configurable. Add `client_experience_daily` for fixed aggregate rows partitioned logically by UTC day, environment, city, release, measurement version, and metric/journey/capability. This is a small derived table, not a new analytics platform or database partitioning scheme.

Store attempt starts and terminal/excluded/unresolved counts, duration sums, fixed latency-histogram buckets, toast counts, coverage diagnostics, and daily affected/observed session counts. Calculate multi-day rates from summed numerators and denominators; never average daily percentages or percentiles. Histograms permit approximate combined percentiles; label them approximate. Daily distinct page sessions cannot be added to obtain distinct sessions over a month, nor summed across overlapping capabilities/cities to obtain a global unique total. Historical dashboards must label those as session-days or show daily values.

Use attempt-start receipt day for operational outcome aggregates, and window-end receipt day for operational duration aggregates; those duration charts describe received observations and can shift during replay. Provide a separately keyed experience-time basis for trusted occurrence timestamps if historical timing is required, and never mix the bases. Assign duration windows to their observed end day and label the resulting boundary approximation (normally at most one 60-second window); do not pretend duration totals reveal the ordering of states within a window. Keep skewed observations in receipt views with a quality flag. Compare releases only within the same measurement version and time basis.

An hourly cancellable job recomputes and atomically replaces affected daily rows from raw events, including zero/reclassified rows, under a PostgreSQL advisory lock. Recompute the recent 10-day correction window plus buckets touched by newly received correlated events; starts and results must be joined before bucketing. This accommodates the 24-hour attempt bound plus 7-day delivery horizon. Track a persisted processing watermark, job version, last success, and lag. After downtime, resume from that watermark across all still-retained raw data, not just the usual correction window. Aggregation retries must never increment totals twice.

Finalization requires elapsed correction horizon and no unprocessed data affecting the bucket. Late/out-of-contract observations remain diagnostics instead of silently rewriting finalized history. Raw cleanup runs in bounded hourly batches only after the aggregate watermark covers the deletion range; alert if maintenance lag prevents deletion. Cleanup and aggregation coordinate across replicas. Confirm storage/query volume after the first week. Detailed session/event investigations expire with raw retention; long-term trends remain available without event/session IDs in the aggregate table.

Require a read-only Grafana role with SELECT access to reviewed views/aggregate tables. Existing data-source connectivity does not imply access to new objects. Keep credentials in server/Grafana configuration. Browser reports are untrusted observations: validation and rate limits reduce abuse but cannot establish that a genuine visitor performed a submitted action or saw a submitted notification.

## 4. Grafana dashboard

Deliver `observability/grafana/dashboards/transitjazz-client-experience.json`, using a configurable PostgreSQL data-source UID. Verify the documented source at deployment rather than hardcoding an assumption that its grants still work. Use UTC timestamps and environment/city/release/measurement-version filters, with journey/capability and toast-severity filters where appropriate. Escape variables through Grafana's SQL formatting; identifier choices must be fixed. Separate experience health, feedback, and collection sections within this dashboard.

| Panel | Calculation and interpretation |
| --- | --- |
| Startup and audio-start outcomes | Starts, success/bad/unresolved shares, exclusions, and sample sizes for matched start cohorts; show provisional status |
| Time to readiness | p50/p95 foreground elapsed time for successful attempts, deadline/failure counts beside it; retain wall time for diagnosis so slow failures cannot disappear into success-only latency |
| Requested capability health | Healthy/degraded/unavailable/pending/unknown observed seconds, excluded time, and observation gaps; per capability |
| Recovery | Eligible recovery duration distribution, recovered episode count, and incomplete episodes; no inferred recovery on session disappearance |
| Action reliability | Settings effects and persistence outcomes, audio resume/stop, and source navigation outcomes with action-specific denominators |
| Live freshness diagnostics | Last-valid-batch ages and accepted/failed processing counts; distinguish unknown expectation from confirmed delivery failure |
| Affected page sessions | Distinct sessions with bad outcomes divided by observed eligible sessions for the same capability/cohort/filter; raw retention only for arbitrary multi-day distinct counts |
| Release / city comparison | Matched measurement definitions, rates, durations, sample sizes, unresolved share and delivery coverage; `unknown` remains an explicit city/release group |
| Long-term trend | Daily aggregates for up to 13 months; summed counts/durations, weighted rates, mergeable latency buckets; aggregate and time-basis labels |
| Warning/error presentations | Toast event counts by displayed severity, issue/capability and release; associated episodes where available; overlapping classifications cannot be summed |
| Feedback correlation | Owner episodes with linked presentations and known local/blocked contexts; no blanket expected-toast percentage because local feedback and suppression are intentional and unknown links remain unknown |
| Recent evidence | Attempt/episode timelines and toast metadata within raw retention; safe reason keys, city, release, event IDs |
| Delivery and data quality | Delay, skew, sequence gaps, unmatched results, conflicting outcomes/windows, reported local losses, and observed denominator coverage |
| Collection health | Ingestion errors/rejections/latency, probe and synthetic status, aggregate watermark/lag, and cleanup health via existing metrics |

Default event-volume panels to receipt time and journey panels to matched start cohorts. An outage backlog can create a receipt spike after recovery; pair volume with delivery delay. Historical occurrence views exclude skewed events and display their excluded count. Use zero-filled buckets for valid empty count queries; show rates with no eligible observations as `N/A`, and query failures as errors. Do not infer collection or application health from a lack of errors/toasts. Optional performance panels must show browser support/sample coverage and remain separate from domain readiness.

## 5. Alerts

Deliver `observability/grafana/alerts/transitjazz-client-experience-alerts.json` plus `docs/observability/client-experience-runbook.md`. Provision Grafana-managed SQL time-series rules with fixed windows and environment filters; alert queries cannot depend on dashboard template variables or panel transformations. PostgreSQL alerting requires time-series query format. [Grafana query editor](https://grafana.com/docs/grafana/latest/datasources/postgres/query-editor/)

Primary alerts come from owner outcomes and observed capability degradation, independently of toast presentation. The following are provisional defaults to exercise in staging and baseline before operational routing; they are not established SLOs. Group by environment/city and bounded journey/capability, not session, attempt, or release.

| Rule | Initial condition | Pending / evaluation |
| --- | --- | --- |
| Journey failure elevated | Confirmed bad share >=10% for one journey; >=20 eligible attempts and >=3 bad page sessions in a 15-minute start cohort ending 2 minutes ago | 3 minutes / every minute |
| Capability degradation sustained | Degraded + unavailable time >=10% of eligible observed time in 10 minutes; >=10 observed session-minutes and >=3 affected page sessions | 3 minutes / every minute |
| Audio stop failed | At least one fresh, confirmed owner outcome that audio remained playing after requested stop/master mute; no presentation required | Immediate / every minute |
| Bootstrap/city synthetic failing | Independent controlled browser cannot reach bootstrap/map readiness on two consecutive runs | Every 5 minutes; route runner failures separately |
| Telemetry ingestion failing | Persistence failures on at least 5 requests in 5 minutes | 2 minutes / every minute |
| Collection unavailable | Ingest API/database probe or Grafana query path fails | 2 minutes / every minute |
| Aggregate maintenance stalled | Watermark lag >2 hours or repeated cleanup failure | 10 minutes / every 5 minutes |
| Toast volume elevated (diagnostic) | >=10 errors across >=3 page sessions in 5 minutes, or >=30 warnings across >=5 sessions in 10 minutes | Dashboard/advisory initially; not primary paging |

For journey rules, unresolved share above 10% suppresses the rate-based health conclusion and emits a coverage advisory; it does not report healthy recovery. For duration rules, apply the same guard when reported unknown state plus observation gaps exceed 10% of the eligible observation span. A vanished page cannot report its gap, so this is a coverage guard for received observations, not proof of complete coverage. For both journey and duration rules, require a trustworthy recent observation set: initially non-skewed events with delivery delay <=2 minutes. Apply that freshness rule consistently to starts and results, keeping missing/non-fresh results unresolved rather than dropping their starts from the denominator. If fresh data is insufficient, report insufficient evidence and rely on independent probes/synthetics. Delayed backlog updates history and diagnostic counts but must not page as a new current outage. Fresh urgent stop failures still alert below traffic minimums; delayed stop failures remain visible as historical incidents.

Duration alerts use validated occurrence intervals fully contained in the evaluation window, not their receipt spike. Exclude partial boundary windows and report the resulting observed duration; aggregated state totals cannot be accurately trimmed without sub-window evidence. Exclude unknown/gap time from confirmed degradation while retaining it in coverage reporting. Missing freshness expectations never activate a live-data outage rule. Offline-qualified failures remain visible in experience outcomes; segment by observed browser/network context during investigation without pretending the online hint identifies root cause.

For each rule, return a timestamp and numeric series with bounded labels. Use configured cities plus `unknown`, LEFT JOIN count aggregates, and zero-fill valid counts. Return denominator and coverage series separately so a zero denominator cannot evaluate as a successful rate. Use database `now()` for evaluation; query definitions must share the reviewed views used by dashboards. An alert with insufficient traffic is explicitly unevaluated, not a healthy result. Formal SLO/error-budget and multi-window burn-rate rules follow only after targets and coverage are validated.

Treat a valid zero bad count with adequate denominator/coverage as healthy. An unexpected no-data result or SQL error goes to a collection-health notification, never silently becomes zero or a client-error incident. Configure Grafana missing-series/no-data behavior explicitly so loss of observations cannot emit a misleading recovery; an existing incident retains its last evaluation with a coverage annotation until enough data returns. Configure grouping by environment/city/rule, recovery notifications after an adequately observed healthy window, and a sensible repeat interval (initially 1 hour). Attach dashboard links and a runbook explaining late backlog, changing traffic, release/measurement changes, low volume, and telemetry outages. Reuse the operator's existing contact point/routing; actual delivery must be tested during rollout. No contact-point changes or notifications are sent during this planning task.

### Independent checks

Add a bounded ingestion health probe that exercises API availability and database access without inserting fake user events. Verify end-to-end browser capture/delivery with controlled staging journeys and a toast separately. Run a small browser synthetic every 5 minutes against the production page to verify shell startup, city routes/map readiness, and confirmed live join. It must check outcomes directly rather than rely solely on this telemetry pipeline. Separate synthetic infrastructure failure from application failure; production synthetic events use a dedicated server-controlled environment/ingestion route and are excluded from real-session metrics. A browser-supplied flag alone cannot establish trusted synthetic traffic.

Use a controlled feed fixture in staging to test live processing and rendering during otherwise quiet periods. Audio gesture/context/voice checks run in a supported browser matrix during staging/release verification; automated audio readiness is not a claim of audible output. Production quiet feeds and headless audio restrictions must not create false failures. Existing application/worker health remains useful for diagnosis but cannot prove browser startup or interaction success.

Instrument request outcomes (`accepted`, `rejected`, `rate_limited`, `store_failed`), request duration, accepted/duplicate event counts by fixed kind, quality/loss reports, aggregation watermark/lag, and cleanup with a dedicated Meter registered in WebAPI. Labels are fixed enums/environment, never event/session IDs, resource text, releases, or raw URLs. These metrics describe collection; persisted SQL rows remain the authority for experience counts. A server crash between DB commit and metric recording can affect operational counters without changing SQL totals.

## 6. Implementation sequence

1. **Measurement contract:** inventory owner evidence and document the exact starts, successes, failures, eligibility, exclusions, deadlines, and recovery for each journey. Define measurement version 1, fixed catalog, attempt/window reconciliation, and clock/late-data rules. Identify unavailable evidence explicitly; do not manufacture freshness or audible-output health.
2. **Contract and persistence:** add shared typed DTOs/options, raw and daily tables/configuration/migration, reviewed query views, transaction/deduplication behavior, aggregate maintenance and retention. Require storage when enabled. Review migration and read-only Grafana grants.
3. **Ingestion and browser delivery:** add endpoint/transport, limits, Stats callback isolation, diagnostics, metrics/probe, bounded IndexedDB sender, retry/acknowledgement semantics, and early-bootstrap handoff. Deploy the compatible server contract before emitting clients.
4. **First useful slice:** instrument bootstrap/city map startup, live start/readiness, and audio unlock/resume at their owners, including inline failures, successful attempts, and denominators. Add capability transitions/recovery and bounded observation windows. Enable committed-toast capture alongside them, preserving notification policy and removing unused warning/error bypass methods. This slice must show health without depending on toast presentation.
5. **Complete capability coverage:** add settings effect/persistence, audio stop, requested voice health, map conditional outcomes, and source navigation outcomes. Add coarse live freshness/processing observations only where evidence supports them. Cover disabled notification mappings independently when the underlying outcome is verified.
6. **Dashboards and independent checks:** deliver experience dashboard, SQL alert rules/fixtures, runbook, browser synthetic, aggregate trends and collection panels. Configure datasource UID, grants, measurement parameters, environment, thresholds and existing contact-point route. Build daily aggregation before the first raw data expires.
7. **Staging and rollout:** exercise successful/failed journeys, recovery, missing observations, offline replay, controlled feed and browser/audio matrix. Verify alerts firing, routing, and adequately observed recovery. Enable server then client gradually; monitor coverage, delay, event volume and overhead. Initially observe threshold rules before operational routing. Disabling capture stops new reports; disabling ingestion returns a retriable status rather than falsely acknowledging persistence. Retain stored history during rollback.
8. **Baseline and optional performance:** after at least two representative weeks, choose measured reliability targets, tune traffic guards and deadlines, and consider burn-rate alerts. Add Web Vitals only as a separately reviewed extension. Required owner outcomes, denominators, recovery, and daily aggregates are part of the initial scope, not contingent on this optional extension.

Expected code seams: `Client.Core/Services/{SignalRNotificationService,LiveDeliveryState}`, `Client.Shared/ViewModels/ApplicationViewModel`, map/readiness components, `Client.Shared/Services/Audio`, settings effect owners, `Client.Shared/Services/Notifications`, `ToastService.cs`, `Client.Shared/wwwroot/js`, `Client.WebApp/{Program.cs,wwwroot/index.html}`, shared DTOs/`ApiEndpoints.cs`, `Server.Data/{Models,Configurations,Migrations}`, `AppDbContext`, and `WebAPI/{EndpointGroups,Program.cs}`. Keep the passive capture abstraction accessible to Core without adding a dependency on Client.Shared presentation types. Use existing project dependency directions and DI lifetimes; no new runtime project is required.

## 7. Acceptance and verification

- Parameterized coverage proves every owner outcome/event kind and notification issue code is serializable/mapped; policy scenarios preserve represented/explained codes and displayed toast severity. Unsupported future values cannot poison a batch.
- A successful journey produces its eligible start and one terminal result; one user retry produces one new attempt while internal retries do not. Stale results, duplicate callbacks, host remounts and city changes cannot duplicate or misattribute observations. Results arriving before starts reconcile correctly; conflicting results are quality errors.
- Known cohort fixtures prove success/bad/unresolved/excluded calculations, zero-denominator `N/A`, provisional late corrections, sample sizes, and measurement-version isolation. Missing results never become automatic success/failure. Orphan results and loss are visible, not silently included in rates.
- An inline unlock failure is observable with zero toasts. A failed notification presenter does not hide the underlying map/audio/live outcome. A healthy session contributes denominators and observed time despite producing no warning/error.
- One committed toast yields one durable toast row. Suppressed, stale, failed, inline-only, and success notifications yield zero toast rows. Escalation and re-armed recurrence yield separate presentations. These conditions do not suppress independently eligible owner telemetry.
- Foreground/background, mute/pause, modal and scope changes produce correct eligibility. Simulated timer throttling, frozen tabs, browser suspension, and abrupt termination produce gaps/incomplete episodes, never fabricated continuous health or recovery. Capability time does not overlap within a window, and overlapping/replayed windows cannot inflate duration.
- Live transport-connected-but-not-joined is not ready. Quiet feeds without cadence evidence remain unknown freshness. Running audio context without required voices is not full requested-audio readiness; scheduling success does not claim physical audibility. A confirmed failed audio stop is observable without presentation.
- Recovery requires verified owner evidence. A deadline followed by recovery produces one attempt result plus recovery, not a second result. Scope disposal, toast expiry/dismissal, and telemetry loss never imply recovery.
- Early bootstrap and WASM share one page ID/outbox ownership without double subscription. WASM startup failure can still produce a bounded bootstrap observation; failure before the bootstrap script is covered by independent checks, with the browser coverage limitation documented.
- Reporter exceptions/storage errors never change rendering, reading time, interaction, playback, or application behavior, and cannot cause recursive notifications. No raw setting values, coordinates, URLs, IDs of transit entities, or diagnostic exception strings appear in payloads.
- Test offline -> enqueue -> reload/city change -> reconnect -> replay, multi-tab writes, unavailable storage, expiry/capacity, retries, `Retry-After`, timeout, invalid poison events, and lost acknowledgements. Use deterministic time/transport for unit tests and real browsers for IndexedDB/navigation behavior.
- PostgreSQL integration tests verify concurrent duplicate requests, rollback on failure, lost-response replay, multi-replica insertion, immutable receipt time, and logical-event reconciliation. An in-memory EF provider cannot prove these properties.
- Aggregate fixtures prove late result correction, affected-bucket replacement, watermark resume after extended downtime, repeat-job idempotency, histogram merging, and retention only after aggregate coverage. Multi-day rates use summed denominators; daily distinct session counts are never presented as monthly unique people/sessions.
- Endpoint tests cover body limits with/without content length, rate limits, malformed data, version skew, status/acknowledgement semantics, and exclusion of payloads/secrets from logs.
- Seed known starts/results, duration windows, combined toasts, delayed events, skewed clocks, orphan results, exclusions and page sessions. Verify dashboard totals and alert queries, including low traffic, empty denominators, delayed backlog, collection loss, and database failure. Run EXPLAIN against realistic successful-session volume and 30-day raw/13-month aggregate data, and measure query/insert latency.
- Demonstrate configured alerts firing, reaching the designated route, and recovering in staging. No-data/insufficient-coverage transitions must not silently resolve an existing health incident. A delayed backlog does not page as a current outage; fresh urgent stop failures still alert at low traffic. Lack of toasts does not establish collection health.
- Synthetic visits cannot inflate real-session denominators. A production bootstrap failure is detected even when its browser telemetry is absent; runner failures are distinguished. Controlled staging feed and supported-browser gesture checks provide reproducible map/audio evidence.
- Measure client CPU, memory, queue bytes, request/event rate, and audio/map responsiveness under normal and flapping/offline conditions. Verify one foreground scheduler/sender, fixed payload bounds, no per-note/frame .NET callbacks, and no growing event subscription graph. Record the practical overhead before broad enablement.
- Run affected existing client/server tests and Release builds. Preserve the existing notification behavior and architecture/observability constitution gates. Record browser, database, dashboard, and alert evidence in the runbook before release.

## Decisions to validate during implementation

The proposed defaults are 30-day raw / 13-month daily retention, a 7-day browser retry horizon, 1,000 queued events / 4 MiB, one observation window per foreground minute, a shared 5-second observation/deadline scheduler, 15-second suspension tolerance, bounded journey deadlines, and provisional rate/duration alert thresholds. All are configuration or measurement-version decisions to verify in staging; changes to measurement semantics must be versioned.

Before rollout, verify Grafana grants/network access, production CORS/proxy behavior, early-bootstrap API configuration, release-ID injection, successful-session event volume, synthetic isolation, supported-browser observations, aggregate maintenance, and existing contact-point routing. Quantify collection gaps, unmatched attempts and replay delay before treating percentages as reliable. Review low-traffic cities explicitly; minimum traffic may make rate alerts unevaluable, so synthetic checks and urgent outcome signals remain necessary.

Independent eligible-attempt/session denominators and recovery are included in this scope. Formal SLO targets, source heartbeat/cadence changes, cross-document navigation correlation, background-audio health, and general Web Vitals collection are follow-on decisions. The first release must label those coverage boundaries and must not describe foreground observed health as complete user experience or all-browser availability.
