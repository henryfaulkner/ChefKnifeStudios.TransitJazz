# Contract: MBTA City Configuration

The concrete config entry to add to **both** `Cities:` arrays (worker and WebAPI), in both `appsettings.json` and `appsettings.Development.json`. Identical in all four files.

```json
{
  "Name": "mbta",
  "GtfsRtUrls": [ "https://cdn.mbta.com/realtime/VehiclePositions.pb" ],
  "StaticZipUrls": [ "https://cdn.mbta.com/MBTA_GTFS.zip" ],
  "EmitsTelemetry": false
}
```

## Contract guarantees

- **No `ApiKeyEnvVar`**: the endpoints are public/keyless. Adding a key field would be wrong (and a Principle II liability if a real key were ever committed).
- **No `RailRealtime`**: there is no separate MBTA rail feed; heavy rail rides `VehiclePositions.pb`.
- **No `RailRouteIdMap`**: RT heavy-rail IDs (`Red`/`Orange`/`Blue`) equal static `route_id`s. Adding a map would misroute them.
- **Single-element URL arrays**: unlike WMATA (separate bus/rail zips and feeds), MBTA is one of each.
- **`EmitsTelemetry: false`**: required so MBTA produces no parquet telemetry (FR-010).

## Worker registration (no code, by contract)

`Program.cs` routes any `Cities:` entry whose `Name != "marta"` to `GtfsRtCity` automatically (`Program.cs:39-42`). The `mbta` entry therefore becomes a live `GtfsRtCity` with **no registration code**.

## WebAPI static load (no code, by contract)

`GtfsStaticLoader.LoadCityEntries()` iterates the `Cities:` section and loads every entry's `StaticZipUrls`, keying shapes `{Name}:{routeId}`. The `mbta` entry is loaded with **no code change**.

## Reject / accept vectors

| Input | Expected |
|---|---|
| MBTA RT vehicle on `route_id` `Red` | Snaps to static shape `mbta:Red` (heavy rail), renders on the Red line. ✅ |
| MBTA RT vehicle on `route_id` `1` (bus) | Snaps to `mbta:1`. ✅ |
| MBTA RT vehicle on `Shuttle-Generic` (no static shape) | Appears as a live position; no shape to render — degraded, not broken. ✅ (edge case) |
| MBTA feed 5xx during a cycle | City skipped this cycle, logged with `{City}=mbta`; MARTA/WMATA unaffected. ✅ |
| A committed `mbta` entry containing an `ApiKeyEnvVar` with a literal key | ❌ Contract violation (Principle II) — never do this; MBTA needs no key. |
