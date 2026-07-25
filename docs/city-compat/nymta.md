# MTA — New York City Transit Compatibility

**Agency:** Metropolitan Transportation Authority (MTA) — NYCT Subway, NYCT Bus, MTA Bus
Company  
**City:** New York, NY  
**Evaluated:** 2026-07-12  
**System:** Subway (heavy rail, 20+ trunk lines) + NYCT Bus + MTA Bus Company (combined
citywide bus network)  
**Auth:** Subway static/RT and the `obanyc` bus GTFS-RT endpoint are keyless. The SIRI bus
API (`bustime.mta.info`) requires a free registered key for production use (a `key=test`
placeholder worked for this evaluation but should not ship).

---

## Verdict

| Feed | Result | Notes |
|------|--------|-------|
| Buses | **COMPATIBLE — needs a route-ID normalizer, no new adapter** | 100% RT/static alignment achievable with 4 cheap, systematic transforms |
| Subway (rail) | **INCOMPATIBLE — needs new algorithm, not a config change** | GTFS-RT never carries `position.lat/lon`; only stop-sequence + status + stop_id |

NYC is a strong candidate **for buses only**. 1,788 live, GPS-tracked vehicles citywide
with clean route alignment once IDs are normalized — comparable in shape to MARTA's bus
feed. Subway is architecturally different from every other agency evaluated so far
(MARTA, WMATA, MBTA all publish real lat/lon for rail): NYCT's signal system tracks trains
by track-circuit occupancy, not GPS, so there is no live coordinate to snap to a shape.
Adding subway visualization is possible but requires a **stop-sequence interpolation**
algorithm — a materially new capability, not a lookup-map fix like WMATA's rail.

---

## Feed Endpoints

| Feed | URL | Auth |
|------|-----|------|
| Subway static GTFS zip | `http://web.mta.info/developers/data/nyct/subway/google_transit.zip` | none |
| Subway GTFS-RT (per line group, e.g. ACE) | `https://api-endpoint.mta.info/Dataservice/mtagtfsfeeds/nyct%2Fgtfs-ace` | none |
| NYCT Bus static GTFS zips (5 borough files) | `http://web.mta.info/developers/data/nyct/bus/google_transit_{manhattan,bronx,brooklyn,queens,staten_island}.zip` | none |
| MTA Bus Company static GTFS zip | `http://web.mta.info/developers/data/busco/google_transit.zip` | none |
| Bus GTFS-RT (live positions, all boroughs) | `https://gtfsrt.prod.obanyc.com/vehiclePositions?key=<KEY>` | key (placeholder accepted at evaluation time) |
| Bus SIRI VehicleMonitoring (JSON, alternate) | `https://bustime.mta.info/api/siri/vehicle-monitoring.json?key=<KEY>` | key |

Subway and bus are entirely separate feed families — different static zips, different RT
transports, different route universes. There is no single "MTA feed" the way MARTA
publishes one zip + one `.pb`; this is closer to WMATA's bus/rail split, but with an
additional third static source (MTA Bus Company) on the bus side.

### Fetch pattern (PowerShell)

```powershell
# Subway static
Invoke-WebRequest -Uri "http://web.mta.info/developers/data/nyct/subway/google_transit.zip" `
    -OutFile "$env:TEMP\gtfs-nymta.zip" -UseBasicParsing
Expand-Archive -Path "$env:TEMP\gtfs-nymta.zip" -DestinationPath "$env:TEMP\gtfs-nymta" -Force

# Subway GTFS-RT — MUST read RawContentStream, not .Content (which mangles binary to string)
$resp = Invoke-WebRequest -Uri "https://api-endpoint.mta.info/Dataservice/mtagtfsfeeds/nyct%2Fgtfs-ace" -UseBasicParsing
[System.IO.File]::WriteAllBytes("$env:TEMP\gtfs-rt.pb", $resp.RawContentStream.ToArray())

# Bus static — NYCT (any one borough zip has the full route registry; trips/shapes differ per borough)
Invoke-WebRequest -Uri "http://web.mta.info/developers/data/nyct/bus/google_transit_manhattan.zip" `
    -OutFile "$env:TEMP\gtfs-nymta-bus.zip" -UseBasicParsing
Expand-Archive -Path "$env:TEMP\gtfs-nymta-bus.zip" -DestinationPath "$env:TEMP\gtfs-nymta-bus" -Force

# Bus static — MTA Bus Company (separate operator, separate registry — required for full route coverage)
Invoke-WebRequest -Uri "http://web.mta.info/developers/data/busco/google_transit.zip" `
    -OutFile "$env:TEMP\gtfs-nymta-busco.zip" -UseBasicParsing
Expand-Archive -Path "$env:TEMP\gtfs-nymta-busco.zip" -DestinationPath "$env:TEMP\gtfs-nymta-busco" -Force

# Bus GTFS-RT (protobuf, same format as MARTA/WMATA/MBTA)
$resp = Invoke-WebRequest -Uri "https://gtfsrt.prod.obanyc.com/vehiclePositions?key=$env:MTA_BUS_KEY" -UseBasicParsing
[System.IO.File]::WriteAllBytes("$env:TEMP\gtfs-rt-bus.pb", $resp.RawContentStream.ToArray())
```

> **PowerShell gotcha:** `(Invoke-WebRequest -Uri $url -UseBasicParsing).Content` returns a
> **string**, not `byte[]`, whenever the response's `Content-Type` isn't recognized as
> binary — the MTA gateway serves protobuf as `text/plain`, so `.Content` silently mangles
> the bytes (this cost a full failed decode attempt during evaluation). Always read
> `$resp.RawContentStream.ToArray()` for protobuf feeds, regardless of declared
> `Content-Type`.

---

## Verified Feed Snapshot (2026-07-12)

### Subway GTFS-RT (ACE line group)

| Metric | Value |
|--------|-------|
| Feed size | 69,498 bytes |
| Header timestamp | 0 (normal — same as MARTA/WMATA/MBTA) |
| Total entities | 140 (70 trip_update + 70 vehicle) |
| Vehicle entities | 70 |
| With `route_id` | 70 (100%) — `A`, `C`, `E` |
| **`position.lat`/`lon`** | **0 of 70 (0%)** — verified across every distinct field-shape in the feed |
| `current_stop_sequence` | 100% (e.g. 37, 32, 29) |
| `current_status` | 100% (varint enum) |
| `stop_id` | 100% (e.g. `H13S`, `A06N` — a station platform code, not a coordinate) |
| `vehicle.timestamp` | 100% |

Field-level confirmation (against the real GTFS-RT `.proto` field numbers — `trip`=1,
`position`=2, `current_stop_sequence`=3, `current_status`=4, `timestamp`=5,
`congestion_level`=6, `stop_id`=7, `vehicle`=8, `occupancy_status`=9): every subway
vehicle entity populates fields `{1, 3, 4, 5, 7}` (plus a proprietary NYCT extension at
field 1001 nested inside the trip descriptor, carrying `train_id` and direction). **Field
2 (`position`) is never populated, in any sampled entity, across two independent live
pulls.**

### Subway Static GTFS

| Metric | Value |
|--------|-------|
| Total routes | 29 (28 `route_type=1` heavy rail + 1 `route_type=2` Staten Island Railway) |
| Routes with shape | 29 / 29 (100%) |
| Route index key format | Single alphanumeric: `A`, `C`, `E`, `1`…`7`, `6X`, `7X` |

### Subway Route ID Alignment

| Metric | Value |
|--------|-------|
| RT distinct route IDs (ACE snapshot) | 3 (`A`, `C`, `E`) |
| Static index keys | 29 |
| Matched | **3 / 3 (100%)** |

Route ID alignment is a non-issue — the blocker is entirely the missing position field.

### Bus GTFS-RT (citywide, `obanyc` protobuf)

| Metric | Value |
|--------|-------|
| Feed size | 229,232 bytes |
| Header timestamp | 0 (normal) |
| Total / vehicle entities | 1,788 / 1,788 |
| With `route_id` | **1,788 (100%)** |
| `lat`/`lon` | **100%** |
| `bearing` | 99.8% |
| `speed` | 0% (optional — MTA bus does not publish it; degrades gracefully, same as WMATA rail) |
| `vehicle.timestamp` | 100% |
| Distinct route IDs | 266 |

### Bus Static GTFS

| Metric | Value |
|--------|-------|
| NYCT route registry size | 307 (shared citywide — identical across all 5 borough zips; only `trips.txt`/`shapes.txt` differ per borough) |
| MTA Bus Company route registry size | adds routes NYCT's zips don't cover (Q06–Q115 numbered locals, QM/BXM express) |
| Combined registry (NYCT + Bus Co, case-folded) | 399 keys |

**Gotcha:** all five NYCT borough zip filenames (`google_transit_manhattan.zip`,
`_bronx.zip`, etc.) resolve to genuinely different S3 objects with different sizes, but
`routes.txt` is byte-identical across all of them — it's a shared citywide registry, not a
borough-scoped one. Only `trips.txt` and `shapes.txt` differ per borough. Don't mistake
identical `routes.txt` line counts for a stale/duplicate download.

### Bus Route ID Alignment — Four Systematic Fixes

| Step | Match rate |
|------|-----------|
| Raw (worker's current `routeShortName ?? routeId`, case-sensitive, single static source) | 55.3% (147/266) |
| + case-fold | 70.7% (188/266) |
| + `+` → `-SBS` suffix transform | 76.7% (204/266) |
| + MTA Bus Company as a second static source | 98.5% (262/266) |
| + zero-pad strip (`Q06`→`Q6`) | **100% (266/266)** |

Each mismatch class, with cause:

| RT `route_id` | Static equivalent | Cause |
|---|---|---|
| `BX3` | `route_short_name="Bx3"` | Static short names are mixed-case; RT is uppercase |
| `M15+`, `BX6+` | `route_short_name="M15-SBS"`, `"Bx6-SBS"` | RT uses `+` for Select Bus Service; static uses `-SBS` |
| `Q22`, `BXM4`, … | present only in `busco/google_transit.zip` | MTA Bus Company is a separate operator with its own static registry — NYCT's zips don't include it at all |
| `Q06`, `Q07`, `Q08`, `Q09` | `route_id="Q06"` but `route_short_name="Q6"` | `route_id` is zero-padded, `route_short_name` isn't; RT sends the zero-padded form |

**Fix (apply before `index.TryGetValue`):** uppercase the RT `route_id`, map trailing `+`
→ `-SBS`, strip leading zeros after a letter prefix (`^([A-Z]+)0*(\d.*)$` → group1+group2),
and build the static index from **both** the NYCT and MTA Bus Company registries.

### Rail (heavy rail / `route_type=1`)

| Metric | Value |
|--------|-------|
| Static rail routes | 28 (all subway trunk lines) + Staten Island Railway |
| Rail realtime API | **None exists publicly** — no separate lat/lon source the way MARTA has one |
| Live-position check | **FAIL** — not applicable; no position field is ever sent, by design |

Unlike MARTA (proprietary JSON rail API with live lat/lon) and unlike WMATA/MBTA
(standard GTFS-RT protobuf carrying rail positions in the same feed as buses), NYCT
subway publishes **no live coordinate for trains at all**, through any channel. The
realtime feed is fundamentally a stop-arrival-prediction feed (`current_stop_sequence` +
`current_status` + `stop_id` + `timestamp`), which is how NYCT's fixed-block signal
system reports train state internally.

---

## Notable Call-outs

### 1. Subway realtime has no lat/lon, by design — not a decode bug or a missing feed

Every other agency evaluated so far (MARTA, WMATA, MBTA) publishes a live coordinate for
rail vehicles, either in standard GTFS-RT or a bespoke JSON API. NYCT subway does neither.
This was confirmed exhaustively: a full recursive field dump of multiple vehicle entities,
across two independent live pulls, cross-checked against the real `gtfs-realtime.proto`
field numbers (not assumed ones), shows field 2 (`position`) is simply never present.
`stop_id` (field 7, e.g. `H13S`) is easy to mistake for position data at a glance — it
is a station platform code, not a coordinate.

### 2. Making subway trains visible requires a new algorithm, not a config change

The only path to plotting subway trains is interpolating position along the route
shape: use `current_stop_sequence` + `stop_id` (looked up against `stops.txt` for that
station's lat/lon) + `current_status` (stopped vs. in-transit) + elapsed time since the
last status change, walking the shape geometry between consecutive stops. This is a
different algorithm from `ProcessSpatialReconciliationAsync`'s snap-to-broadcast-position
model — a real feature, comparable in size to the original MARTA rail integration, not a
lookup map like WMATA's six-line fix.

### 3. Bus route ID alignment looks broken at 55% but isn't

A naive first pass (case-sensitive, single static source) matches only 147/266 route IDs
and would look like an INCOMPATIBLE verdict. All four gaps are systematic and cheap to
fix (case-fold, suffix transform, second static source, zero-pad strip) — the real
ceiling is 100%. Don't stop at the raw number; check whether unmatched IDs cluster by a
consistent pattern before concluding a feed is incompatible.

### 4. Bus needs a second static source: MTA Bus Company is a separate operator

NYCT Bus and MTA Bus Company are legally distinct entities (MTA Bus Company absorbed
several former private franchise operators, concentrated in Queens and the Bronx) with
**separate static GTFS zips**. Live RT vehicles from both operators are merged into the
single `obanyc` GTFS-RT feed, so the RT side looks unified — only the static side reveals
the split. Any route-index build for NYC buses must fetch both zips.

### 5. `routes.txt` is shared citywide across all five NYCT borough zips

This looks like a duplicate/stale-download bug the first time you see it (all five zips
report the same `routes.txt` line count) but is real MTA publishing behavior — the route
registry is citywide, only `trips.txt`/`shapes.txt` (actual scheduled service) differ per
borough zip. Verify with `trips.txt` line counts, not `routes.txt`, before concluding a
download failed.

### 6. Header timestamp 0 is normal — consistent with every prior agency

Both the subway and bus RT feeds return `header.timestamp = 0`, matching MARTA, WMATA,
and MBTA. Not a decode error.

### 7. PowerShell binary-download gotcha cost a full failed attempt

`Invoke-WebRequest -UseBasicParsing).Content` silently returns a mangled **string** instead
of `byte[]` when the server's `Content-Type` header doesn't signal binary — MTA's gateway
serves protobuf as `text/plain`. The fix is `$resp.RawContentStream.ToArray()`, always,
regardless of declared content type. Worth checking first on any new agency before
concluding a feed decode failure is a field-mapping problem.

---

## What Works With Zero Code Changes

- Bus GTFS-RT decode (standard protobuf, same format as MARTA/WMATA/MBTA)
- Bus position data completeness (100% route_id, 100% lat/lon, 100% timestamp)
- Subway static route/shape loading (`route_type=1` classification works as-is)
- Subway route ID alignment (3/3 sampled, single-letter keys, no transform needed)

## What Requires Code Changes

| Change | File | Project | Size |
|--------|------|---------|------|
| Bus route-ID normalizer (case-fold + `+`→`-SBS` + zero-pad strip) before `index.TryGetValue` | `Worker.cs` | Worker | Small |
| Fetch + merge MTA Bus Company static zip alongside NYCT zip(s) | `GtfsStaticLoader.cs` | WebAPI | Small |
| Make static zip URL(s) configurable / list-based | `GtfsStaticLoader.cs` | WebAPI | Small |
| Store bus RT API key in env / secrets | `appsettings.json` + env | Both | Config only |
| **Subway position interpolation algorithm** (new, if subway visualization is wanted) | new module, likely alongside `RailRealtimeAdapter` | Worker | **Large — new feature, not a fix** |

## Not Available / Out of Scope

| Item | Status |
|------|--------|
| Subway live position (any channel) | **Does not exist publicly.** NYCT tracks trains by track-circuit occupancy, not GPS. |
| Bus `speed` | Not published by MTA bus GTFS-RT; always null, same handling as WMATA rail |
| Commuter rail (LIRR / Metro-North) | Separate systems under the MTA umbrella; not evaluated here |
| Staten Island Railway realtime | Static routes exist (`route_type=2`); RT feed not evaluated in this pass |
| SIRI JSON bus API | Confirmed working as an alternate to the `obanyc` protobuf feed, but not needed — the protobuf path already matches the Worker's existing decode format |
