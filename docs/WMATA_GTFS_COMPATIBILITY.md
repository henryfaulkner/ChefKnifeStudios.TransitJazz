# WMATA (Washington DC) — GTFS Compatibility Assessment

**Evaluated:** 2026-06-26  
**Agency:** Washington Metropolitan Area Transit Authority (WMATA)  
**System:** Metrobus (buses) + Metrorail (heavy rail)  
**Auth:** All four feed endpoints require an `api_key` header. Register at
[developer.wmata.com](https://developer.wmata.com) for a free key (Default Tier: 10
req/s, 50k req/day).

---

## Summary Verdict

| Feed | Verdict | Blocker |
|------|---------|---------|
| Buses (GTFS-RT) | **COMPATIBLE — zero transform** | none |
| Rail (GTFS-RT) | **PARTIALLY COMPATIBLE — one lookup map** | RT uses full color names; static keys by single letter |

WMATA is an excellent first multi-city candidate. Buses drop in with no code changes to
the reconciliation logic. Rail needs a six-entry name→letter map in the worker, but
crucially **no new adapter** — WMATA publishes Metrorail live positions directly in
GTFS-RT protobuf, the same format as buses. This is the opposite of MARTA, where rail
required a bespoke JSON adapter.

---

## Feed Inventory and Endpoints

All four endpoints require the header `api_key: <your-key>`.

| Feed | URL | Format |
|------|-----|--------|
| Bus static GTFS | `https://api.wmata.com/gtfs/bus-gtfs-static.zip` | ZIP of CSVs |
| Bus GTFS-RT (live positions) | `https://api.wmata.com/gtfs/bus-gtfsrt-vehiclepositions.pb` | Binary protobuf |
| Rail static GTFS | `https://api.wmata.com/gtfs/rail-gtfs-static.zip` | ZIP of CSVs |
| Rail GTFS-RT (live positions) | `https://api.wmata.com/gtfs/rail-gtfsrt-vehiclepositions.pb` | Binary protobuf |

**Note:** Bus and rail are split across separate static zips and separate RT feeds.
The bus static zip has zero `route_type=1` routes; the rail static zip has six.

### How to fetch (PowerShell)

```powershell
$key = $env:WMATA_API_KEY   # never hardcode; store in env/secrets

# Bus GTFS-RT
$bytes = (Invoke-WebRequest -Uri "https://api.wmata.com/gtfs/bus-gtfsrt-vehiclepositions.pb" `
    -Headers @{ "api_key" = $key } -UseBasicParsing).Content
[System.IO.File]::WriteAllBytes("$env:TEMP\wmata-bus-rt.pb", $bytes)

# Bus static zip
Invoke-WebRequest -Uri "https://api.wmata.com/gtfs/bus-gtfs-static.zip" `
    -Headers @{ "api_key" = $key } -OutFile "$env:TEMP\wmata-bus-static.zip" -UseBasicParsing

# Rail GTFS-RT
$bytes = (Invoke-WebRequest -Uri "https://api.wmata.com/gtfs/rail-gtfsrt-vehiclepositions.pb" `
    -Headers @{ "api_key" = $key } -UseBasicParsing).Content
[System.IO.File]::WriteAllBytes("$env:TEMP\wmata-rail-rt.pb", $bytes)

# Rail static zip
Invoke-WebRequest -Uri "https://api.wmata.com/gtfs/rail-gtfs-static.zip" `
    -Headers @{ "api_key" = $key } -OutFile "$env:TEMP\wmata-rail-static.zip" -UseBasicParsing
```

---

## Verified Feed Facts (2026-06-26 snapshot)

### Bus GTFS-RT

| Metric | Value |
|--------|-------|
| Feed size | 67,420 bytes |
| Header timestamp | 0 (normal — same behavior as MARTA) |
| Total entities | 762 |
| Vehicle entities | 762 |
| With `route_id` | 555 (72.8%) |
| Without `route_id` | 207 (27.2%) |
| `lat`/`lon` present | 100% |
| `speed` present | 45.9% |
| `bearing` present | 41.2% |
| `vehicle.timestamp` present | 100% |

The 207 vehicles without `route_id` are real buses in the feed — likely deadheading or
out-of-service. They carry valid lat/lon but no trip assignment. The RT `route_id` value is
consumed into the worker's internal `routeJoinKey`, so a missing value here means no
`routeJoinKey` can be derived; the worker will count them as `skippedNoJoinKey`, which is
correct behavior.

### Bus Static GTFS

| Metric | Value |
|--------|-------|
| Routes | 128 total / 128 with shapes / 0 without |
| Rail routes (`route_type=1`) | **0** — rail is in the separate rail static zip |
| Total shape points | 172,333 |
| Route index key format | Alphanumeric short names: `A11`, `C21`, `M44`, `P96`, etc. |

### Bus Route ID Alignment

RT `route_id` values are identical to static `route_short_name` values — same
alphanumeric format, no prefix, no padding, no transform.

| Metric | Value |
|--------|-------|
| Distinct RT route IDs (live snapshot) | 107 |
| Static index keys | 128 |
| Matched | **107 (100%)** |
| Unmatched RT IDs | **none** |
| Unmatched static keys | 21 (routes in schedule with no active vehicles at snapshot time) |

The 21 static-only keys (`A25`, `A28`, `A29`, `A49`, `A90`, `C77`, `C85`, `C87`,
`D82`, `EXP`, `F19`, `F26`, `F28`, `F29`, `F81`, `F83`, `LCL`, `M6X`, `M82`, `P15`,
`P97`) are routes that exist in the timetable but had no live vehicles at the moment
of evaluation. This is expected — they will appear in the RT feed when active.

### Rail GTFS-RT

| Metric | Value |
|--------|-------|
| Feed size | 14,566 bytes |
| Header timestamp | 0 (normal) |
| Total entities | 102 |
| Vehicle entities | 102 |
| With `route_id` | 93 (91.2%) |
| Without `route_id` | 9 (8.8%) |
| `lat`/`lon` present | 100% |
| `speed` present | **0%** — Metrorail does not publish speed in its GTFS-RT feed |
| `bearing` present | 98.0% |
| `vehicle.timestamp` present | 100% |

RT `route_id` values seen: `BLUE`, `GREEN`, `NR`, `ORANGE`, `RED`, `SILVER`, `YELLOW`

The 9 vehicles without `route_id` use the internal `NR` designation (non-revenue /
yard moves). They will be counted as `skippedNoJoinKey`.

### Rail Static GTFS

| Metric | Value |
|--------|-------|
| Rail routes (`route_type=1`) | 6 |
| Non-rail routes | 1 (`SHUTTLE`, `route_type=3`) |
| Total shape points | 2,429 |
| Rail `route_short_name` values (= index keys) | `B`, `G`, `O`, `R`, `S`, `Y` |

### Rail Route ID Alignment — The Mismatch

| RT `route_id` | Static `route_id` | Static `route_short_name` (index key) |
|---------------|-------------------|---------------------------------------|
| `BLUE`   | `BLUE`   | **`B`** |
| `GREEN`  | `GREEN`  | **`G`** |
| `ORANGE` | `ORANGE` | **`O`** |
| `RED`    | `RED`    | **`R`** |
| `SILVER` | `SILVER` | **`S`** |
| `YELLOW` | `YELLOW` | **`Y`** |

The static `route_id` column *happens* to use the full color name (same as RT), but the
worker indexes by `routeJoinKey` (`RouteShapeProperties.JoinKey`, i.e. `route_short_name ??
route_id`). WMATA's rail static GTFS uses single-letter short names, so the index keys are
`B`, `G`, `O`, `R`, `S`, `Y` — not the full names. Without a transform, all 93 rail vehicles
become `skippedUnknownRoute`.

**Fix:** a six-entry map applied to each rail entity's wire `route_id` before the worker
looks it up in the index by `routeJoinKey`:

```
BLUE   → B
GREEN  → G
ORANGE → O
RED    → R
SILVER → S
YELLOW → Y
```

This is the *only* code change required to make Metrorail work.

---

## Notable Call-outs

### 1. Rail is GTFS-RT, not a JSON sidecar — no new adapter needed

MARTA's rail integration required a `RailRealtimeAdapter` because MARTA's train feed is
a proprietary JSON API (one row per upcoming station per train). WMATA is different:
Metrorail live positions are published in standard GTFS-RT protobuf, the same binary
format as buses. The existing vehicle-position decode path handles it already. The only
thing standing between "zero trains on map" and "all trains on map" is the six-entry
route_id lookup map above.

### 2. Bus route IDs are structurally identical to MARTA's convention

Both MARTA and WMATA key their bus routes by `route_short_name`, and both match the RT
`route_id` exactly. If the worker is made configurable for multiple cities, no
per-agency bus transform is needed for either Atlanta or DC.

### 3. Rail speed is always absent — lerp telemetry will have sparse speed fields

Metrorail publishes `bearing` on 98% of vehicles but `speed` on 0%. The worker's lerp
dataset records speed from the GTFS-RT `position.speed` field; those rows will always
be `null` for WMATA rail. This does not block snapping or map rendering — speed is
optional — but it means speed-dependent soundscape logic (if any is added) would need
to handle the null case for WMATA trains.

### 4. Header timestamp is always 0 — same as MARTA, not an error

Both WMATA feeds return `header.timestamp = 0`. The decoder already treats this as
normal (confirmed MARTA behavior). No special handling needed.

### 5. Bus and rail require two separate static zips

Unlike some agencies that publish a single combined GTFS zip, WMATA publishes bus and
rail static data separately. The `GtfsStaticLoader` currently fetches a single
hardcoded URL (MARTA's zip). Supporting WMATA means either fetching both URLs and
merging their route indexes, or making the loader accept a list of URLs. Rail routes
load automatically via `route_type` once the rail static zip is included — no other
static-side changes.

### 6. Metrorail has a sixth line — Silver — that MARTA does not

MARTA has 4 rail lines (RED, GOLD, BLUE, GREEN). WMATA has 6 (RED, BLUE, ORANGE,
SILVER, GREEN, YELLOW). The Silver Line is WMATA's newest, opening its full Phase 2
extension to Dulles Airport in 2022. The `SILVER` → `S` transform must be included or
Silver Line trains will be silently skipped.

---

## What Works With No Code Changes

- Bus GTFS-RT decoding (same protobuf format as MARTA)
- Bus position snapping (route IDs align 100%)
- Lerp / snap / cycle telemetry for buses
- Rail GTFS-RT decoding (standard protobuf — no adapter needed)
- Rail position snapping (once the route_id map is applied — the index already works)
- Rail geometry (shapes are in the rail static zip, same structure as bus shapes)

## What Requires Code Changes

| Change | Scope | Effort |
|--------|-------|--------|
| Add `api_key` header to all WMATA HTTP requests | Worker config / secrets | Trivial |
| Make `GtfsStaticLoader` URL(s) configurable; load both bus + rail static zips | `GtfsStaticLoader.cs` | Small |
| Make `_gtfsRtUrl` configurable for bus | `Worker.cs` | Small |
| Add WMATA rail GTFS-RT feed to the merge in `ExecuteAsync` | `Worker.cs` | Small |
| Apply `BLUE→B` etc. map when keying rail vehicles into the route index | `Worker.cs` or a thin normalization helper | Trivial |

No new DTO, no new adapter, no new interop, no client changes.

---

## What is NOT Available / Out of Scope

- **Metrobus `vehicle_id`:** WMATA's bus GTFS-RT does not populate the
  `VehicleDescriptor.id` field. Entity IDs are present but `vehicle_id` will always be
  `null` in the worker's model for WMATA buses. MARTA buses also lack this in practice.
- **Rail speed:** As noted above, always absent in the WMATA rail feed.
- **`NR` (non-revenue) trains:** 9 entities in the rail feed carry no `route_id` (the
  internal `NR` code surfaces as `skippedNoJoinKey`). These are yard/maintenance moves
  and are correctly excluded.
- **Commuter rail (MARC / VRE):** These agencies serve the DC metro area but are
  separate from WMATA and not evaluated here.
