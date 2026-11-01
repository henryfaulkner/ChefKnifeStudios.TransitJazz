# Research: StatsOverlay Retrieval Optimization

**Date**: 2026-10-30. Repository inspection and primary documentation; no runtime result claimed.

## Existing server ownership

**Decision**: Keep StatsOverlayService singleton as sole cache/fill owner, generalize filtered requests and add one refresh policy helper. Preserve Data/Statistics/StatsOverlayQuery.cs unchanged.

**Evidence**: ReadAsync sets cacheable only for zero route keys, gates inFlight behind it and passes caller cancellation into filtered fills. BuildCacheKey omits routes and uses delimiter joins. CacheScopeKey has city/All slots only. CacheSnapshot/RemoveExpired use GeneratedAtUtc+CacheSeconds. Existing admission has four slots with Wait(0), bounded JSON serialization and a five-second deadline. Options are concrete startup-validated singletons in Program.cs.

**Rationale**: Selected-route SQL changes coverage/aggregates; browser filtering an unfiltered DTO is insufficient. All must remain one consistent read.

**Alternatives**: A second cache layer, reusable dataset and distributed caching add different contracts. CacheSeconds cannot remain an invisible minimum expiry.

## Schedule, identity and server clocks

**Decision**: Unix epoch UTC origin; integer interval 1–60 dividing 60; floor((now-origin-grace)/interval). Effective next is fixed next or earliest future root LocalDayEndUtc. Resolve reporting with real now and unchanged StatsOverlayPolicy. Root windows govern refresh; historical PreviousDay.LocalDayEndUtc never creates an immediate loop.

**Rationale**: Quarter-hour rereads change correction visibility, not aggregation. Midnight splits a fixed window; identity includes actual local date even with unchanged windowStart. Existing policy handles DST/invalid/ambiguous boundaries. Settings use startup/restart configuration, consistent across replicas, with no push.

**Decision**: Canonical structured exact reporting/selection identity shared by cache and fills; digest only with equality verification. Keep monotonic expiry alongside UTC and flush cache on observed clock regression. Admission checks original boundary and latest slot generation/policy.

**Alternatives**: Sliding TTL, rewritten generation timestamps and separator-joined arbitrary keys violate evidence/identity rules. Server polling is unnecessary.

## Shared work and budgets

**Decision**: Publish promise under short lock, acquire slot without queue, run outside lock; shared CTS links deadline and ApplicationStopping. Caller token cancels only its wait. Finally remove only that promise/release slot. Support immediate completion/throwing fakes as well as slow reads.

**Rationale**: Existing Task.Yield must not hide registration/cleanup races. Shared work can outlive all callers but always has finite lifetime. Crossing responses may return original expired metadata, never enter retention.

**Decision**: Total 33 entries/64 MiB, filtered eight/16 MiB; zero filtered entries disables retention only. Current estimate bytes*8+4096 plus exact key/entry overhead. Expired first, filtered LRU protects baselines, baseline displaces filtered before oldest baseline; nonfitting valid responses uncached. Separate retained memory from four transient fills.

**Alternatives**: Unbounded route combinations, failure retention and stale-server fallback violate bounds. Keep dedicated dictionaries; [Microsoft cache guidance](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/memory?view=aspnetcore-10.0) supports explicit limits and a working miss path.

## App lifetime and shared visibility

**Decision**: One Client.Shared StatsSnapshotStore, registered by WebApp, started after first interactive render by App and disposed with App. Subscribe navigation/VisibilityChanged then query initial hidden state; recheck after startup to avoid races. Initially hidden means no requests. Preparation flag below StatsOverlay defaults off.

**Evidence**: App currently starts ApplicationViewModel from DI scope in OnInitialized. StatsEndpointsService is a metadata-aware wrapper. TransitMap.razor.cs owns first-render AddVisibilityChangeListenerAsync. Interop has one replaceable map callback plus multicast event. Existing overlay subscribes to event. Keep map registration; never create a competing browser listener.

**Rationale**: First render permits interop; observe/cancel/await owned tasks on teardown without gating map/audio. NavigationManager.ResolveCity already resolves canonical city. [Microsoft lifecycle guidance](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/lifecycle?view=aspnetcore-10.0).

**Alternatives**: Program root-provider resolution risks scoped graphs; dialog ownership clears data; second listener replaces map callback. Browser acceptance verifies map-first/App-first/remount ordering.

## Client scheduling, retry and fairness

**Decision**: One request/earliest-wakeup delay; city then All; displayed first on resume; positive jitter sampled once per due window; 200 ms latest filtered desire. At most one interactive turn before a due baseline; alternate city/All. A baseline serving displayed selection satisfies both.

**Evidence**: StatsOverlayRequestPolicy already contains 60-second-to-five-minute retry, selection matching and generation/admission helpers. Existing session open resets retry and close clears evidence; move these lifetimes to store. Existing accepted state embeds chart presentation, so reuse pure helpers rather than that combined type as store state.

**Rationale**: Absolute displayed-first priority can starve baselines. Include stale/day/expiry transitions during cooldown. 429 is endpoint-wide; other retries per selection. No queue of every click/tick.

**Alternatives**: Multiple timers, dialog cancellation of baseline work and lifecycle retry resets create races/traffic. Use existing TimeProvider with controllable delay and jitter sampler; no timing package.

## Acceptance, legacy and elapsed deadlines

**Decision**: Nullable scheduling fields; all absent is legacy, some absent invalid. Keep v1 contract/metric versions. Validate full required structure/selection/time order before atomic publication. Reject superseded generations and GeneratedAtUtc regressions across replicas. Identical complete evidence updates contact only.

**Decision**: Future generation skew tolerance 30 seconds. Map UTC deadlines once at receipt to elapsed TimeProvider timestamps, remaining freshness clamped to [0, interval]. Do not re-anchor identical evidence. Known error stale immediately; otherwise boundary+30 seconds; hard expiry boundary+2*interval. Root day end immediately triggers historical labeling/withholding. Over-tolerance skew or bad ordering is invalid_response. Test skew, wall jumps, identical responses and suspension. [TimeProvider timestamp documentation](https://learn.microsoft.com/en-us/dotnet/api/system.timeprovider.gettimestamp?view=net-10.0) supplies elapsed measurement; browser suspension behavior remains an acceptance case.

**Decision**: Legacy deadline GeneratedAtUtc+60 seconds, no extended fallback, minute scheduling. No old payload becomes hourly evidence. Existing code has no long retained stale fallback; conservative minute hard expiry resolves the design's legacy wording. A legacy response cannot replace a newer accepted generation.

**Rationale**: HttpServiceResponse exposes status/problem/RetryAfter, not server Date. Avoid time synchronization/CORS machinery. Initial skew is bounded; later wall changes cannot extend deadlines. Coverage remains independent of freshness. Units/theme never enter retrieval identity.

**Alternatives**: Hourly default for missing metadata, receipt-time sliding expiry and partially published fields are unsafe.

## Diagnostics and release inputs

**Decision**: Extend server StatsOverlayTelemetry. No client Stats diagnostic service exists: use existing ILogger plus bounded optional Umami events through wwwroot/js/umami-city.js; aggregate counts/timings/buckets only, independent of analytics availability. Never label route sets/raw keys/hashes/visitor IDs.

**Evidence**: Server meter has cache events/duration/bytes, no subset/occupancy gauges. Client services use ILogger; trackCityView/trackPageView are existing optional analytics helpers.

**Decision**: Baseline capture names supported desktop/mobile hardware, browser/network/load profile and variance in validation.md. Measure warm/cold/never-opened sessions, startup, actual retained/transient heap, compressed bytes, serialization, reads, age and boundary traffic. Include 500 visible apps/multiple replicas/shared-IP limits. Exact device selection is a release execution input, not unresolved architecture.

**Alternatives**: New telemetry stack, hit ratio alone and treating ~1,000/hour arithmetic as measured peak capacity are unjustified.

## Resolution

No unresolved technical research markers. Explicit conservative assumptions above are implementable. Existing English-only Stats scope is an inherited XII exception documented in plan. Actual measurements and rollout remain later release gates.
