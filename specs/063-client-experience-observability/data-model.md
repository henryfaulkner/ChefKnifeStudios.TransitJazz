# Data Model: Client Experience Observability

**Source**: [spec.md](spec.md), [research.md](research.md), and [source design](../../docs/CLIENT_TOAST_OBSERVABILITY_DESIGN_PLAN.md). These are proposed implementation entities, not existing tables.

## Identity and version rules

- `schema_version = 1` describes the wire shape. `measurement_version = 1` describes eligibility, deadlines, state, and cohort calculations. A changed measurement definition increments the latter even if the wire shape stays compatible.
- Each accepted event gets immutable `event_id` UUID before enqueue. `page_session_id` is a fresh UUID for each document. `city_activation_id` is a fresh UUID for each city scope. `sequence` increases within the page session and exposes gaps.
- `attempt_id` identifies one accepted action through one terminal result; `episode_id` identifies one capability episode. These identities never become metric labels. `presentation_id` is local to a page and is not the transport deduplication key.
- Correlation requires matching page and city scope. The 24-hour attempt horizon and seven-day outbox horizon are distinct. An expired correlation is not an inferred failure.
- `occurred_at_utc` is untrusted client time. `page_elapsed_ms` is monotonic within the page. `received_at_utc` and `environment` are assigned by the server. Mark skewed occurrence time; never rewrite the original receipt on retry.

## Capture entities

| Entity | Fields / relationship | Validation and meaning |
| --- | --- | --- |
| PageSession | `page_session_id`, `client_release`, start time, event sequence | One per document; no durable person ID. Pre-application script and main application share the same ID. |
| CityActivation | `city_activation_id`, parent page, allowlisted city slug, start/end context | One current city at a time. Full reload creates a new page session. Unknown city is explicit. |
| Attempt | `attempt_id`, page/city, journey or fixed action code, parent action ID where relevant, eligibility, deadline, monotonic start, terminal status/reason/elapsed | Exactly one accepted start and at most one terminal result. Duplicate callbacks reuse IDs. Result may precede start at intake and is reconciled later. |
| CapabilityEpisode | `episode_id`, page/city/capability, requested state, accepted operation generation, state/reason transitions | States: `pending`, `healthy`, `degraded`, `unavailable`, `unknown`, `not_requested`. Only verified healthy evidence closes a degraded episode as recovered. |
| ObservationWindow | page/city, `window_sequence`, monotonic start/end, observed/gap/excluded durations, fixed per-capability state totals, coarse counters | At most one full window per 60 foreground seconds plus partial scope/visibility windows. Intervals cannot overlap; state/eligibility partition totals must equal observed duration under the measurement definition. |
| ToastPresentation | `event_id`, page/city, local presentation ID, accepted time, displayed severity, summary kind, capability and represented/explained issue arrays, safe resource keys, known episode links | One record only after current-scope acceptance. One combined toast remains one presentation. Issue classifications overlap and cannot be summed as independent incidents. |
| RuntimeFailure | page/city if known, fixed category, once per category per activation | Only classified fatal UI/bootstrap states, never arbitrary exception text. |
| LossSummary | fixed event-kind counts and reason, bounded window | Best-effort local loss report after queue expiry/capacity/storage failure; not an exact audit and never an experience outcome. |

## Fixed event union

All events share `schemaVersion`, `measurementVersion`, `eventKind`, `eventId`, `pageSessionId`, nullable `cityActivationId`, `sequence`, `occurredAtUtc`, `pageElapsedMs`, bounded `citySlug`, and `clientRelease`. Kind-specific payloads have no arbitrary property bag.

| `eventKind` | Required kind-specific data |
| --- | --- |
| `session_started` | One pre-application page start and bootstrap measurement identity. |
| `context_changed` | City activation, foreground/background, requested delivery/audio, deliberate exclusion codes. |
| `attempt_started` | Attempt ID, fixed journey/action code, start offset, eligibility, configured deadline, optional parent action ID. |
| `attempt_finished` | Same attempt ID/code/scope, one of `succeeded`, `failed`, `deadline_exceeded`, `cancelled`, `superseded`; safe reason and foreground/wall elapsed. |
| `capability_changed` | Capability, previous/current state, fixed cause, episode ID, accepted operation generation; initial snapshot flagged as late if applicable. |
| `observation_window` | Non-overlapping observed and gap intervals, state/eligibility partitions for all fixed capabilities, bounded coarse counters/ages. |
| `toast_presented` | Immutable committed summary, displayed severity, issue/explained arrays, resource keys, optional known episode IDs. |
| `runtime_failed` | Fixed fatal category and activation scope; no message or stack. |

An optional future `performance_summary` kind is reserved but not emitted in initial scope. No route/vehicle IDs, coordinates, URLs, setting values, IPs, raw user-agent, raw exceptions, secrets, or rendered toast text belong in event payloads.

## Durable storage

### `client_experience_events`

Append-only raw event table. Primary key `event_id uuid`; common indexed query columns: `page_session_id uuid`, nullable `city_activation_id uuid`, `sequence`, `received_at_utc timestamptz`, `occurred_at_utc timestamptz`, `clock_skewed boolean`, `environment`, `city_slug`, `client_release`, `schema_version`, `measurement_version`, `event_kind`, `page_elapsed_ms`. Nullable typed columns hold `attempt_id`, `episode_id`, `journey`, `capability`, `outcome`, and toast severity. A schema-validated bounded JSON payload holds kind-specific fields. Index receipt time, environment/city/kind/receipt, and a partial session/activation/attempt correlation key. Additional indexes require query-plan evidence.

The server assigns receipt time once. Insert uses a conflict target restricted to `event_id`, returning acknowledgement for an already stored ID without replacing data. A transaction either stores every accepted member of a batch or acknowledges none. Replayed event UUIDs are transport duplicates; two distinct UUIDs claiming the same logical attempt result or window are quality conflicts and are quarantined from derived views rather than arbitrarily chosen.

### Reviewed logical views

- **Attempts**: Join starts and terminal results by scope and attempt ID. Unmatched results are diagnostic; starts without results are unresolved. A cohort is keyed by start receipt time, environment, city, journey/action, release, and measurement version. `S`, `F`, `D`, `U`, `X` remain separate; eligible denominator `S+F+D+U`, success share `S/eligible`, bad share `(F+D)/eligible`, unresolved share `U/eligible`.
- **Validated windows**: Accept only non-overlapping, internally balanced windows. Exclusions and gaps are separate from eligible observed duration. Validated occurrence intervals supply current degradation alerts; receipt buckets supply operational volume.
- **Episodes**: Resolve only from verified current-scope healthy evidence. End-of-page or gaps produce incomplete episodes. Recovery latency counts eligible observed time between confirmed degradation and recovery.
- **Presentations**: One accepted toast event, preserving displayed severity and full arrays. A single toast with several issues is still one presentation.
- **Quality/coverage**: Orphans, conflicts, overlap, skew, sequence gaps, delay, reported local loss, and incomplete episodes remain explicit.

Operational reports use receipt-based start cohorts; trusted historical occurrence views have a separate labeled time basis. A zero denominator is `N/A`, never a 100% success rate. Comparisons require matching measurement versions and time basis.

### `client_experience_daily`

Derived rows keyed by UTC day, environment, city, release, measurement version, time basis, metric, and journey/capability where applicable. Values include starts and outcome counts, state-duration sums, fixed latency-histogram buckets, toast counts, quality/coverage counts, and daily affected/observed page-session counts. Sum counts and denominators over days; derive rates afterward. Merge histogram buckets for approximate percentiles; never average daily percentiles or sum daily distinct sessions as monthly unique people.

### Maintenance state

Persist watermark, job version, last success, and lag. An hourly single-writer job under a database advisory lock atomically replaces affected daily buckets, including zero/reclassified rows. Recompute the recent ten-day correction window plus buckets touched by newly received correlations; after downtime resume from the watermark across retained raw data. Hourly bounded raw cleanup follows only after aggregate coverage. Default raw retention is 30 days by receipt, daily retention 13 months.

## State transitions and eligibility

| Transition | Accepted evidence | Reporting consequence |
| --- | --- | --- |
| `not_requested -> pending` | Current city/action requests capability | Start eligible attempt/episode only where applicable. |
| `pending -> healthy` | Owner confirms required readiness | Finish attempt once; begin healthy observed time. |
| `pending/healthy -> degraded/unavailable` | Owner confirms impact | Finish pending attempt if applicable; open/continue degradation episode. |
| `degraded/unavailable -> healthy` | Owner confirms current-scope restoration | Close recovery interval; no second result for a deadline-finished attempt. |
| Any -> `unknown` | Evidence stale, suspension gap, or unverified status | Stop attributing confirmed health; do not infer failure or recovery. |
| Requested -> deliberate pause/mute/hidden | Accepted user or visibility change | Close partial window; subsequent capability time excluded, while the explicit action attempting the change remains eligible. |
| Scope ends | Navigation/disposal | Close observed interval if possible; leave unresolved/incomplete states explicit. |

Live connected transport without confirmed join is not healthy. Map readiness needs usable current-city geometry and required layers. Audio unlock requires running audio, while requested audio health also requires required voice readiness. Quiet live updates have unknown freshness until a source cadence contract exists.
