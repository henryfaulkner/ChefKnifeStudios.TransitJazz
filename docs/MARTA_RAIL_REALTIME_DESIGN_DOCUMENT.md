# MARTA Rail Realtime — Design Document

**Status:** Proposed — all open questions resolved (2026-06-23), ready to spec
**Author:** Henry Faulkner
**Scope:** Server-side only (TransitDataWorker). Minimal-to-no client impact.
**Decision date:** 2026-06-23

---

## 1. Decision & Rationale

**Decision:** Add **MARTA heavy-rail trains** (RED / GOLD / BLUE / GREEN lines) to the
soundscape — **not** additional cities — by ingesting the MARTA Rail Realtime RESTful
API and normalizing its output into the *existing* vehicle-position reconciliation
pipeline.

**Why train over more cities:** The optimization goal is **depth / showcase quality**,
not reach. A richer, more complete Atlanta transit soundscape is a stronger portfolio
piece than thin coverage smeared across cities the author doesn't ride. Trains add a new
*voice class* to the existing musical model (route = instrument, vehicle = pitch — see
`specs/009-transit-soundscape/`) within the city the app already knows.

### The "extrapolation" worry was misframed

The original hesitation was that trains require *extrapolating* position from time, and
"people will know when it's wrong." This is resolved:

1. **The bus pipeline already extrapolates.** The server snaps a real GPS position to the
   route shape every ~10 s; the client *lerps* the marker smoothly between snapped
   samples (`specs/019-lerp-event-cache/`). Buses on screen are already
   real-position-plus-interpolation. Trains are the *same* pattern.
2. **The rail feed gives a live track position per train.** `GetRealtimeArrivals` returns
   `LATITUDE` / `LONGITUDE` per `TRAIN_ID` — a real GPS fix, confirmed to move along
   track, not a station coordinate.
3. **Trains are *easier* than buses.** A train is confined to a fixed rail line, so
   snapping its position to the route shape is nearly unambiguous (buses can stray
   off-route). The musical payload is also forgiving of small position lag — a note
   triggered slightly late at a trigger point is still musically coherent.

The only genuine unknown is **feed update cadence** (Section 6), handled as a build-time
spike rather than pre-engineered.

---

## 2. Verified Facts (grounding for the design)

| Fact | Evidence |
| --- | --- |
| MARTA static GTFS has 4 heavy-rail routes as `route_type=1` | `routes.txt`: `26984 BLUE`, `26985 GOLD`, `26986 GREEN`, `26987 RED` |
| Rail `route_short_name` values are exactly `BLUE/GOLD/GREEN/RED` | matches the feed's `LINE` field verbatim |
| Rail shapes exist and are dense | shape point counts 4,345 (GREEN) – 22,936 (RED) |
| `GtfsStaticLoader` ingests **all** routes — no `route_type` filter | `GtfsStaticLoader.cs` never reads `route_type`; stores every route in `trips.txt`/`routes.txt` |
| Shapes are stored keyed by `routeId`, with `routeShortName` in properties | `GtfsStaticLoader.cs:56`, `BuildLineStringFeature` |
| Worker indexes shapes by `RouteShortName ?? RouteId` | `Worker.cs:57` |
| ⇒ Rail is **already in `_routeIndex`** under keys `"BLUE"`, `"GOLD"`, `"GREEN"`, `"RED"` | derived from the two rows above |

**Consequence:** The rail line geometry is already loaded server-side today. No
GTFS-static change, no shape work. The feed's `LINE: "RED"` maps to the index key
`"RED"` with **zero translation**.

---

## 3. The Rail Realtime Feed

**Endpoint:**
```
GET https://developerservices.itsmarta.com:18096/itsmarta/railrealtimearrivals/developerservices/traindata?apiKey={KEY}
```

**Auth:** query-param API key. Sign up required. **The key must NOT be committed** —
load from configuration / environment (per the feature-012 FR-020 secret-handling
precedent). See Section 7.

**Response:** JSON array. One element **per (train, upcoming-station)** — so a single
`TRAIN_ID` appears multiple times (once for each station it is predicted to reach), each
row carrying the *same* live train `LATITUDE/LONGITUDE` but a different
`STATION`/`NEXT_ARR`/`WAITING_SECONDS`.

Sample element (fields the design uses in **bold**):

```json
{
  "DESTINATION": "Airport",
  "DIRECTION": "S",
  "EVENT_TIME": "01/23/2025 11:50:06 AM",
  "IS_REALTIME": "true",
  "LINE": "RED",              // → route index key
  "NEXT_ARR": "11:52:49 AM",
  "STATION": "AIRPORT STATION",
  "TRAIN_ID": "401",          // → vehicleId
  "WAITING_SECONDS": "107",   // → cadence/adaptive-lerp signal (later, if needed)
  "WAITING_TIME": "1 min",
  "DELAY": "T582S",
  "LATITUDE": "33.660274",    // → live position
  "LONGITUDE": "-84.447091"
}
```

Notes:
- All values are JSON **strings**; numeric fields require parsing
  (`double.TryParse` with `InvariantCulture`).
- `IS_REALTIME` may be `"false"` for schedule-based predictions — these rows should be
  treated cautiously (Section 5, open question OQ-2).

---

## 4. Architecture — Reuse, Don't Rebuild

The integration is an **adapter problem, not an architecture problem.** The rail feed is
normalized into the same shape the existing reconciliation loop consumes, so
snap / lerp / telemetry / SignalR all work unchanged.

```
                         EXISTING (unchanged)
  ┌────────────────────────────────────────────────────────────────┐
  │  ProcessSpatialReconciliationAsync(feed)                         │
  │    loop entities → snap to _routeIndex → lerp deltas →           │
  │    SnapEventArgs / LerpEventArgs / CycleEventArgs →              │
  │    RouteNearestPointBatchEvent → SignalR → client                │
  └────────────────────────────────────────────────────────────────┘
        ▲                                            ▲
        │ FeedMessage (buses, protobuf)              │ FeedMessage (trains, adapted)
        │                                            │
  FetchGtfsRtFeedAsync                         RailRealtimeAdapter  ◄── NEW
  (existing)                                   - fetch JSON
                                               - dedup by TRAIN_ID
                                               - build FeedEntity/VehiclePosition
```

### 4.1 The adapter contract

The adapter produces the **same `FeedMessage` shape** (`GtfsRtModels.cs`) the worker
already consumes, so the reconciliation loop need not distinguish buses from trains:

| Reconciliation field | Source from rail feed |
| --- | --- |
| `FeedEntity.Id` | `TRAIN_ID` |
| `VehiclePosition.Vehicle.Id` (`vehicleId`) | `TRAIN_ID` |
| `VehiclePosition.Trip.RouteId` (`routeId`) | `LINE` (`"RED"` etc.) — matches index key |
| `Position.Latitude` / `Longitude` | parsed `LATITUDE` / `LONGITUDE` |
| `Position.Speed` | *null* (feed has no speed; see Section 6) |
| `Position.Bearing` | *null* (derive later if needed — Section 6) |
| `VehiclePosition.Timestamp` | parsed `EVENT_TIME` (UTC) — drives the stale-sample check at `Worker.cs:197` |

**De-dup (load-bearing):** Collapse the per-station rows to **one row per `TRAIN_ID`**
before building entities. The collapsed rows for one train should share an identical
`LATITUDE/LONGITUDE` — which, observed during the build, is the in-flight confirmation
that the lat/lon is a *live train* position and not a per-station coordinate. (If they
differ per station, the "live position" assumption is wrong — see OQ-1.)

### 4.2 Worker integration

The cleanest integration keeps both feeds flowing into the same loop on the same
`PeriodicTimer` tick (`Worker.cs:41`):

```
while (timer tick) {
    busFeed   = await FetchGtfsRtFeedAsync(ct);        // existing
    railFeed  = await railAdapter.FetchAsync(ct);      // NEW
    var merged = Merge(busFeed, railFeed);             // concat entities
    if (merged != null && _routeIndex != null)
        await ProcessSpatialReconciliationAsync(merged, ct);
}
```

Because vehicle IDs are namespaced naturally (bus IDs vs. `TRAIN_ID`) and route keys
differ (numeric bus routeIds vs. `RED/GOLD/BLUE/GREEN`), there is **no collision risk**
in `_vehicleStateCache`. Trains simply become additional entities in the existing batch.

> **Alternative considered:** a separate timer / separate cache for trains. Rejected —
> it duplicates the loop and the telemetry plumbing for no benefit. Merging into one feed
> is the minimal change.

---

## 5. Client Impact — None (by design)

Trains ride the **existing** `RouteNearestPointBatchEvent` → SignalR → map/synth path.
The client already renders any vehicle the batch contains and plays its route's voice.

- **Map:** trains appear as vehicle markers snapped to rail shapes already on the map.
- **Audio:** `RED/GOLD/BLUE/GREEN` need instrument/voice assignments in the existing
  Tone.js palette (route = instrument). This is a **data/config** addition in the
  existing soundscape model — see `select-neighborhood-tones` skill and
  `specs/009-transit-soundscape/` — **not** a code change. If the palette auto-assigns by
  route key, even this is free.
- **Route filter / multi-select** (`specs/015`, `specs/020`): rail routes will surface in
  the existing filter UI automatically since they are routes in the index. Acceptable and
  arguably desirable; no code change.

**Net client code change: none anticipated.** Only possible client touch is a tone/voice
data entry for the four lines, and only if the palette doesn't auto-assign.

---

## 6. Cadence & Motion Model — Prediction-Paced Lerp (RESOLVED, see OQ-3)

> This section originally proposed deferring the lerp decision to a build-time spike. The
> spike was instead run up front (OQ-3, Section 10) and **resolved the question
> conclusively**: the feed refreshes in coarse, irregular steps (holds of 0 m, then jumps
> up to ~820 m / 11 s ≈ 168 mph). Raw fixed-window lerp is therefore rejected.

**v1 motion model — reuse the existing route-aware animator (no new client subsystem):**

The client animator (`wwwroot/js/vehicle-animator.js`) already interpolates *along the
route polyline* and extrapolates vehicles forward on an *empirical speed* between samples,
with a `MAX_EXTRAPOLATION_MS` coast cap and per-sample re-anchoring. That is precisely the
route-aware motion model the coarse rail cadence requires. Trains reuse it unchanged:

- During `0,0,0` feed holds the animator coasts the train forward along the rail and
  re-anchors on the next real GPS step — no freeze, no teleport.
- The `MAX_EXTRAPOLATION_MS = 30000` cap + re-anchor handle the impossible 820 m catch-up
  step (re-anchor rather than animate the dash).

**Optional refinement (only if needed):** if empirical-speed coasting from coarse rail
samples looks noisy in the running app, pace interpolation by the feed's own ETA —
advance along the polyline toward the next `STATION` at `distance_remaining /
WAITING_SECONDS`. This is a *tuning step*, not a prerequisite, and is the only path that
would add client logic.

**Speed/bearing:** absent from the feed. The reconciliation loop and telemetry already
tolerate null speed/bearing (buses omit them ~40% of the time per the `mj-gtfs` notes).
Bearing can be derived for free from the along-shape direction; speed stays null in v1.

**Why this is showcase-quality, not over-engineering:** the coarse cadence is a measured
fact (OQ-3). Raw straight-line lerp would freeze-then-teleport trains — but the animator
isn't raw lerp; it's route-aware extrapolation that already absorbs this cadence. v1
therefore gets honest gliding motion for free.

**Explicitly out of scope for v1:** ETA-paced interpolation (the optional refinement
above), derived speed, a rail-distinct voice family, and keeping `IS_REALTIME=false`
trains (OQ-2 drops them). Derived bearing is free and may land in v1; everything else is
a follow-up only if the running app demands it.

---

## 7. Security & Config

- **API key:** load `Marta:RailRealtime:ApiKey` (and the base URL) from configuration /
  environment / user-secrets. **Never commit the key.** Mirrors the feature-012 FR-020
  remediation (committed Azure key → env var).
- **TLS:** endpoint is on port `18096` with HTTPS; use the standard
  `IHttpClientFactory` client. Validate the cert normally (no override).
- **Failure handling:** the adapter must be **best-effort** — a failed or empty rail
  fetch must not break the bus path. On error, log a warning and return an empty feed so
  the merged feed still carries buses (mirror `FetchGtfsRtFeedAsync`'s null-on-failure
  behavior at `Worker.cs:550`).

---

## 8. Files Touched

| File | Change | Side |
| --- | --- | --- |
| `…/TransitDataWorker/RailRealtime/RailRealtimeAdapter.cs` | **NEW** — fetch JSON, dedup by `TRAIN_ID`, emit `FeedMessage` | Server |
| `…/TransitDataWorker/RailRealtime/RailArrivalDto.cs` | **NEW** — JSON DTO mirroring the feed | Server |
| `…/TransitDataWorker/Worker.cs` | fetch rail feed + merge entities into reconciliation tick | Server |
| `…/TransitDataWorker/Program.cs` (or DI extension) | register adapter + named HttpClient + config binding | Server |
| `appsettings*.json` | add `Marta:RailRealtime` base URL (key via secrets/env) | Server |
| GtfsStatic loader | **none** — rail already ingested | — |
| Client (map / synth / SignalR) | **none** — animator + synth auto-handle any route key (OQ-3, OQ-4) | — |
| Tone/voice palette data | **none** — `transit-synth.js` auto-assigns voices by hashing the route key (OQ-4) | — |

---

## 9. Verification Plan

1. **Dedup guard (OQ-1, resolved):** Retain the runtime assertion — collapsed rows per
   `TRAIN_ID` must share one `LATITUDE/LONGITUDE`. Already empirically true; the assert
   fails loudly if MARTA changes the contract.
2. **Snap correctness:** Confirm rail entities snap with small `SnapDistanceKm` against
   `RED/GOLD/BLUE/GREEN` shapes (telemetry `snap` dataset via `mj-data-explorer`).
3. **Motion in running app (OQ-3, resolved):** Watch trains in-app — the animator should
   coast them along the rail through `0,0,0` holds and re-anchor on steps without
   freeze/teleport. Apply the optional ETA-pacing refinement *only* if coasting looks
   noisy.
4. **Voice assignment (OQ-4, resolved):** Confirm rail keys preload and play a trio voice;
   no manual palette entry needed.
5. **No bus regression:** Bus counts in `CycleEventArgs` unchanged when rail feed is
   toggled off vs. on (merge must be additive only).
6. **Realtime filter (OQ-2):** Confirm `IS_REALTIME != "true"` rows are dropped before
   dedup.
7. **Key safety:** Confirm no key in committed config; app starts from env/secrets.

---

## 10. Open Questions

| ID | Question | Resolution path |
| --- | --- | --- |
| ~~OQ-1~~ | ~~Is `LATITUDE/LONGITUDE` truly the live train position (not station-snapped)?~~ | **RESOLVED — see below** |
| ~~OQ-2~~ | ~~How to treat `IS_REALTIME == "false"` rows (scheduled, not real)?~~ | **RESOLVED — see below** |
| ~~OQ-3~~ | ~~Feed update cadence (drives lerp decision)~~ | **RESOLVED — see below** |
| ~~OQ-4~~ | ~~Does the Tone.js palette auto-assign voices by route key, or need explicit rail entries?~~ | **RESOLVED — see below** |

### OQ-2 — RESOLVED (2026-06-23): drop `IS_REALTIME == "false"` rows in v1

The adapter filters out any row whose `IS_REALTIME` is not `"true"` before dedup. Only
genuine GPS positions enter the pipeline. Rationale:

- **Consistent with the design thesis** — lerp between *real* samples is honest;
  rendering a schedule-estimated position is exactly the "people know when it's wrong"
  failure the feature set out to avoid.
- **Trivial to implement** — a single predicate in the adapter; no pipeline or client
  plumbing.
- **Low cost** — only ~16 trains run system-wide at peak; losing the occasional
  non-realtime train (typically at terminals pre-departure or during GPS dropout) does
  not empty the map.

Revisit only if the cadence spike (OQ-3) shows too few realtime trains for the soundscape
to feel alive — in which case escalate to "keep but flag" (dimmed marker / muted voice),
not unconditional keep.

> Note: the 2026-06-23 probe returned **all** rows as `IS_REALTIME: "true"`, so the
> drop filter was a no-op on that sample. It is still applied defensively.

### OQ-4 — RESOLVED (2026-06-23): palette auto-assigns by route key — no rail entries, no code/data change

`transit-synth.js` maps route → instrument by hashing the route key: `instrumentFor(routeId)`
computes `djb2(routeId) % PALETTE.length` and picks a voice slot
(transit-synth.js:78–84). This is deterministic and works for **any** route key, so
`RED`/`GOLD`/`BLUE`/`GREEN` are assigned a trio voice (bassoon / viola / cello)
automatically. **No code change, no manual voice data.**

Caveats (recorded, not blocking):
1. **3-voice cycle vs. 4 lines** — by pigeonhole, at least two rail lines share a voice.
   Trains sound musical but not each-uniquely-instrumented. This matches how buses
   already behave (dozens of routes over 3 voices) and is acceptable for v1. Giving rail
   a *distinct* voice family from bus is a future palette enhancement, not a requirement.
2. **Preload** — `preload(routeIds)` (transit-synth.js:121) should receive the rail keys
   so samplers warm up. Since trains flow through the same batch/path as buses, this
   likely happens automatically; confirm during the build.

### OQ-3 — RESOLVED (2026-06-23): coarse/irregular cadence → prediction-paced lerp (NOT raw lerp)

Probed the live endpoint 4× at ~11 s spacing and measured per-train Haversine movement
between consecutive samples. The feed does **not** refresh smoothly on a ~10 s tick:

| Train | Line | Δ between samples (m) | Interpretation |
| --- | --- | --- | --- |
| 109, 306, 405, 408 | mixed | `0, 0, 0` | dwelling / GPS not refreshed |
| 401 | RED | `0, 0, 210` | held, then a step |
| 203 | GREEN | `0, 0, 522` | held, then a large step |
| 402 | RED | `0, 820, 366` | **820 m in ~11 s ≈ 168 mph — physically impossible** |
| 305 | GOLD | `278, 165, 327` | the only train moving plausibly per-sample |

The 820 m jump proves the feed reports position in **coarse, irregular steps** — it
catches up after several stale polls rather than streaming continuous motion.
`EVENT_TIME` changes don't reliably coincide with position changes.

**Consequence:** raw fixed-window lerp (the bus default) is **insufficient** for trains.
It would freeze a train during `0,0,0` holds, then either teleport it or animate an
absurd 168 mph dash on the catch-up step — the most visible form of the
"people know when it's wrong" failure, made worse by the clean, recognizable rail
geometry.

**Resolution — reuse the existing route-aware animator; ETA-pacing is an optional
refinement, not a new subsystem.** After resolving OQ-3, inspection of the client
animator (`wwwroot/js/vehicle-animator.js`) showed it **already implements the motion
model OQ-3 calls for**:

- It interpolates **along the route polyline** (`interpolateAlongPath` / `extractSubPath`),
  not straight-line.
- It derives an **empirical speed** from a history ring buffer and **extrapolates the
  vehicle forward along the route** (`extrapolateAlongRoute`) between samples — i.e. it
  coasts a vehicle along the line at its computed speed when the feed goes quiet.
- It already handles the exact failure modes the coarse cadence creates: stale-sample
  re-anchoring, route-transfer teleport, and a `MAX_EXTRAPOLATION_MS = 30000` cap that
  idles a vehicle when no fresh data arrives.

So during the `0,0,0` holds the animator coasts the train forward on empirical speed and
re-anchors on the next real step, rather than freezing. The one residual concern is that
empirical speed computed from *coarse rail samples* (incl. the 820 m catch-up step) may
be noisy; the existing cap + re-anchor logic blunts it.

**Net scope impact (revised down):** v1 can likely **reuse the animator as-is** — trains
ride the same route-aware extrapolation buses already use. The `WAITING_SECONDS` /
`NEXT_ARR` ETA-pacing described above becomes an **optional refinement** to try only if
empirical-speed coasting looks wrong on rail in the running app. This keeps the
"server-side only, minimal client impact" thesis intact (see Section 6 + Section 5).

**Guardrail (already present):** the animator caps coasting via `MAX_EXTRAPOLATION_MS`
and re-anchors on each fresh snapped position; an impossible catch-up step re-anchors
rather than animating an absurd dash. Bearing can be derived from the along-shape
direction for free; speed stays null from the feed.

### OQ-1 — RESOLVED (2026-06-23): `LATITUDE/LONGITUDE` is the live train position

Probed the live endpoint directly (264 rows, 16 distinct trains). Grouped every row by
`TRAIN_ID` and counted distinct coordinates per train:

- **Every train has exactly one distinct coordinate across all its rows** —
  `distinctCoords = 1` for all 16 trains, including train `109` which spans **11 different
  upcoming stations**. If the lat/lon were the station coordinate, that train would show
  11 distinct coords; it shows 1.
- **Geographic sanity check:** Train `301` (GOLD, 8 upcoming stations North Ave →
  Doraville) sits at `33.766521,-84.387387` (near North Ave/Midtown) — i.e. *upstream of*
  its first upcoming station, exactly where a live train would be. Same pattern for the
  other multi-station trains.

**Conclusion:** the coordinate is the train's live track position, identical across that
train's per-station rows. The adapter therefore picks **any row per `TRAIN_ID`** after
dedup. The "all rows share one coordinate" assertion (Section 9, Step 1) is retained as a
cheap contract guard that fails loudly if MARTA ever changes this.

**Bonus findings from the probe:**
- The endpoint returned **HTTP 200 with no API key** on 2026-06-23. The key may be
  optional for this deployment, but the design still treats it as configurable
  (Section 7) — do not hard-depend on keyless access.
- `EVENT_TIME` is present and per-row (e.g. `06/23/2026 10:52:57 PM`), parseable for the
  `VehiclePosition.Timestamp` staleness field.

---

## 11. Summary

Trains are **low-risk depth**: the rail geometry is already loaded, the feed's `LINE`
maps to the route index with zero translation, the feed gives a real per-train position,
and the entire snap/lerp/telemetry/SignalR pipeline is reused by normalizing the rail
JSON into the existing `FeedMessage` shape. All net-new code is a single best-effort
adapter plus a merge line in the worker. **Server-side only; no client code change
anticipated.** The sole genuine unknown — feed cadence — is deferred to a build-time
spike rather than pre-engineered.
