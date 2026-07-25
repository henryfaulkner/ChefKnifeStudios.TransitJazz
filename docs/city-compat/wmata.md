# WMATA — Washington DC Transit Compatibility

**Agency:** Washington Metropolitan Area Transit Authority (WMATA)  
**City:** Washington, DC  
**Evaluated:** 2026-06-28  
**System:** Metrobus (buses) + Metrorail (heavy rail: 6 lines)  
**Auth:** All four feed endpoints require an `api_key` header. Register free at
`developer.wmata.com` (Default Tier: 10 req/s, 50 k req/day).

---

## Verdict

| Feed | Result | Notes |
|------|--------|-------|
| Buses | **COMPATIBLE — no code changes to reconciliation** | RT route IDs match static index 100% |
| Rail | **COMPATIBLE — one lookup map in Worker.cs** | RT uses full names; static indexes by single letter |

WMATA is a strong first multi-city candidate. Buses are plug-and-play at the
reconciliation level. Rail needs a six-entry name→letter lookup applied before the
route index lookup, but **no new adapter** — WMATA publishes Metrorail positions in
standard GTFS-RT protobuf (same binary format as buses), unlike MARTA whose rail feed
required a bespoke JSON adapter.

---

## Feed Endpoints

All four require the header `api_key: <your-key>`.

| Feed | URL |
|------|-----|
| Bus static GTFS zip | `https://api.wmata.com/gtfs/bus-gtfs-static.zip` |
| Bus GTFS-RT (live positions) | `https://api.wmata.com/gtfs/bus-gtfsrt-vehiclepositions.pb` |
| Rail static GTFS zip | `https://api.wmata.com/gtfs/rail-gtfs-static.zip` |
| Rail GTFS-RT (live positions) | `https://api.wmata.com/gtfs/rail-gtfsrt-vehiclepositions.pb` |

Bus and rail are split across **separate** static zips and separate RT feeds. The bus
static zip contains zero `route_type=1` routes; the rail static zip contains six.

### Fetch pattern (PowerShell)

```powershell
$key = $env:WMATA_API_KEY  # never hardcode — store in env or secrets

# Bus GTFS-RT
$bytes = (Invoke-WebRequest `
    -Uri "https://api.wmata.com/gtfs/bus-gtfsrt-vehiclepositions.pb" `
    -Headers @{ "api_key" = $key } -UseBasicParsing).Content
[System.IO.File]::WriteAllBytes("$env:TEMP\wmata-bus-rt.pb", $bytes)

# Bus static
Invoke-WebRequest `
    -Uri "https://api.wmata.com/gtfs/bus-gtfs-static.zip" `
    -Headers @{ "api_key" = $key } `
    -OutFile "$env:TEMP\wmata-bus-static.zip" -UseBasicParsing

# Rail GTFS-RT
$bytes = (Invoke-WebRequest `
    -Uri "https://api.wmata.com/gtfs/rail-gtfsrt-vehiclepositions.pb" `
    -Headers @{ "api_key" = $key } -UseBasicParsing).Content
[System.IO.File]::WriteAllBytes("$env:TEMP\wmata-rail-rt.pb", $bytes)

# Rail static
Invoke-WebRequest `
    -Uri "https://api.wmata.com/gtfs/rail-gtfs-static.zip" `
    -Headers @{ "api_key" = $key } `
    -OutFile "$env:TEMP\wmata-rail-static.zip" -UseBasicParsing
```

---

## Verified Feed Snapshot (2026-06-28)

### Bus GTFS-RT

| Metric | Value |
|--------|-------|
| Feed size | 67,420 bytes |
| Header timestamp | 0 (normal — same as MARTA) |
| Total entities | 762 |
| Vehicle entities | 762 |
| With `route_id` | 555 (72.8%) |
| Without `route_id` | 207 (27.2%) — deadheading / out-of-service, correctly skipped |
| `lat` / `lon` | 100% |
| `speed` | 45.9% |
| `bearing` | 41.2% |
| `vehicle.timestamp` | 100% |

### Bus Static GTFS

| Metric | Value |
|--------|-------|
| Total routes | 128, all with shapes |
| Rail routes (`route_type=1`) | **0** — rail is in the separate rail static zip |
| Total shape points | 172,333 |
| Route index key format | Alphanumeric short names: `A11`, `C21`, `M44`, `P96`, … |

### Bus Route ID Alignment

Every `route_id` in the live RT feed matched its static `route_short_name` exactly —
same alphanumeric format, no prefix, no padding.

| Metric | Value |
|--------|-------|
| Distinct RT route IDs (live snapshot) | 107 |
| Static index keys | 128 |
| Matched | **107 / 107 (100%)** |
| Unmatched RT IDs | **none** |
| Static keys with no active vehicle | 21 (schedule-only at snapshot time — expected) |

The 21 static-only keys: `A25 A28 A29 A49 A90 C77 C85 C87 D82 EXP F19 F26 F28 F29 F81 F83 LCL M6X M82 P15 P97`.

### Rail GTFS-RT

| Metric | Value |
|--------|-------|
| Feed size | 14,566 bytes |
| Header timestamp | 0 (normal) |
| Total entities | 102 |
| Vehicle entities | 102 |
| With `route_id` | 93 (91.2%) |
| Without `route_id` | 9 — use `NR` internally (non-revenue / yard moves), correctly skipped |
| `lat` / `lon` | 100% |
| `speed` | **0%** — Metrorail does not publish speed in GTFS-RT |
| `bearing` | 98.0% |
| `vehicle.timestamp` | 100% |

RT `route_id` values seen: `BLUE GREEN NR ORANGE RED SILVER YELLOW`

### Rail Static GTFS

| Metric | Value |
|--------|-------|
| Rail routes (`route_type=1`) | 6 |
| Other routes | 1 (`SHUTTLE`, `route_type=3`) |
| Total shape points | 2,429 |
| Rail `route_short_name` values | `B G O R S Y` |

### Rail Route ID Alignment — The Mismatch

The rail static GTFS uses **single-letter** `route_short_name` values. The Worker
indexes by `routeShortName ?? routeId`, so the index keys are the single letters. The
RT feed emits full color names. Result without a fix: 0% match, all 93 rail vehicles
counted as `skippedUnknownRoute`.

| RT `route_id` | Static `route_id` | Static `route_short_name` (= index key) |
|--------------|-------------------|-----------------------------------------|
| `BLUE`       | `BLUE`            | **`B`**                                 |
| `GREEN`      | `GREEN`           | **`G`**                                 |
| `ORANGE`     | `ORANGE`          | **`O`**                                 |
| `RED`        | `RED`             | **`R`**                                 |
| `SILVER`     | `SILVER`          | **`S`**                                 |
| `YELLOW`     | `YELLOW`          | **`Y`**                                 |

**Fix:** apply this map to WMATA rail entities before the `index.TryGetValue` call in
`Worker.cs:ProcessSpatialReconciliationAsync`:

```
BLUE → B   GREEN → G   ORANGE → O   RED → R   SILVER → S   YELLOW → Y
```

---

## Notable Call-outs

### 1. Rail is GTFS-RT — no new adapter needed

MARTA's rail integration required `RailRealtimeAdapter` because MARTA publishes train
positions as a proprietary JSON API (one row per train × upcoming station). WMATA is
different: Metrorail live positions are standard GTFS-RT protobuf, identical in format
to the bus feed. The existing decode path in `FetchGtfsRtFeedAsync` handles it as-is.
The only gap between zero trains and all trains is the six-entry lookup map above.

### 2. Bus route ID format is structurally identical to MARTA's

Both agencies key buses by `route_short_name`, and both match the RT `route_id` value
exactly. If the Worker is generalized for multiple cities, no per-agency bus transform
is needed for either Atlanta or DC.

### 3. Rail speed is always absent

Metrorail publishes `bearing` on 98% of vehicles but `speed` on 0%. The lerp dataset's
`speed` column will always be `null` for WMATA rail. Snapping and map rendering are
unaffected (speed is optional), but any speed-dependent soundscape logic must handle
null for WMATA trains.

### 4. Header timestamp 0 is normal — not an error

Both WMATA feeds return `header.timestamp = 0`, same as MARTA. The existing decoder
already treats this as normal.

### 5. Two separate static zips, not one combined zip

MARTA publishes a single `google_transit.zip` covering all routes. WMATA splits bus and
rail into separate zips. `GtfsStaticLoader` (in the **WebAPI** project, not the Worker)
currently fetches a single hardcoded URL. Supporting WMATA requires either fetching both
zips and merging the route index, or accepting a list of URLs. Rail routes load
automatically via `route_type=1` once the rail static zip is included — no other
static-side changes.

### 6. Metrorail has six lines, including Silver

MARTA has 4 rail lines. WMATA has 6: Red, Blue, Orange, Silver, Green, Yellow. Silver
(`SILVER → S`) opened its Dulles Phase 2 extension in 2022 and must be in the transform
or Silver Line trains silently drop.

### 7. `vehicle_id` is absent on buses

WMATA's bus RT feed does not populate `VehicleDescriptor.id`. The Worker falls back to
`entity.Id` (a numeric string like `"4727"`). This is already how MARTA buses behave in
practice and poses no problem.

---

## What Works With Zero Code Changes

- Bus GTFS-RT decode (same protobuf format)
- Bus position snapping (route IDs align 100%)
- Rail GTFS-RT decode (standard protobuf — existing fetch path handles it)
- Rail geometry (shapes are in the rail static zip, same CSV structure as buses)
- Snap / lerp / cycle telemetry for buses
- `TransitMode.Rail` classification via `railVehicleIds` set (already in `Worker.cs`)

## What Requires Code Changes

| Change | File | Project | Size |
|--------|------|---------|------|
| Add `api_key` header to bus RT fetch | `Worker.cs` | Worker | Trivial |
| Add WMATA rail GTFS-RT fetch + merge into `ExecuteAsync` | `Worker.cs` | Worker | Small |
| Apply `BLUE→B` etc. map before `index.TryGetValue` for rail entities | `Worker.cs` | Worker | Trivial |
| Make static zip URL(s) configurable; fetch bus + rail zips | `GtfsStaticLoader.cs` | **WebAPI** | Small |
| Store WMATA API key in env / secrets | `appsettings.json` + env | Both | Config only |

The Worker-only boundary cannot be maintained: `GtfsStaticLoader.cs` lives in the
WebAPI project and is the only source of the route index the Worker uses. It must be
touched to load WMATA's route shapes.

---

## Not Available / Out of Scope

| Item | Status |
|------|--------|
| Bus `vehicle_id` | Not published by WMATA; Worker falls back to entity ID |
| Rail speed | Always absent in WMATA rail GTFS-RT |
| Non-revenue trains (`NR`) | 9 entities per snapshot; silently skipped as `skippedNoRouteId` |
| Commuter rail (MARC / VRE) | Separate agencies serving the DC area; not evaluated here |
| Combined bus+rail static zip | WMATA does not publish one; two fetches required |
