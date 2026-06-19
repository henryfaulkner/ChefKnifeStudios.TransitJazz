---
name: mj-gtfs
description: Fetch and decode GTFS data from two sources: (1) a GTFS-RT vehicle positions protobuf feed (live positions) and (2) a static GTFS zip (routes.txt + shapes.txt + trips.txt) from a transit agency's published feed. Outputs clean structured snapshots of both. Use when any skill or the user needs live vehicle feed data, static route geometry, or both for analysis, compatibility evaluation, or adding a new transit data source.
---

# MJ GTFS

Fetches and decodes GTFS data from two sources and outputs clean structured snapshots.
This is a **data-fetch tool** — it retrieves and formats; callers apply their own logic.

## Sources

| Source | What it contains | Format |
|--------|-----------------|--------|
| GTFS-RT feed | Live vehicle positions: lat/lon, route_id, speed, bearing, timestamps | Binary protobuf |
| Static GTFS zip | Route geometry: routes.txt + shapes.txt + trips.txt from the agency's published feed | ZIP of CSV files |

### Known agency feeds

| Agency | Static GTFS zip | GTFS-RT vehicle positions |
|--------|----------------|--------------------------|
| MARTA (Atlanta) | `https://itsmarta.com/google_transit_feed/google_transit.zip` | `https://gtfs-rt.itsmarta.com/TMGTFSRealTimeWebService/vehicle/vehiclepositions.pb` |

When working with a new agency, ask the user for both URLs before proceeding.

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
GTFS-RT proto field reference (verified 2026-06-19 against live MARTA feed):
  FeedMessage:       1=header (msg), 2=entity (msg, repeated)
  FeedHeader:        1=gtfs_realtime_version (str), 2=timestamp (varint, may be 0)
  FeedEntity:        1=id (str), 2=is_deleted, 3=trip_update, 4=vehicle (msg), 5=alert
  VehiclePosition:   1=trip (msg), 2=vehicle_descriptor (msg), 3=position (msg),
                     5=timestamp (varint), 6=current_status (varint)
  TripDescriptor:    1=trip_id (str), 5=route_id (str)
  VehicleDescriptor: 1=id (str)
  Position:          1=latitude (float32/wire5), 2=longitude (float32/wire5),
                     3=bearing (float32/wire5), 4=odometer (double/wire1),
                     5=speed (float32/wire5)

  Wire types: 0=varint, 1=64-bit, 2=length-delimited (msg/str/bytes), 5=32-bit float
  NOTE: header.timestamp=0 is normal for some feeds (e.g. MARTA) — not a decode error.
  NOTE: speed field 5 of Position is float32 in m/s; absent on ~40% of MARTA vehicles.
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
    if 3 in vf:
        pf = parse_fields(vf[3][0][1])
        lat = flt(pf, 1); lon = flt(pf, 2); bearing = flt(pf, 3); speed = flt(pf, 5)
        if lat: has_lat += 1
        if bearing and bearing > 0: has_bearing += 1
        if speed and speed > 0: has_speed += 1

    vts = intv(vf, 5) if 5 in vf else None
    if vts: has_ts += 1

    vid = None
    if 2 in vf:
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

print(json.dumps({
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
}, indent=2))
"@
```

### Decode — gtfs-realtime-bindings (optional, cleaner if pip is available)

> **Note:** `pip install` is blocked in Claude Code auto-mode by default. Only attempt
> this if the user has already granted pip permissions or is running in interactive mode.

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

```powershell
Invoke-WebRequest -Uri "<STATIC_GTFS_ZIP_URL>" -OutFile "$env:TEMP\gtfs-static.zip" -UseBasicParsing
Expand-Archive -Path "$env:TEMP\gtfs-static.zip" -DestinationPath "$env:TEMP\gtfs-static" -Force
Write-Host "Extracted to $env:TEMP\gtfs-static"
Get-ChildItem "$env:TEMP\gtfs-static" | Select-Object Name, Length
```

Then parse the three files the worker depends on:

```powershell
python -c @"
import csv, json, os

base = r'$env:TEMP\gtfs-static'

# routes.txt: route_id -> route_short_name, color
routes = {}
with open(os.path.join(base, 'routes.txt'), encoding='utf-8-sig') as f:
    for row in csv.DictReader(f):
        routes[row['route_id']] = {
            'route_short_name': row.get('route_short_name', ''),
            'route_color': row.get('route_color', ''),
            'route_text_color': row.get('route_text_color', ''),
        }

# trips.txt: first shape_id per route_id
route_to_shape = {}
with open(os.path.join(base, 'trips.txt'), encoding='utf-8-sig') as f:
    for row in csv.DictReader(f):
        rid = row.get('route_id', '')
        sid = row.get('shape_id', '')
        if rid and sid and rid not in route_to_shape:
            route_to_shape[rid] = sid

# shapes.txt: shape_id -> sorted coordinate count
shape_point_counts = {}
with open(os.path.join(base, 'shapes.txt'), encoding='utf-8-sig') as f:
    for row in csv.DictReader(f):
        sid = row.get('shape_id', '')
        shape_point_counts[sid] = shape_point_counts.get(sid, 0) + 1

# Build summary
summary = []
for route_id, meta in routes.items():
    shape_id = route_to_shape.get(route_id)
    point_count = shape_point_counts.get(shape_id, 0) if shape_id else 0
    summary.append({
        'route_id': route_id,
        'route_short_name': meta['route_short_name'],
        'shape_id': shape_id,
        'point_count': point_count,
        'color': meta['route_color'],
    })

index_keys = sorted({r['route_short_name'] or r['route_id'] for r in summary})
total_points = sum(r['point_count'] for r in summary)

print(json.dumps({
    'route_count': len(routes),
    'routes_with_shape': sum(1 for r in summary if r['shape_id']),
    'routes_without_shape': sum(1 for r in summary if not r['shape_id']),
    'total_shape_points': total_points,
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
Total shape points:    <total>

Route index keys (<count>): <comma-separated routeShortName values, first 20 + "… and N more">

Sample routes (up to 5):
  route_id=<>  short_name=<>  shape_id=<>  points=<>  color=<>
```

> **Index key note**: The worker keys its route index by `routeShortName` (falling back
> to `routeId`). These are the values that `trip.route_id` in the GTFS-RT feed must
> match for a vehicle to be snapped rather than counted as `skippedUnknownRoute`.

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
