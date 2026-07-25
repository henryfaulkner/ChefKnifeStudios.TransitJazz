# Contract: Join-Time Crossing Replay (US3 / D5, FR-008, FR-009)

Governs `WebAPI/SignalR/ILastBatchCache.cs` and `WebAPI/SignalR/TransitHub.cs`. **Server-only** — `RouteCrossingBatchEvent` is already `[Union(1)]` in the MessagePack contract (`ISignalREvent.cs`), so no client or wire change.

## Current behavior (the thing being changed)

- `LastBatchCache.Set` extracts ONLY `RouteNearestPointBatchEvent` records; crossings are deliberately dropped (`ILastBatchCache.cs:54–99`).
- `TransitHub.JoinCity` replays `Current(city)` — position only, zero crossings (`:21–28`).
- This is deliberate: it fixed a "rapid pulsing" burst on load (`TransitMap.razor.cs:119` comment, `LastBatchCacheCrossingExclusionTests`).

## New behavior

`LastBatchCache` MUST additionally retain **age-capped** recent crossings and include them in `Current`:

- **On `Set`**: if the batch contains a `RouteCrossingBatchEvent`, store its records timestamped; prune records older than `CrossingAgeCapSeconds`.
- **On `Current`**: return the position envelope AND (if any survive the age cap at read time) a `RouteCrossingBatchEvent` envelope of those recent records, ordered `(RouteJoinKey, VehicleId, TriggerIndex)` like the live publish path. If none survive, omit the crossing envelope entirely (never send an empty one).
- `TransitHub.JoinCity` replays whatever `Current` returns — no hub logic change beyond that it may now carry a second envelope.

## Age cap (the rapid-pulsing guard — FR-009)

- `CrossingAgeCapSeconds` bounds how many crossings can pile up for replay (start ~one tick / ~10 s; tune in quickstart).
- The client already computes each crossing's fire delay from `AlongDistanceM` vs. the animated dot's current position (`crossing-dispatcher.js` `crossingDelayMsFor`). Crossings whose dot has already passed produce a non-positive delay and are dropped client-side — so age-capped replay respects real dot positions and does not fire for passed points.
- Net: a joining client hears at most one age-capped, dot-position-respecting set of recent crossings — not a replayed burst.

## Size ceiling (FR-017, spec edge "Very high-volume city on join replay")

The replayed crossing envelope MUST stay under the feature-040 **5 MB SignalR ceiling** for the busiest city (NYMTA) at peak. This is bounded by construction: the age cap (~one tick) means the replay can carry at most **one tick's worth** of crossing records — it cannot accumulate more than the live publish path already sends per batch, which already ships under the ceiling. Worst-case bound to confirm in T028a: `max(tones_emitted at NYMTA peak) × per-crossing-record wire size ≪ 5 MB` (NYMTA observed 85 tones/tick evening; check the true peak). Because the cap makes replay ≤ one live batch, overflow is structurally impossible unless a single live batch already overflows — which the 040 ceiling work already governs.

## FR-017 size bound (verified, T028a)

`CrossingAgeCap` is fixed at **10 seconds** (`LastBatchCache.CityCache.CrossingAgeCap`, `ILastBatchCache.cs`), matching one Worker tick (`Worker.cs`'s `PeriodicTimer(TimeSpan.FromSeconds(10))`). Because a crossing is only ever added to `_recentCrossings` once (on the `Set` call for the tick that produced it) and pruned by the same cap on every subsequent `Set`/`Current`, the replay buffer can never hold more than **one tick's worth** of crossing records — it is structurally bounded to at most what the live publish path already sent in its most recent batch for that city.

The live publish path (`Worker.cs` `ProcessSpatialReconciliationAsync`) already ships every tick's `RouteCrossingBatchEvent` under the feature-040 5 MB SignalR ceiling — that is an existing, separately-enforced invariant of the live path, not something this feature adds. Since replay can carry at most one tick's crossings (same records, same wire shape, `RouteCrossingRecord` is unchanged), the replayed envelope is bounded by the same ceiling the live path already respects: `replay size ≤ max single-tick crossing batch size ≪ 5 MB`.

Worst case checked against NYMTA (the busiest city, observed ~85 tones/tick evening peak in the discovery baseline): 85 records × `RouteCrossingRecord`'s fields (`VehicleId` string, `RouteJoinKey` string, `TriggerIndex` int, `TotalTriggers` int, `AlongDistanceM` double) is on the order of a few KB even before MessagePack compaction — several orders of magnitude under the 5 MB ceiling. Overflow is therefore structurally impossible for replay specifically unless a single live tick already overflows, which is governed by the existing feature-040 ceiling work, not this feature.

## Verification (SC-004)

- Fast-click (unlock immediately on load) time-to-first-note MUST converge toward dwell time-to-first-note (previously it was materially worse by the cold-start 5–15 s penalty).
- No audible rapid-pulse burst on load (ear check + the rewritten exclusion test).

## Tests (`LastBatchCacheCrossingExclusionTests.cs` — rewritten)

The existing test pins "Current excludes all crossings." Rewrite to the new guarantee:

- Crossings within the age cap ARE included in `Current`.
- Crossings older than the age cap are NOT included.
- An empty surviving set produces NO crossing envelope (position envelope only).
- Replayed crossings preserve the canonical ordering.
- Position-snapshot behavior (eviction, staleness) is unchanged.
