# Phase 1 Data Model: Time-to-First-Note

This feature adds no new persisted store. It extends three existing in-memory/serialized structures and one client-side probe object. Field names below are binding where they cross a contract (telemetry snake_case, MessagePack union).

---

## 1. TelemetryEvent — new suppression-count columns (US2, D3)

Extends `TransitDataWorker/Logging/TelemetryEvent.cs`. Property names ARE parquet column names (Parquet.Net 5.6.1 has no rename attribute — see the file's own comment and `project_parquet_net_no_column_rename` memory). All new columns are `int?`, PerCityCycle-only (null on FullCycle rows, summed on FullCycle like the existing per-cycle ints).

| New column (snake_case) | Type | Meaning | Set from |
|---|---|---|---|
| `crossings_suppressed_first_seen` | `int?` | Vehicles that emitted nothing because baseline was null (first observation / re-seen after prune) | `CrossingDetector` reason `FirstSeen` |
| `crossings_suppressed_delta_leq0` | `int?` | Vehicles with no forward progress along the stored shape (incl. reverse-direction BEFORE D4) | reason `DeltaLeqZero` |
| `crossings_suppressed_teleport` | `int?` | Vehicles whose along-distance jumped > 2000 m (baseline reset, out-and-back snap-flip) | reason `Teleport` |
| `crossings_suppressed_transfer` | `int?` | Vehicles whose `RouteJoinKey` changed (route transfer reset) | reason `RouteTransfer` |

**Invariant (SC-007, FR-006)**: for a PerCityCycle row,
`crossings_suppressed_first_seen + …delta_leq0 + …teleport + …transfer + (vehicles that emitted ≥1 crossing) == vehicles that ran crossing detection this cycle`.
(Vehicles skipped before detection — no join key, unknown route, stale, no trigger points — are already counted by the existing `skippedNoJoinKey`/`skippedUnknownRoute` log fields and are outside this sum.)

**Contract coupling (FR-016)**: each new column MUST be added to the Go allow-list `kindNumeric` map in `tools/telemetry-mcp/internal/validate/validate.go` and given an accept vector in `validate_test.go`; and to `TelemetryEventSchemaTests`.

---

## 2. CrossingBaseline — direction state (US2, D4)

Extends `TransitDataWorker/Checkpoints/CrossingDetector.cs` `CrossingBaseline`. Today it holds `{ RouteJoinKey, LastCrossedAlongDistanceM }`. Reverse-direction emission needs to know which way the vehicle is travelling along the stored shape.

| Field | Type | Meaning |
|---|---|---|
| `RouteJoinKey` | `string` | (existing) resolved join key of the vehicle's current route |
| `LastCrossedAlongDistanceM` | `double` | (existing) baseline along-distance from the last emitting tick |
| `Direction` | `sbyte` / enum `{ Unknown, Forward, Reverse }` | **new** — sign of sustained along-distance motion; drives which window of trigger points is collected |

**State transitions** (per vehicle per tick, `delta = currentDistM − LastCrossedAlongDistanceM`):

- `baseline is null` → seed `{ key, currentDist, Unknown }`, emit nothing (`FirstSeen`).
- `key changed` → reset `{ newKey, currentDist, Unknown }`, emit nothing (`RouteTransfer`).
- `|delta| > 2000 m` → teleport reset, keep/clear direction to `Unknown`, emit nothing (`Teleport`).
- `delta > 0` → Forward: collect trigger points in `(prev, current]`, advance baseline up. If prior `Direction == Reverse`, this is a turnaround → reset to Forward, emit nothing this tick (avoid double count).
- `delta < 0` → **Reverse (NEW)**: collect trigger points in `[current, prev)` in reverse order, advance baseline **down**. If prior `Direction == Forward`, turnaround → reset to Reverse, emit nothing this tick.
- `delta == 0` → no movement, emit nothing (`DeltaLeqZero`), baseline unchanged.

**Pre-D4 behavior** (counters only): `delta <= 0` returns `DeltaLeqZero` and emits nothing (current code). D4 splits the `< 0` case out into Reverse emission; the `== 0` case stays `DeltaLeqZero`.

---

## 3. LastBatchCache — recent-crossing replay buffer (US3, D5)

Extends `WebAPI/SignalR/ILastBatchCache.cs` `CityCache`. Today it upserts only `RouteNearestPointBatchEvent` records and rebuilds a single position envelope on `Current`. Add an age-capped ring of recent crossing records.

| Field | Type | Meaning |
|---|---|---|
| (existing) `_vehicles` | `Dictionary<string, Entry>` | latest position record per vehicle, cycle-aged |
| `_recentCrossings` | `List<(RouteCrossingRecord Record, DateTimeOffset At)>` or bounded ring | **new** — crossing records from the last ≤ K seconds / ≤ 1 tick |
| `CrossingAgeCapSeconds` | const | **new** — max age of a replayed crossing (e.g. one tick, ~10 s; tune in quickstart) |

**Rules**:
- `Set(city, batch)` extracts `RouteCrossingBatchEvent` records from the batch (if present) and stores them with a timestamp; prunes any older than the age cap.
- `Current(city)` returns the position envelope PLUS a `RouteCrossingBatchEvent` envelope containing only crossings within the age cap at read time (may be empty → omit the crossing envelope, exactly as the Worker omits it when `crossingRecords.Count == 0`).
- The replayed crossing envelope MUST be ordered like the live one (join key, vehicle, trigger index) so client dispatch is deterministic.

**Regression guard (D5)**: the age cap is what prevents the "rapid pulsing" burst. The client's `crossingDelayMsFor` fires each replayed crossing against the animated dot's current position, so stale crossings (dot already past) produce a non-positive delay and are dropped client-side; the age cap bounds how many can pile up.

---

## 4. TtfnProbe — client measurement object (US5, D7)

Module-scope object in `transit-synth.js`, exposed as `window.TtfnProbe` (mirrors `window.MemoryProbe`). Not persisted; per-session.

| Field | Type | Meaning |
|---|---|---|
| `version` | `string` | per-deploy stamp (commit short-SHA), set at build/deploy |
| `unlockAt` | `number \| null` | `performance.now()` at first unlock (either path) |
| `firstTriggerAt` | `number \| null` | `performance.now()` when the first note passes the `_unlocked` + `_audioEnabled` gates |
| `firstAudibleAt` | `number \| null` | `performance.now()` after the first `sampler.triggerAttackRelease` |
| `droppedWhileLocked` | `number` | count of `triggerNote` calls that returned because `!_unlocked` (>0 ⇒ steady-state/dwell; 0 ⇒ cold-start) |
| `noiseBedAt` | `number \| null` | `performance.now()` when the ambient noise bed starts in `unlock()`; `noiseBedAt − unlockAt` is the recorded SC-001 "audible within 1 s" number (ambient, distinct from `firstAudibleAt` which is the first *note*) |

**Derived metric (FR-012)**: `unlock→trigger = firstTriggerAt − unlockAt` (supply half, B1+B2); `trigger→audible = firstAudibleAt − firstTriggerAt` (build half, B4); `total = firstAudibleAt − unlockAt`. Emitted once as a `[TTFN] v=… unlock→trigger=…ms trigger→audible=…ms total=…ms droppedWhileLocked=…` console line, plus `performance.mark`/`measure` spans.

---

## 5. Musical-density health signal (US5, D8) — derived, not stored

No new field. A rolling-window computation over existing `PerCityCycle` rows:

| Signal | Definition | Threshold |
|---|---|---|
| zero-tone-tick fraction | count(`tones_emitted == 0`) / count(rows) over a rolling hour, per `city_name` | flag if **> ~30%** |
| tones/tick vs. baseline | avg(`tones_emitted`) over the window vs. the city's recorded baseline | flag if **< ~½ baseline** (post-D4) |

Consumed via the telemetry query path (mj-data-explorer / Go bridge). Documented threshold, not a live service.
