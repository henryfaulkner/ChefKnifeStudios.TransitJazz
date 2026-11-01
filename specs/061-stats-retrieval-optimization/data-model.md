# Data Model: StatsOverlay Retrieval Optimization

All new state is in process/app memory. No tables, migrations, worker records or aggregation changes.

## StatisticsSelection

Fields: Scope (`city`/`all`), canonical CitySlug or null, immutable ordinal-sorted distinct RouteJoinKeys. Empty city keys mean unfiltered; All requires null city and empty keys. Preserve key spelling/case, reject blank/oversized values and existing request limits. Structured equality, never display labels/theme/units. Navigation invalidates city/filtered states, not independent All.

## RefreshPolicy / RefreshWindow

Server fields: RefreshIntervalMinutes (default 60; integer 1–60 dividing 60), IngestionGraceMinutes (existing 1–10, default 2), fixed Unix epoch UTC origin. Derived WindowStartUtc <= GeneratedAtUtc, ScheduledNextUtc=WindowStart+interval, EffectiveNextUtc=min(ScheduledNext, earliest future root CityWindow.LocalDayEndUtc). Existing cutoff=floorUtcHour(real now-grace) and calendar windows unchanged. Historical PreviousDay is not a schedule input.

## StatsOverlayResponse extension

Existing immutable-by-convention DTO/nested lists retained. Add nullable int RefreshIntervalMinutes and nullable UTC RefreshWindowStartUtc/NextRefreshAtUtc for mixed versions; new server always fills all. Existing generated/cutoff, selection, root cities/day bounds, coverage and PreviousDay remain authoritative. All absent fields mean legacy, partial presence invalid. Contract/metric version unchanged. No server delivery timestamp overwrites original generation.

## ServerReportingIdentity

Selection + refresh interval/window + cutoff + per-city local dates/time zones/cadence + configured membership + grace + contract/metric definitions + reported-vehicle enablement/source/cadence fingerprints. Canonical structured value or digest backed by exact canonical equality. Exclude generation/contact/units/theme. Cache and fill use identical full identity. Replaceable cache slot is exact normalized selection, including selected route array.

## ServerCacheEntry / SharedFill

Entry: selection slot, full identity, response, generated UTC, original boundary UTC, monotonic admission/deadline, estimated bytes including key/entry, kind filtered/baseline, last-access sequence. Hit requires full identity and not expired on either time basis. Hits never rewrite age/deadlines. Clock regression flushes entries. Newer generation/window protects against stale replacement.

Fill: identity, published completion promise, own deadline/shutdown CTS, acquired read-slot lease. Waiters own separate cancellation. At most four fills; no waiting work queue. Completion transitions to admitted response or valid uncached response; failure to safe problem; all outcomes remove promise/release lease. No caller token owns shared work.

Budget: total 33/64 MiB; filtered eight/16 MiB inside total. Filtered zero entries disables retention, not coalescing. Current bytes*8+4096 plus canonical identity/entry overhead is an estimate; actual retained/transient heap measured separately. Expired-first, filtered LRU, protected baselines; nonfitting returns uncached.

## ClientRetainedSelectionState

Fields: exact selection; navigation epoch/request generation; optional accepted DTO; immutable schedule mode; original generation/cutoff; receipt UTC and elapsed timestamp; fixed due/stale/hard-expiry/day-transition elapsed deadlines; transport progress; last contact; safe error; per-selection exponential retry deadline; estimated bytes. At most current-city, All and latest filtered snapshot, combined <=32 MiB. Admission order city, All, filtered. A nonfitting DTO may serve an active overlay until replaced/closed but cannot masquerade as a retained slot; record skipped admission and measure that transient presentation separately.

Freshness is separate from coverage. Derived states: missing/loading, current, refreshing-with-evidence, stale, historical-daily, expired/unavailable. Complete/partial/pending/unavailable/zero metrics do not change on refresh failure. Known error marks stale immediately; overdue at boundary+30 seconds; expired no later than boundary+2*interval. At local-day end daily evidence must retain original date and prominent stale notice or be withheld. Never synthesize PreviousDay from stale root evidence.

Legacy mode: minute schedule and hard expiry original GeneratedAtUtc+60 seconds, no extended fallback. Schedule-aware mode: all fields valid; future-generation tolerance 30 seconds; convert deadlines once to elapsed time and cap fresh remaining duration at interval. Identical delivery never resets anchors. Older generation/time cannot replace newer; invalid payload preserves old DTO with invalid_response. Expiry has no dependency on successful replacement.

## AppSchedulerState

Fields: started/disposed; statistics/session-suppressed flags; current city; overlay closed/exact displayed selection; visibility; endpoint cooldown; single active request/generation; latest filtered desire/debounce deadline; city/All due bits and fairness cursor; one cancellable next wakeup; one jitter sample per selection/window. No queue of ticks/clicks. All tasks observed and canceled/awaited at stop.

Transitions:

1. Start -> read visibility; visible+statistics enabled prepares city then All without blocking render/audio. Repeated start idempotent.
2. Open -> synchronous matching read; ensure missing/due through same admission. Close -> remove filtered automatic desire and presentation subscription; retain slots. Next open defaults current-city/unfiltered.
3. Boundary -> mark due once, sample positive jitter; displayed first then fair baseline turns. At most one interactive turn ahead of overdue baseline; alternate city/All. Already satisfied window consumes duplicate trigger.
4. Hide -> pause network delay/cancel app wait, preserve snapshots. Resume -> evaluate elapsed deadlines and refresh needed selections once; no tick replay.
5. Failure -> per-selection backoff; 429 also endpoint cooldown. HTTP success resets transport backoff, valid atomic acceptance clears evidence error. User controls cannot bypass either applicable cooldown.
6. Navigate -> advance epoch, invalidate/cancel old city/filtered work and slots, retain All; warm new city if statistics enabled/visible.
7. stats_disabled -> suppress automatic requests until explicit feature reevaluation/reload. Stats off/stop -> no work, cancel/await/unsubscribe.

## Presentation ownership

Store never contains chart models. StatsOverlayAcceptedSnapshotState may remain dialog-only; use existing projection and route metadata enrichment only while open/changed. Freshness state can notify open dialog without rebuilding charts. Theme/units/metadata reproject retained DTO only. Closed overlay background acceptance does not call main App.StateHasChanged. New user copy stays in neutral resources and both themes.
