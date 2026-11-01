# Client Stats Snapshot Store / Overlay Contract v1

One app-owned retrieval service, no chart ownership or browser persistence. Conceptual API, to be implemented in existing Client.Shared style:

```text
StartAsync(currentCity, lifetimeToken)      idempotent; return without waiting for warmup
SetCurrentCity(city)
SetOverlaySelection(exactSelection|null)   null means closed
TryGet(exactSelection) -> immutable state  evaluate age on every read
EnsureRequestedAsync(exactSelection)       join/admit; respect cooldown
StateChanged(exactSelection)               notify only changed state
StopAsync()                               cancel, await, unsubscribe
```

## Lifetime and gates

Register one instance per WASM app. App starts after first interactive render, subscribes navigation/shared visibility before initial hidden read, and stops/disposes owned work. Map retains sole AddVisibilityChangeListenerAsync callback registration. Store subscribes VisibilityChanged and never removes/disposes map's listener. Verify App/map startup/remount order; initial hidden tab issues no requests. Existing ApplicationViewModel initialization remains independent.

The existing StatsOverlay flag enables presentation and baseline preparation together; no separate preparation setting exists. Stats off means no work. Server stats_disabled suppresses automatic work until explicit configuration reevaluation/reload. Opening joins matching preparation through the same owner; never restore a dialog timer. Start returns without awaiting network; all launched tasks are observed, canceled and awaited at stop.

## Selection, retention and generations

Exact normalized identity is scope/canonical city/ordinal sorted distinct route keys. Reuse selection matcher after normalization. Labels/static RouteId/theme/units do not affect identity. Three snapshot slots maximum: current unfiltered city, independent All, latest filtered selection, <=32 MiB conservative estimated retained objects. Admission prioritizes city, All, filtered; measure actual and transient memory separately. Serialize once per new accepted DTO for estimate, not on opens/ticks. Active valid oversized DTO may be shown transiently but not retained; record skipped admission.

Navigation advances epoch, invalidates/cancels old city/filtered work and removes their snapshots; All stays. Reopen resets controls to current-city/unfiltered without clearing evidence. Closing invalidates dialog presentation subscription only; baseline acceptance can still complete.

Before publication validate entire required structure, supported versions, exact normalized selection, city/day/cutoff windows and schedule. Reject obsolete per-slot request/navigation generations and GeneratedAtUtc regressions across replicas. Accept immutable DTO/nested collections atomically. Identical complete evidence updates successful contact only: no age/deadline reset, no DTO/chart replacement. A malformed 200 preserves old evidence, records invalid_response and marks stale. HTTP success resets transport backoff; only valid evidence clears evidence error. Never relabel another selection's numbers.

## Time validation and states

For schedule-aware response require all three fields present/non-null, valid integer divisor interval, UTC timestamps, windowStart<=generation<next<=windowStart+interval, coherent root city windows and next no later than their future day ends. A crossing response still satisfies original generation ordering even if next<=receipt. Reject generation>receiptUtc+30 seconds; reject impossible cutoff/day order. Convert original deadlines once using receipt UTC and TimeProvider timestamp. Cap remaining fresh duration at interval; never extend an existing same-evidence anchor on contact. Subsequent wall-clock changes cannot extend age/expiry. Browser suspension/resume must evaluate elapsed age and expired metadata before display/network; verify on supported browsers.

All scheduling fields absent uses legacy 60-second schedule and hard expiry generation+60 seconds; no hourly inference or long retained fallback. Explicit null/partial fields invalid, so deserialization must preserve presence. Legacy response cannot replace newer accepted generation.

| Evidence condition | Presentation / action |
| --- | --- |
| Matching, before boundary, no known failure | Immediate charts with original Updated timestamp; open adds no request |
| Same selection refreshing | Keep charts/tables/focus; optional quiet progress |
| Known refresh failure | Immediately stale with safe error/retry and original update time |
| No replacement boundary+30s | Explicit overdue/stale notice while within retention |
| Original boundary+2*returned interval reached | Remove fallback; unavailable/retry subject to cooldown |
| Root city's local day ended | Explicit snapshot-date historical/stale daily section, or withhold daily section |
| No exact snapshot, request running | Honest selection loading |
| Missing/expired and request failed | Unavailable with admitted retry |

Age uses generation, completed-hour limit uses cutoff, scheduling uses returned next. Receipt is separate. Daily headings always use snapshot dates; stale root evidence cannot become authoritative PreviousDay. Coverage/zero/partial/pending/unavailable values remain unchanged by freshness. Known failure never waits 30 seconds. Day presentation becomes historical immediately at rollover. Scheduled stale/expiry/day transitions run even under network backoff; hiding pauses network, not the meaning of deadlines. Expiry is enforced on next read/resume as well.

## One scheduler and retry owner

Visible preparation startup: city then All. Earliest required boundary determines one cancellable delay. Independently sample 0–5 seconds positive jitter once per selection/window; never early. Recompute after response/resume/schedule change; evidence satisfying current window consumes duplicate triggers. Due while active is one pending desire, no accumulated ticks. Displayed selection first, then city/All; after at most one interactive turn, serve a due baseline and alternate overdue baselines. A displayed baseline counts as its baseline turn. This is the fairness guarantee under repeated route interactions.

Route desire retains existing 200 ms debounce and latest selection only. Filtered automatic refresh stops on close; baseline refresh continues only with preparation enabled. Exact duplicate work joins. At most one browser request including startup/open/retry/resume. Hidden cancels app-owned HTTP wait and pauses requests; shared server fill continues independently. Resume refreshes each overdue needed selection once, displayed first, no missed-tick replay. Repeated starts/opens cannot create loops.

Per-selection exponential retry: 60s, 120s, 240s, capped 300s, or longer Retry-After. 429 also sets endpoint-wide cooldown; every trigger including manual retry respects it. Other failures do not suppress healthy baseline selections. Cancellation caused by hide/navigation/stop is lifecycle control, not a transport failure. No lifecycle action resets applicable retry state.

## Overlay and diagnostics

Synchronous TryGet on opening precedes EnsureRequested. Overlay no longer injects transport/owns periodic loop. Presentation/chart instances built only open and changed accepted evidence or theme/metadata/presentation inputs. Same-evidence contact/stale status can update notices without chart rebuild; background work does not rerender App. Retain existing projection, route metadata, controls, accessible tables/native focus and instant dismissal. Do not implement separate units feature; its eventual change only reprojects DTO without requests. New copy lives in RouteFilterResources.resx, resource-backed English, light/dark styling and polite failure/recovery announcements; no per-tick announcements/focus shifts.

Use existing ILogger/optional Umami helper for bounded preparation, warm open, loader, age bucket, admission skip and error events/timings; no new platform and no identity labels. Failure of diagnostic delivery cannot affect retrieval. Browser/network/load traces separately establish latency, serialization, egress, actual memory and map/audio effects.

## Required lifecycle/acceptance checks

Idempotent starts/opens/dispose; initially hidden and listener ordering; current-city then All without startup blocking; preparation/statistics/server-disabled gates; warm open join/no request; unchanged/older/malformed atomic acceptance; exact selection/nav races; latest debounce/fairness; no early jitter or between-boundary reads; 15-minute adoption; local-day/DST history; hidden resume one needed request; 429/global and per-slot retry isolation; long Retry-After while stale/expiry still transition; clock/skew/suspension/identical-contact no extension; slot/byte pressure/nonfit; no hidden charts/main-app rerenders; legacy schedule/presence; both themes/focus/tables/polite recovery; no units/theme HTTP; independent rollback.
