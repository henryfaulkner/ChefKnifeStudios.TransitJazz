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

### Decode (Python preferred)

```powershell
pip install gtfs-realtime-bindings --quiet
python -c @"
from google.transit import gtfs_realtime_pb2
import json

with open(r'$env:TEMP\gtfs-rt.pb', 'rb') as f:
    feed = gtfs_realtime_pb2.FeedMessage()
    feed.ParseFromString(f.read())

header = feed.header
entities = list(feed.entity)
vehicle_entities = [e for e in entities if e.HasField('vehicle')]
vehicles_with_route = [e for e in vehicle_entities if e.vehicle.HasField('trip') and e.vehicle.trip.route_id]
route_ids = sorted({e.vehicle.trip.route_id for e in vehicles_with_route})

samples = []
for e in vehicle_entities[:5]:
    v = e.vehicle
    samples.append({
        'entity_id': e.id,
        'vehicle_id': v.vehicle.id if v.HasField('vehicle') else None,
        'route_id': v.trip.route_id if v.HasField('trip') else None,
        'latitude': v.position.latitude if v.HasField('position') else None,
        'longitude': v.position.longitude if v.HasField('position') else None,
        'speed': v.position.speed if v.HasField('position') and v.position.HasField('speed') else None,
        'bearing': v.position.bearing if v.HasField('position') and v.position.HasField('bearing') else None,
        'vehicle_timestamp': v.timestamp if v.HasField('timestamp') else None,
    })

print(json.dumps({
    'header_version': header.gtfs_realtime_version,
    'header_timestamp': header.timestamp if header.HasField('timestamp') else None,
    'total_entities': len(entities),
    'vehicle_entities': len(vehicle_entities),
    'vehicles_with_route_id': len(vehicles_with_route),
    'vehicles_without_route_id': len(vehicle_entities) - len(vehicles_with_route),
    'route_ids': route_ids,
    'samples': samples,
}, indent=2))
"@
```

Fallback — protoc:
```powershell
protoc --decode=transit_realtime.FeedMessage gtfs-realtime.proto < "$env:TEMP\gtfs-rt.pb"
```

### GTFS-RT output block

```
GTFS-RT Feed Snapshot
---------------------
URL:               <url>
Feed size:         <N> bytes
Header version:    <version or "—">
Header timestamp:  <UTC datetime or "—">

Entities:          <total> total / <vehicle_entities> vehicle positions
With route_id:     <N> of <vehicle_entities> (<pct>%)
Without route_id:  <N>

Distinct route IDs (<count>): <comma-separated, first 20 + "… and N more" if long>

Sample vehicles (up to 5):
  entity_id=<>  vehicle_id=<or —>  route_id=<or —>
    lat=<>  lon=<>  speed=<or —>  bearing=<or —>  ts=<or —>
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
| Missing file in static zip | Report which of routes.txt / trips.txt / shapes.txt is absent; stop that parse step |
| Routes with no shape | Normal; report count separately |
