---
name: mj-gtfs
description: Fetch and decode transit data from three sources: (1) a GTFS-RT vehicle positions protobuf feed (live bus positions), (2) a static GTFS zip (routes.txt + shapes.txt + trips.txt, incl. route_type so rail routes are identified) from a transit agency's published feed, and (3) an agency-specific rail realtime API (live train positions, e.g. MARTA's JSON traindata feed) where one exists. Outputs clean structured snapshots of each. Use when any skill or the user needs live vehicle/train feed data, static route geometry, or any combination for analysis, compatibility evaluation, or adding a new transit data source.
---

# MJ GTFS

Fetches and decodes GTFS data from two sources and outputs clean structured snapshots.
This is a **data-fetch tool** — it retrieves and formats; callers apply their own logic.

## Sources

| Source | What it contains | Format |
|--------|-----------------|--------|
| GTFS-RT feed | Live vehicle positions: lat/lon, route_id, speed, bearing, timestamps | Binary protobuf |
| Static GTFS zip | Route geometry: routes.txt + shapes.txt + trips.txt from the agency's published feed | ZIP of CSV files |
| Rail realtime feed | Live **train** positions (heavy rail), where the agency publishes them in a separate, non-GTFS-RT API | Agency-specific JSON |

**Why rail is separate:** A GTFS-RT protobuf feed often carries **buses only**. Heavy-rail
positions frequently come from a different, agency-specific realtime API (MARTA's is JSON,
not protobuf — see section 3). Static GTFS *does* describe rail routes (`route_type=1`),
so rail geometry is in the static zip; only the *live* rail position source differs. When
evaluating an agency, treat "buses via GTFS-RT" and "trains via a rail API" as two
independent realtime sources — either can be present, absent, or incompatible on its own.

### Known agency feeds

| Agency | Static GTFS zip | GTFS-RT vehicle positions (buses) | Rail realtime (trains) |
|--------|----------------|--------------------------|------------------------|
| MARTA (Atlanta) | `https://itsmarta.com/google_transit_feed/google_transit.zip` | `https://gtfs-rt.itsmarta.com/TMGTFSRealTimeWebService/vehicle/vehiclepositions.pb` | `https://developerservices.itsmarta.com:18096/itsmarta/railrealtimearrivals/developerservices/traindata?apiKey={KEY}` (JSON) |

When working with a new agency, ask the user for the static zip + GTFS-RT URL, and — if the
agency runs heavy rail — whether they have a separate rail realtime API.

---

## 1. Fetch & decode GTFS-RT (live vehicle positions)

GTFS-RT returns **binary protobuf** — WebFetch cannot decode it. Always use PowerShell:

```powershell
# No auth
$bytes = (Invoke-WebRequest -Uri "<GTFS_RT_URL>" -UseBasicParsing).Content
[System.IO.File]::WriteAllBytes("$env:TEMP\gtfs-rt.pb", $bytes)
Write-Host "Downloaded $($bytes.Length) bytes"

# Query-param key
$bytes = (Invoke-WebRequest -Uri "<URL>?api_key=<VALUE>" -UseBasicParsing).Content
[System.IO.File]::WriteAllBytes("$env:TEMP\gtfs-rt.pb", $bytes)

# Header key
$bytes = (Invoke-WebRequest -Uri "<URL>" -Headers @{ "Authorization" = "Apikey <VALUE>" } -UseBasicParsing).Content
[System.IO.File]::WriteAllBytes("$env:TEMP\gtfs-rt.pb", $bytes)
```

If the response is empty or HTML: report and stop.

### Decode — pure Python (no external deps, use this first)

This is the **primary decode path**. It requires no pip installs and works in auto-mode.
The field numbers below are verified against real MARTA feed data.

```
GTFS-RT proto field reference:
  FeedMessage:       1=header (msg), 2=entity (msg, repeated)
  FeedHeader:        1=gtfs_realtime_version (str), 2=timestamp (varint, may be 0)
  FeedEntity:        1=id (str), 2=is_deleted, 3=trip_update, 4=vehicle (msg), 5=alert
  VehiclePosition:   1=trip (msg), 5=timestamp (varint), 6=current_status (varint)
                     SPEC says: 2=vehicle_descriptor, 3=position
                     MARTA observed (2026-06-19): 2=position (vehicle_descriptor absent),
                     8=occupancy_status — spec field numbers shift when fields are omitted
  TripDescriptor:    1=trip_id (str), 5=route_id (str)
  VehicleDescriptor: 1=id (str)
  Position:          1=latitude (float32/wire5), 2=longitude (float32/wire5),
                     3=bearing (float32/wire5), 4=odometer (double/wire1),
                     5=speed (float32/wire5)

  Wire types: 0=varint, 1=64-bit, 2=length-delimited (msg/str/bytes), 5=32-bit float
  NOTE: header.timestamp=0 is normal for some feeds (e.g. MARTA) — not a decode error.
  NOTE: speed field 5 of Position is float32 in m/s; absent on ~40% of MARTA vehicles.
  WARNING: If lat/lon decode as null, run the raw field inspection below — field numbers
           vary by feed depending on which optional fields the publisher omits.
```

```powershell
python -c @"
import struct, json

def read_varint(buf, pos):
    result = shift = 0
    while True:
        b = buf[pos]; pos += 1
        result |= (b & 0x7F) << shift
        if not (b & 0x80): return result, pos
        shift += 7

def parse_fields(buf):
    pos = 0; end = len(buf); fields = {}
    while pos < end:
        tag, pos = read_varint(buf, pos)
        fn, wt = tag >> 3, tag & 7
        if wt == 0: v, pos = read_varint(buf, pos)
        elif wt == 1: v = buf[pos:pos+8]; pos += 8
        elif wt == 2:
            n, pos = read_varint(buf, pos); v = buf[pos:pos+n]; pos += n
        elif wt == 5: v = buf[pos:pos+4]; pos += 4
        else: break
        fields.setdefault(fn, []).append((wt, v))
    return fields

def flt(fields, fn):
    if fn in fields and fields[fn][0][0] == 5:
        return struct.unpack('<f', fields[fn][0][1])[0]
    return None

def strv(fields, fn):
    if fn in fields and fields[fn][0][0] == 2:
        return fields[fn][0][1].decode('utf-8', errors='replace')
    return None

def intv(fields, fn):
    if fn in fields and fields[fn][0][0] == 0:
        return fields[fn][0][1]
    return None

with open(r'$env:TEMP\gtfs-rt.pb', 'rb') as f:
    raw = bytearray(f.read())

top = parse_fields(raw)

header_ts = None
header_ver = None
if 1 in top:
    hf = parse_fields(top[1][0][1])
    header_ts = intv(hf, 2)
    header_ver = strv(hf, 1)

entities = top.get(2, [])
vehicle_count = with_route = without_route = has_lat = has_speed = has_bearing = has_ts = 0
route_ids = set()
samples = []

for _, entity_bytes in entities:
    ef = parse_fields(entity_bytes)
    entity_id = strv(ef, 1)
    if 4 not in ef: continue
    vehicle_count += 1
    vf = parse_fields(ef[4][0][1])

    route_id = None
    if 1 in vf:
        tf = parse_fields(vf[1][0][1])
        route_id = strv(tf, 5)

    lat = lon = speed = bearing = None
    # MARTA encodes position at field 2 (vehicle_descriptor absent, so position shifts up).
    # Spec says field 3. Use field 2 first; fall back to field 3 for feeds that follow spec.
    pos_field = 2 if 2 in vf else (3 if 3 in vf else None)
    if pos_field:
        pf = parse_fields(vf[pos_field][0][1])
        lat = flt(pf, 1); lon = flt(pf, 2); bearing = flt(pf, 3); speed = flt(pf, 5)
        if lat: has_lat += 1
        if bearing and bearing > 0: has_bearing += 1
        if speed and speed > 0: has_speed += 1

    vts = intv(vf, 5) if 5 in vf else None
    if vts: has_ts += 1

    vid = None
    # VehicleDescriptor: only attempt field 2 if it wasn't consumed as position
    if pos_field != 2 and 2 in vf:
        vidf = parse_fields(vf[2][0][1])
        vid = strv(vidf, 1)

    if route_id:
        with_route += 1; route_ids.add(route_id)
    else:
        without_route += 1

    if len(samples) < 5:
        samples.append({
            'entity_id': entity_id, 'vehicle_id': vid, 'route_id': route_id,
            'lat': round(lat, 5) if lat else None,
            'lon': round(lon, 5) if lon else None,
            'speed_ms': round(speed, 2) if speed else None,
            'bearing': round(bearing, 1) if bearing else None,
            'ts': vts,
        })

pct = lambda n: round(n / vehicle_count * 100, 1) if vehicle_count else 0

result = {
    'header_version': header_ver,
    'header_timestamp': header_ts,
    'total_bytes': len(raw),
    'total_entities': len(entities),
    'vehicle_entities': vehicle_count,
    'vehicles_with_route_id': with_route,
    'vehicles_without_route_id': without_route,
    'lat_lon_pct': pct(has_lat),
    'speed_pct': pct(has_speed),
    'bearing_pct': pct(has_bearing),
    'timestamp_pct': pct(has_ts),
    'route_ids': sorted(route_ids),
    'samples': samples,
}
# Self-diagnosis: if position decoded as 0%, emit the actual VehiclePosition field map
# so the caller can identify the correct field number without a separate inspection run.
if vehicle_count > 0 and has_lat == 0:
    ef0 = parse_fields(entities[0][1])
    vf0 = parse_fields(ef0[4][0][1]) if 4 in ef0 else {}
    result['_diag_vp_fields'] = list(vf0.keys())
    result['_diag_note'] = 'lat/lon=0: check _diag_vp_fields — position field number differs from expected'
print(json.dumps(result, indent=2))
"@
```

### Decode — gtfs-realtime-bindings (optional, cleaner if pip is available)

> **Note:** automated modes may block `pip install`. Only attempt this if the user has
> already granted the required permission or is running in an interactive mode.

```powershell
pip install gtfs-realtime-bindings --quiet
python -c @"
from google.transit import gtfs_realtime_pb2
import json, struct

with open(r'$env:TEMP\gtfs-rt.pb', 'rb') as f:
    feed = gtfs_realtime_pb2.FeedMessage()
    feed.ParseFromString(f.read())

header = feed.header
entities = list(feed.entity)
vehicle_entities = [e for e in entities if e.HasField('vehicle')]
vehicles_with_route = [e for e in vehicle_entities if e.vehicle.HasField('trip') and e.vehicle.trip.route_id]
route_ids = sorted({e.vehicle.trip.route_id for e in vehicles_with_route})

has_lat = sum(1 for e in vehicle_entities if e.vehicle.HasField('position') and e.vehicle.position.latitude != 0)
has_speed = sum(1 for e in vehicle_entities if e.vehicle.HasField('position') and e.vehicle.position.speed > 0)
has_bearing = sum(1 for e in vehicle_entities if e.vehicle.HasField('position') and e.vehicle.position.bearing > 0)
has_ts = sum(1 for e in vehicle_entities if e.vehicle.timestamp > 0)
n = len(vehicle_entities)
pct = lambda x: round(x/n*100,1) if n else 0

samples = []
for e in vehicle_entities[:5]:
    v = e.vehicle
    samples.append({
        'entity_id': e.id,
        'vehicle_id': v.vehicle.id if v.HasField('vehicle') else None,
        'route_id': v.trip.route_id if v.HasField('trip') else None,
        'lat': round(v.position.latitude, 5) if v.HasField('position') else None,
        'lon': round(v.position.longitude, 5) if v.HasField('position') else None,
        'speed_ms': round(v.position.speed, 2) if v.HasField('position') and v.position.speed > 0 else None,
        'bearing': round(v.position.bearing, 1) if v.HasField('position') and v.position.bearing > 0 else None,
        'ts': v.timestamp if v.timestamp > 0 else None,
    })

print(json.dumps({
    'header_version': header.gtfs_realtime_version,
    'header_timestamp': header.timestamp or None,
    'total_bytes': None,
    'total_entities': len(entities),
    'vehicle_entities': n,
    'vehicles_with_route_id': len(vehicles_with_route),
    'vehicles_without_route_id': n - len(vehicles_with_route),
    'lat_lon_pct': pct(has_lat),
    'speed_pct': pct(has_speed),
    'bearing_pct': pct(has_bearing),
    'timestamp_pct': pct(has_ts),
    'route_ids': route_ids,
    'samples': samples,
}, indent=2))
"@
```

### GTFS-RT output block

```
GTFS-RT Feed Snapshot
---------------------
URL:               <url>
Feed size:         <N> bytes
Header version:    <version or "—">
Header timestamp:  <UTC datetime or "—" — note: 0 is normal for some feeds>

Entities:          <total> total / <vehicle_entities> vehicle positions
With route_id:     <N> of <vehicle_entities> (<pct>%)
Without route_id:  <N>

Optional fields (of vehicle entities):
  lat/lon:         <pct>%
  speed:           <pct>%   (m/s; absent on many vehicles is normal)
  bearing:         <pct>%
  timestamp:       <pct>%

Distinct route IDs (<count>): <comma-separated, first 20 + "… and N more" if long>

Sample vehicles (up to 5):
  entity_id=<>  vehicle_id=<or —>  route_id=<or —>
    lat=<>  lon=<>  speed=<or — m/s>  bearing=<or —>  ts=<or —>
```

---

## 2. Fetch & decode static GTFS zip (route shapes)

**Use an agency-specific temp directory** — never `$env:TEMP\gtfs-static` (shared name
causes collisions when evaluating multiple agencies, and a partial write from a 35MB zip
is silent). Name it after the agency slug, e.g. `$env:TEMP\gtfs-mbta`.

```powershell
$agency = "<agency-slug>"   # e.g. "mbta", "marta", "trimet"
Invoke-WebRequest -Uri "<STATIC_GTFS_ZIP_URL>" -OutFile "$env:TEMP\gtfs-$agency.zip" -UseBasicParsing
Expand-Archive -Path "$env:TEMP\gtfs-$agency.zip" -DestinationPath "$env:TEMP\gtfs-$agency" -Force
Write-Host "Extracted to $env:TEMP\gtfs-$agency"
Get-ChildItem "$env:TEMP\gtfs-$agency" | Select-Object Name, Length
```

Then parse the files the worker depends on. **`shapes.txt` is optional for alignment
checks** — omit it (and set `total_shape_points` to `null`) when you only need route ID
alignment. Read it only when the report needs shape richness metrics.

```powershell
python -c @"
import csv, json, os, sys

agency = '<agency-slug>'
base = rf'$env:TEMP\gtfs-{agency}'
skip_shapes = '--no-shapes' in sys.argv  # pass flag to skip shapes.txt

# routes.txt: route_id -> route_short_name, color, route_type
routes = {}
with open(os.path.join(base, 'routes.txt'), encoding='utf-8-sig') as f:
    for row in csv.DictReader(f):
        routes[row['route_id']] = {
            'route_short_name': row.get('route_short_name', ''),
            'route_color': row.get('route_color', ''),
            'route_type': row.get('route_type', ''),
        }

# trips.txt: first shape_id per route_id (needed even when skipping shapes, for has-shape flag)
route_to_shape = {}
with open(os.path.join(base, 'trips.txt'), encoding='utf-8-sig') as f:
    for row in csv.DictReader(f):
        rid = row.get('route_id', '')
        sid = row.get('shape_id', '')
        if rid and sid and rid not in route_to_shape:
            route_to_shape[rid] = sid

shape_point_counts = {}
if not skip_shapes and os.path.exists(os.path.join(base, 'shapes.txt')):
    with open(os.path.join(base, 'shapes.txt'), encoding='utf-8-sig') as f:
        for row in csv.DictReader(f):
            sid = row.get('shape_id', '')
            shape_point_counts[sid] = shape_point_counts.get(sid, 0) + 1

summary = []
for route_id, meta in routes.items():
    shape_id = route_to_shape.get(route_id)
    point_count = shape_point_counts.get(shape_id, 0) if shape_id else 0
    summary.append({
        'route_id': route_id,
        'route_short_name': meta['route_short_name'],
        'shape_id': shape_id,
        'point_count': point_count if not skip_shapes else None,
        'color': meta['route_color'],
        'route_type': meta['route_type'],
    })

index_keys = sorted({r['route_short_name'] or r['route_id'] for r in summary})
total_points = sum(r['point_count'] for r in summary if r['point_count']) if not skip_shapes else None
rail = [r for r in summary if r['route_type'] == '1']

print(json.dumps({
    'route_count': len(routes),
    'routes_with_shape': sum(1 for r in summary if r['shape_id']),
    'routes_without_shape': sum(1 for r in summary if not r['shape_id']),
    'total_shape_points': total_points,
    'rail_route_count': len(rail),
    'rail_index_keys': sorted({r['route_short_name'] or r['route_id'] for r in rail}),
    'index_keys': index_keys,
    'sample_routes': summary[:5],
}, indent=2))
"@
```

### Static shapes output block

```
Static GTFS Shapes Snapshot
----------------------------
Source:                <zip URL>
Routes:                <route_count> total / <routes_with_shape> with shape / <routes_without_shape> without
Rail routes:           <rail_route_count> (route_type=1) — keys: <rail_index_keys or "none">
Total shape points:    <total or "— (shapes not read)">

Route index keys (<count>): <comma-separated routeShortName values, first 20 + "… and N more">

Sample routes (up to 5):
  route_id=<>  short_name=<>  shape_id=<>  points=<>  color=<>  type=<route_type>
```

> **Index key note**: The worker keys its route index by `routeShortName` (falling back
> to `routeId`). These are the values that `trip.route_id` in the GTFS-RT feed must
> match for a vehicle to be snapped rather than counted as `skippedUnknownRoute`.

> **Rail note**: routes.txt carries `route_type` (GTFS standard: `1` = subway/metro =
> heavy rail; `3` = bus). The worker classifies a route as Rail vs Bus from this column
> (`GtfsStaticLoader.cs`). Rail routes have shapes in the static zip just like buses —
> their *live positions* are the only thing that comes from a separate source (section 3).
> The summary script above reports `route_type` per route so you can see which static
> routes are rail. MARTA's four rail routes are `route_type=1` with short names
> `RED / GOLD / BLUE / GREEN`.

---

## 3. Fetch & decode rail realtime (live train positions)

Only relevant for agencies that run heavy rail **and** publish train positions through a
separate API (not the GTFS-RT protobuf feed). MARTA's is JSON over HTTPS:

```powershell
# Key is optional on MARTA's deployment (returned 200 keyless on 2026-06-23) but pass it if you have one.
$url = "https://developerservices.itsmarta.com:18096/itsmarta/railrealtimearrivals/developerservices/traindata"
# With key: $url = "$url`?apiKey=<VALUE>"
$json = (Invoke-WebRequest -Uri $url -UseBasicParsing).Content
$json | Out-File "$env:TEMP\rail-rt.json" -Encoding utf8
Write-Host "Downloaded $($json.Length) chars"
```

The response is a JSON **array** with one element **per (train, upcoming-station)** — so a
single train appears many times. All values are JSON **strings**; numeric fields need
parsing. Fields the worker's adapter uses are in **bold**:

```
DESTINATION, DIRECTION, EVENT_TIME (parsed → VehiclePosition.Timestamp),
**IS_REALTIME** (drop rows != "true"), **LINE** (→ route_id; matches static rail
short name RED/GOLD/BLUE/GREEN with zero translation), NEXT_ARR, STATION,
**TRAIN_ID** (→ entity id + vehicleId), WAITING_SECONDS, WAITING_TIME, DELAY,
**LATITUDE**, **LONGITUDE** (→ live position; same for all rows of one TRAIN_ID)
```

Decode + the two contract checks the worker's `RailRealtimeAdapter` relies on:

```powershell
python -c @"
import json, collections

# utf-8-sig tolerates the BOM PowerShell's Out-File -Encoding utf8 prepends on 5.1.
with open(r'$env:TEMP\rail-rt.json', encoding='utf-8-sig') as f:
    rows = json.load(f)

realtime = [r for r in rows if str(r.get('IS_REALTIME','')).strip().lower() == 'true']

by_train = collections.defaultdict(list)
for r in realtime:
    by_train[r.get('TRAIN_ID')].append(r)

trains = []
contract_violations = []
lines = set()
for tid, group in by_train.items():
    coords = {(r.get('LATITUDE'), r.get('LONGITUDE')) for r in group}
    if len(coords) > 1:
        # Live-position contract broken: lat/lon should be identical across a train's rows.
        contract_violations.append({'train_id': tid, 'distinct_coords': len(coords)})
    line = group[0].get('LINE')
    lines.add(line)
    trains.append({
        'train_id': tid, 'line': line,
        'lat': group[0].get('LATITUDE'), 'lon': group[0].get('LONGITUDE'),
        'station_rows': len(group),
    })

print(json.dumps({
    'total_rows': len(rows),
    'realtime_rows': len(realtime),
    'dropped_not_realtime': len(rows) - len(realtime),
    'distinct_trains': len(by_train),
    'distinct_lines': sorted(l for l in lines if l),
    'contract_violations': contract_violations,  # MUST be empty — else lat/lon is not live position
    'sample_trains': trains[:5],
}, indent=2))
"@
```

### Rail realtime output block

```
Rail Realtime Snapshot
----------------------
URL:                  <url>
Total rows:           <N>  (one per train × upcoming-station)
Realtime rows:        <N>  (dropped <N> with IS_REALTIME != "true")
Distinct trains:      <N>
Lines seen:           <comma-separated LINE values — must match static rail short names>
Live-position check:  <PASS (one coord per train) / FAIL — N trains with multiple coords>

Sample trains (up to 5):
  train_id=<>  line=<>  lat=<>  lon=<>  station_rows=<>
```

> **Line-key note**: a row's `LINE` becomes the live `route_id`. For the train to snap, it
> must match a static rail route's index key (`routeShortName ?? routeId`). MARTA's `LINE`
> values equal the static `route_short_name` exactly, so no transform is needed. A new
> agency's rail API may use different line identifiers — check this the same way you check
> bus `route_id` alignment.

---

## Combined decode + alignment (compatibility evaluation fast path)

For compatibility checks, run this **single script** after both feeds are already
downloaded. It does RT decode + static parse + route ID alignment in one Python
invocation — no manual set-copying, no intermediate JSON files, 1 tool call instead of 3.

**Prerequisites:** GTFS-RT already at `$env:TEMP\gtfs-rt.pb`; static already extracted
to `$env:TEMP\gtfs-<agency>`. Run the fetch + extract steps first (in parallel), then
this script once both are done.

```powershell
python -c @"
import struct, csv, json, os

agency = '<agency-slug>'   # e.g. mbta, marta, trimet
static_base = rf'$env:TEMP\gtfs-{agency}'

# ── helpers ──────────────────────────────────────────────────────────────────
def read_varint(buf, pos):
    result = shift = 0
    while True:
        b = buf[pos]; pos += 1
        result |= (b & 0x7F) << shift
        if not (b & 0x80): return result, pos
        shift += 7

def parse_fields(buf):
    pos = 0; end = len(buf); fields = {}
    while pos < end:
        tag, pos = read_varint(buf, pos)
        fn, wt = tag >> 3, tag & 7
        if wt == 0: v, pos = read_varint(buf, pos)
        elif wt == 1: v = buf[pos:pos+8]; pos += 8
        elif wt == 2:
            n, pos = read_varint(buf, pos); v = buf[pos:pos+n]; pos += n
        elif wt == 5: v = buf[pos:pos+4]; pos += 4
        else: break
        fields.setdefault(fn, []).append((wt, v))
    return fields

def flt(f, n): return struct.unpack('<f', f[n][0][1])[0] if n in f and f[n][0][0] == 5 else None
def strv(f, n): return f[n][0][1].decode('utf-8', errors='replace') if n in f and f[n][0][0] == 2 else None
def intv(f, n): return f[n][0][1] if n in f and f[n][0][0] == 0 else None

# ── decode GTFS-RT ────────────────────────────────────────────────────────────
with open(r'$env:TEMP\gtfs-rt.pb', 'rb') as f:
    raw = bytearray(f.read())

top = parse_fields(raw)
header_ts = header_ver = None
if 1 in top:
    hf = parse_fields(top[1][0][1])
    header_ts = intv(hf, 2); header_ver = strv(hf, 1)

entities = top.get(2, [])
vehicle_count = with_route = without_route = has_lat = has_speed = has_bearing = has_ts = 0
rt_route_ids = set()
samples = []

for _, eb in entities:
    ef = parse_fields(eb)
    if 4 not in ef: continue
    vehicle_count += 1
    vf = parse_fields(ef[4][0][1])

    route_id = strv(parse_fields(vf[1][0][1]), 5) if 1 in vf else None

    lat = lon = speed = bearing = None
    pos_field = 2 if 2 in vf else (3 if 3 in vf else None)
    if pos_field:
        pf = parse_fields(vf[pos_field][0][1])
        lat = flt(pf, 1); lon = flt(pf, 2); bearing = flt(pf, 3); speed = flt(pf, 5)
        if lat: has_lat += 1
        if bearing and bearing > 0: has_bearing += 1
        if speed and speed > 0: has_speed += 1

    vts = intv(vf, 5) if 5 in vf else None
    if vts: has_ts += 1

    if route_id: with_route += 1; rt_route_ids.add(route_id)
    else: without_route += 1

    if len(samples) < 5:
        samples.append({'route_id': route_id,
            'lat': round(lat,5) if lat else None, 'lon': round(lon,5) if lon else None,
            'speed_ms': round(speed,2) if speed else None, 'ts': vts})

pct = lambda n: round(n / vehicle_count * 100, 1) if vehicle_count else 0

rt_result = {
    'header_version': header_ver, 'header_timestamp': header_ts,
    'total_bytes': len(raw), 'total_entities': len(entities),
    'vehicle_entities': vehicle_count,
    'vehicles_with_route_id': with_route, 'vehicles_without_route_id': without_route,
    'lat_lon_pct': pct(has_lat), 'speed_pct': pct(has_speed),
    'bearing_pct': pct(has_bearing), 'timestamp_pct': pct(has_ts),
    'route_ids': sorted(rt_route_ids), 'samples': samples,
}
if vehicle_count > 0 and has_lat == 0:
    ef0 = parse_fields(entities[0][1])
    vf0 = parse_fields(ef0[4][0][1]) if 4 in ef0 else {}
    rt_result['_diag_vp_fields'] = list(vf0.keys())
    rt_result['_diag_note'] = 'lat/lon=0: check _diag_vp_fields'

# ── parse static GTFS (routes + trips only; shapes skipped for alignment) ─────
routes = {}
with open(os.path.join(static_base, 'routes.txt'), encoding='utf-8-sig') as f:
    for row in csv.DictReader(f):
        routes[row['route_id']] = {
            'short_name': row.get('route_short_name', ''),
            'route_type': row.get('route_type', ''),
            'color': row.get('route_color', ''),
        }

route_to_shape = {}
with open(os.path.join(static_base, 'trips.txt'), encoding='utf-8-sig') as f:
    for row in csv.DictReader(f):
        rid = row.get('route_id', '')
        sid = row.get('shape_id', '')
        if rid and sid and rid not in route_to_shape:
            route_to_shape[rid] = sid

summary = [{'route_id': rid, 'short_name': m['short_name'],
            'has_shape': rid in route_to_shape, 'route_type': m['route_type'],
            'color': m['color']} for rid, m in routes.items()]

index_keys = sorted({r['short_name'] or r['route_id'] for r in summary})
rail = [r for r in summary if r['route_type'] == '1']
rail_keys = sorted({r['short_name'] or r['route_id'] for r in rail})

static_result = {
    'route_count': len(routes),
    'routes_with_shape': sum(1 for r in summary if r['has_shape']),
    'routes_without_shape': sum(1 for r in summary if not r['has_shape']),
    'total_shape_points': None,  # shapes.txt not read — use separate parse if needed
    'rail_route_count': len(rail), 'rail_index_keys': rail_keys,
    'index_keys': index_keys, 'sample_routes': summary[:5],
}

# ── route ID alignment ────────────────────────────────────────────────────────
static_key_set = set(index_keys)
matched = rt_route_ids & static_key_set
unmatched_rt = sorted(rt_route_ids - static_key_set)
unmatched_static = sorted(static_key_set - rt_route_ids)

alignment = {
    'rt_distinct': len(rt_route_ids),
    'static_keys': len(static_key_set),
    'matched': len(matched),
    'match_pct': round(len(matched) / len(rt_route_ids) * 100, 1) if rt_route_ids else 0,
    'unmatched_rt_ids': unmatched_rt,
    'static_only_sample': unmatched_static[:10],
    'static_only_total': len(unmatched_static),
}

print(json.dumps({'rt': rt_result, 'static': static_result, 'alignment': alignment}, indent=2))
"@
```

### Combined output fields → report mapping

| JSON path | Report field |
|-----------|-------------|
| `rt.total_bytes` | RT feed size |
| `rt.header_timestamp` | Header ts (`0` = normal) |
| `rt.vehicle_entities` | Vehicle entities |
| `rt.vehicles_with_route_id` + `rt.lat_lon_pct` | Required fields PASS/FAIL |
| `rt.speed_pct`, `rt.bearing_pct`, `rt.timestamp_pct` | Optional fields |
| `rt.route_ids` | Input to alignment (already computed — don't re-type) |
| `static.route_count` / `routes_with_shape` | Static routes row |
| `static.rail_route_count` / `rail_index_keys` | Rail section |
| `static.index_keys` | Input to alignment (already computed) |
| `alignment.match_pct` | Route ID alignment verdict |
| `alignment.unmatched_rt_ids` | Unmatched RT IDs (sample) |
| `alignment.static_only_sample` | Static-only keys (sample) |

---

## Error handling

| Symptom | Response |
|---------|----------|
| HTTP 4xx/5xx | Report status code; stop |
| Empty or HTML response | Wrong URL or auth required; report and stop |
| Protobuf parse error | Report byte count; note feed may not be GTFS-RT |
| 0 vehicle entities in RT feed | Emit snapshot with 0; note feed may be trip-updates or alerts only |
| `vehicle_entities > 0` but all lat/lon null | Wrong field numbers in decoder — run raw field inspection (see below) |
| Missing file in static zip | Report which of routes.txt / trips.txt / shapes.txt is absent; stop that parse step |
| Routes with no shape | Normal; report count separately |
| `header.timestamp = 0` | Normal for some feeds (confirmed MARTA behavior) — not an error |
| Rail API returns HTML/empty | Wrong URL, key required, or agency has no rail API; report and stop the rail step only |
| Rail rows all `IS_REALTIME != "true"` | All rows dropped; report — feed may be schedule-only at this time of day |
| One TRAIN_ID has multiple distinct coords | Live-position contract broken: lat/lon is NOT the live train position; flag (worker logs a warning, still snaps the first row) |
| No `route_type=1` routes in static zip | Agency has no heavy rail (or doesn't tag it); rail realtime step is N/A |

### Raw field inspection (if decoder produces null lat/lon)

If a feed produces vehicles with `route_id` but null position, the proto field layout
may differ from the verified MARTA numbers. Run this to inspect actual field numbers:

```powershell
python -c @"
def read_varint(buf, pos):
    result = shift = 0
    while True:
        b = buf[pos]; pos += 1
        result |= (b & 0x7F) << shift
        if not (b & 0x80): return result, pos
        shift += 7

def parse_fields(buf):
    pos = 0; end = len(buf); fields = {}
    while pos < end:
        tag, pos = read_varint(buf, pos)
        fn, wt = tag >> 3, tag & 7
        if wt == 0: v, pos = read_varint(buf, pos)
        elif wt == 1: v = buf[pos:pos+8]; pos += 8
        elif wt == 2:
            n, pos = read_varint(buf, pos); v = buf[pos:pos+n]; pos += n
        elif wt == 5: v = buf[pos:pos+4]; pos += 4
        else: break
        fields.setdefault(fn, []).append((wt, v))
    return fields

import struct
with open(r'$env:TEMP\gtfs-rt.pb', 'rb') as f:
    raw = bytearray(f.read())

top = parse_fields(raw)
print('Top-level fields:', list(top.keys()))
entities = top.get(2, [])
if entities:
    ef = parse_fields(entities[0][1])
    print('FeedEntity fields:', list(ef.keys()))
    for k in ef:
        if ef[k][0][0] == 2:  # length-delimited = submessage candidate
            sf = parse_fields(ef[k][0][1])
            print(f'  entity field {k} sub-fields:', list(sf.keys()))
            for sk in sf:
                wt, v = sf[sk][0]
                if wt == 5:
                    import struct
                    print(f'    field {sk} (float32):', struct.unpack('<f',v)[0])
                elif wt == 0:
                    print(f'    field {sk} (varint):', v)
                elif wt == 2:
                    print(f'    field {sk} (str/msg, {len(v)} bytes):', v[:20])
"@
```
