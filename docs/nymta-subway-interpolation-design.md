# NymtaCity — Subway Position-Interpolation Design

**Status:** Design sketch (no code written)
**Author:** —
**Date:** 2026-07-12
**Prereq reading:** [`docs/city-compat/nymta.md`](city-compat/nymta.md) (feed evaluation), the
`ITransitCity` abstraction (`src/Server/.../TransitDataWorker/Cities/`), and `Worker.cs`
(`ProcessSpatialReconciliationAsync`).

---

## 1. Problem statement

NYC subway GTFS-RT **never carries `position.lat/lon`**. It is a stop-arrival-prediction feed:
each vehicle entity populates `{ trip.route_id, current_stop_sequence, current_status,
stop_id, timestamp }` and nothing else. NYCT tracks trains by fixed-block signal occupancy,
not GPS, so there is no coordinate to publish — this is by design, confirmed exhaustively in
the feed evaluation (field 2 absent across two live pulls, every entity).

Everything downstream of the transit loop assumes a real coordinate:

```csharp
// Worker.cs, ProcessSpatialReconciliationAsync
if (entity.Vehicle?.Position == null) continue;   // <-- every NYC subway entity dies here
```

So NYC subway run through the generic config-driven `GtfsRtCity` produces a feed where **every
entity is skipped** and the map stays empty. Making trains visible requires **synthesizing a
`Position`** the feed does not contain.

## 2. Design principle — keep the loop dumb

The `ITransitCity` contract (locked in the multi-city grill-me, Q7-A) is:

```csharp
public interface ITransitCity
{
    string Name { get; }
    Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct);
    bool EmitsTelemetry { get; }
}
```

> `FetchVehiclesAsync` returns a **fully-normalized merged `FeedMessage`** — the loop is
> permanently dumb and NEVER asks "does this city have rail?".

**All subway complexity lives inside `NymtaCity.FetchVehiclesAsync`.** By the time a
`FeedEntity` leaves that method it carries a real `Position` and is indistinguishable from a
MARTA bus. `Worker.cs` is untouched. `Program.cs` gains exactly one registration branch. This
is the whole reason the adapter pattern is worth having here: it *quarantines* the
NYC-specific algorithm to one class instead of leaking `if (city == "nymta")` into the shared
snap/lerp/crossing pipeline (the anti-drift smell).

`NymtaCity` is a **bespoke** adapter (like `MartaCity`), NOT a `GtfsRtCity` config entry —
because it synthesizes data rather than decoding it.

## 3. The interpolation algorithm

### 3.1 Inputs, per subway vehicle entity

| Field | Example | Role |
|-------|---------|------|
| `trip.route_id` | `A`, `6`, `7X` | Route → shape geometry (already 100% aligned, single-letter keys) |
| `current_stop_sequence` | `37` | Which scheduled stop the train is working toward |
| `current_status` | `InTransitTo` / `IncomingAt` / `StoppedAt` | Whether it's parked or moving between stops |
| `stop_id` | `H13S`, `A06N` | **Station platform code** — the target stop. Suffix `N`/`S` = direction. |
| `timestamp` | unix secs | When this state was observed → drives the fraction-along estimate |

The enum already exists in the codebase (`VehicleStopStatus`: `IncomingAt=0`, `StoppedAt=1`,
`InTransitTo=2`).

### 3.2 Required static lookups (new — see §4)

For each `(route_id)` we need the ordered list of `(stop_id, distanceAlongShapeMeters)` pairs
and each stop's `(lat, lon)`. Concretely:

- **`StationCoord[stop_id]`** → `(lat, lon)` from `stops.txt`.
- **`StopDistOnShape[route_id][stop_id]`** → cumulative metres along the route shape at that
  station, derived from `stop_times.txt` (stop ordering) + the existing shape geometry.

Both are computed **server-side, once, at static-load time** and served to the worker (§4) —
never recomputed on the 10-second hot path (Principle VII).

### 3.3 Per-entity synthesis

```
GivenPosition(entity):
    route   = entity.trip.route_id
    target  = entity.stop_id                     // the station it's heading to / stopped at
    status  = entity.current_status

    targetCoord = StationCoord[target]           // if missing → skip entity (unknown station)

    switch status:
        StoppedAt:
            # Train is at the platform. Snap to the station coordinate exactly.
            return targetCoord

        IncomingAt:
            # Arriving — essentially at the platform for display purposes.
            return targetCoord

        InTransitTo:
            # Train is somewhere on the segment [prevStop -> target].
            prev = stationBefore(target, route, direction)   // from ordered stop list
            if prev is null: return targetCoord              # first stop of the line

            # Fraction of the segment covered, estimated from elapsed time since the
            # observation timestamp against a nominal inter-station run time.
            frac = clamp(elapsedSeconds / nominalRunSeconds(prev, target), 0, 1)

            # Walk the SHAPE geometry between prev and target by cumulative distance,
            # NOT a straight line — subway shapes curve.
            dPrev   = StopDistOnShape[route][prev]
            dTarget = StopDistOnShape[route][target]
            dCurr   = dPrev + frac * (dTarget - dPrev)
            return pointOnShapeAtDistance(route, dCurr)
```

Key algorithmic choices and why:

- **Walk the shape, not the chord.** `pointOnShapeAtDistance` interpolates *along the polyline*
  using the same cumulative-distance array the worker already builds (`_routeCumDist`). A
  straight line between two stations would cut across blocks and look wrong on curved lines
  (e.g. the 7 through Queens). This is the one genuinely new geometric helper.
- **Direction from the `stop_id` suffix.** `N`/`S` (and the NYCT trip extension at proto field
  1001) disambiguates which neighbour is "previous". Without it, `stationBefore` is ambiguous
  at terminals.
- **`frac` is a *smoothing* estimate, not ground truth.** The feed gives no in-segment
  position. We spread the train across the segment by elapsed-time-since-observation so it
  *drifts* between stops on the map instead of teleporting station-to-station every 10 s. It
  will be wrong in the middle of a segment; it will be *right at both endpoints* (the only
  places the feed actually pins the train). Getting the endpoints exactly right is what makes
  it read as correct.
- **`nominalRunSeconds`** can start as a **constant** (say 90 s) and later be refined from
  `stop_times.txt` scheduled deltas. Constant-first is the YAGNI path; it already produces
  believable motion because the endpoints anchor it.

### 3.4 Emitting a normalized entity

Each synthesized position becomes an ordinary `FeedEntity` so the loop treats it like any bus:

```csharp
new FeedEntity
{
    Id = trainId,
    Vehicle = new VehiclePosition
    {
        Vehicle   = new VehicleDescriptor { Id = trainId },
        Trip      = new TripDescriptor    { RouteId = route },   // A, 6, 7X — already index-aligned
        Position  = new Position { Latitude = (float)lat, Longitude = (float)lon },
        Timestamp = entity.Vehicle.Timestamp,
    }
};
```

This is deliberately identical in shape to what `MartaCity.FetchRailEntitiesAsync` already
emits from its JSON rail feed — the precedent to copy.

## 4. Static-data plumbing (the second real cost)

`GtfsStaticLoader` today parses **only** `trips.txt`, `shapes.txt`, `routes.txt` and stores
per-route GeoJSON `LineString`s. The interpolation needs two things it does not currently
produce:

1. **`stops.txt`** → `stop_id → (lat, lon)`.
2. **`stop_times.txt`** → per-trip ordered stop sequence, from which we derive each stop's
   **distance-along-shape** for its route.

### Recommended approach

Compute the stop→shape-offset table **server-side at load time** (co-located with the existing
`Simplify` / cumulative-distance logic) and expose it to the worker via a new endpoint that
mirrors the existing `/gtfs/routes/shapes`:

```
GET /gtfs/subway/stop-offsets?city=nymta
→ [ { routeId, direction, stops: [ { stopId, lat, lon, distMeters }, ... ] }, ... ]
```

`NymtaCity` fetches this once on startup (and on the same 24 h refresh cadence the worker
already uses for the route index), caches it, and reads it on every tick. **No per-tick
re-fetch, no per-tick recompute** — same discipline as `_routeIndex`.

> `stop_times.txt` is large (subway is millions of rows). Parse it **once server-side**,
> collapse to the per-route ordered stop list + offsets, and throw the raw rows away. Never
> ship `stop_times.txt` to the worker.

Why server-side rather than in `NymtaCity`: keeps the worker free of GTFS-zip parsing (it has
none today), reuses the loader's existing shape geometry and cumulative-distance math, and
keeps the "static data → shapes/offsets" transformation in one place.

## 5. RT fan-out

Subway RT is **~8 separate feeds by line group** (`gtfs-ace`, `gtfs-bdfm`, `gtfs-g`,
`gtfs-jz`, `gtfs-nqrw`, `gtfs-l`, `gtfs-1234567`, `gtfs-si`), each keyless. `FetchVehiclesAsync`
fetches all of them, decodes each protobuf, runs §3 synthesis, and merges into one
`FeedMessage` — structurally identical to `MartaCity` merging its bus feed + rail entities, or
`GtfsRtCity` looping `config.GtfsRtUrls`. Failures are per-feed try/catch so one dead line group
doesn't blank the others.

> **Decode note:** the C# `ReadAsStreamAsync` → `ProtoBuf.Serializer.Deserialize<FeedMessage>`
> path used everywhere in this codebase is immune to the PowerShell `text/plain` binary-mangling
> gotcha the feed evaluation hit. No action needed; just don't reintroduce a `.Content`-string
> read.

## 6. Registration & config

**`Program.cs`** — one branch (mirrors the existing MARTA special-case):

```csharp
else if (string.Equals(cfg.Name, CityNames.Nymta, StringComparison.OrdinalIgnoreCase))
    cities.Add(sp.GetRequiredService<NymtaCity>());
```

Plus `builder.Services.AddSingleton<NymtaCity>();` and `CityNames.Nymta = "nymta"`.

**`appsettings.json`** — a `Cities:` entry with the subway static zip + the 8 RT line-group
URLs. (Bus, if added, is a *separate* `GtfsRtCity` config entry — see §8.)

## 7. Edge cases & failure modes

| Case | Handling |
|------|----------|
| `stop_id` not in `stops.txt` | Skip entity (increments an "unknown station" counter, like `skippedUnknownRoute`). |
| `route_id` has no shape | Skip; the route index simply won't contain it. |
| Terminal stop (no previous) | `InTransitTo` with null `prev` → return target coord. |
| `elapsed` far exceeds `nominalRunSeconds` | `frac` clamps to 1 → train sits at the target platform (correct: it's late/held). |
| Missing `current_status` | Treat as `StoppedAt` (pin to station) — safest default, never blanks the train. |
| Feed gap (train reappears far along) | Endpoints re-anchor it; the shared snap window in `Worker.cs` re-establishes `SnapIndex`. |

**Telemetry:** `EmitsTelemetry => false` for NymtaCity initially (per the multi-city decision
Q6 — telemetry stays MARTA-only; don't multiply per-city blobs). The synthesized-position
counters above are worth logging locally regardless.

## 8. Scope boundary — bus is a separate, cheap path

This document is subway/rail **only**. NYC *bus* is `COMPATIBLE` with zero new adapter class —
it reuses the config-driven `GtfsRtCity`, needing only a route-ID normalizer (case-fold,
`+`→`-SBS`, zero-pad strip) before `index.TryGetValue`, plus a second static zip (MTA Bus
Company — already supported by `StaticZipUrls[]`) and an RT API key env var (already supported
by `ApiKeyEnvVar`). Do bus first if you want a quick win; it does not depend on any of the
above.

## 9. Effort summary

| Piece | Where | Size |
|-------|-------|------|
| `pointOnShapeAtDistance` + `stationBefore` + `frac` synthesis | new `NymtaCity.cs` (+ helper) | **Large** — the real feature |
| `stops.txt` + `stop_times.txt` parse → stop-offset table + endpoint | `GtfsStaticLoader` + new API route | **Medium** — net-new static plumbing |
| Per-line-group RT fan-out & merge | `NymtaCity` | Small |
| DI branch + `CityNames.Nymta` + config | `Program.cs`, `CityNames.cs`, `appsettings.json` | Trivial |

**Ballpark: 3–5 focused days**, the bulk in the interpolation helper and in validating the
motion looks sane on the map. The `ITransitCity` seam holds perfectly — the loop and every
downstream stage (snap, lerp, crossing detection, synth) are untouched. The pattern contains
the complexity; it just can't make the missing coordinate appear, which is the irreducible core
of the work.
