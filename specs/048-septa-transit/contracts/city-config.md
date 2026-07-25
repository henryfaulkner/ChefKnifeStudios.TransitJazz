# Contract: SEPTA City Configuration

The `septa` entry added to the `Cities:` array. Must be added **identically** to both
`Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/appsettings.json` and
`Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/appsettings.json`.

## Canonical JSON

```json
{
  "Name": "septa",
  "GtfsRtUrls": [ "https://www3.septa.org/gtfsrt/septa-pa-us/Vehicle/rtVehiclePosition.pb" ],
  "StaticZipUrls": [ "https://www3.septa.org/developer/gtfs_public.zip" ],
  "EmitsTelemetry": true
}
```

## Field contract

| Field | Required | Rule |
|-------|----------|------|
| `Name` | yes | Exactly `"septa"`; MUST equal `CityNames.Septa`. Case-insensitive match in `Program.cs`. |
| `GtfsRtUrls` | yes | Exactly the one keyless vehicle-positions endpoint. No `?api_key=` appended (no `ApiKeyEnvVar`). MUST NOT point at the sibling `rtTripUpdates.pb` or `rtServiceAlerts.pb` feeds — those are not vehicle positions. |
| `StaticZipUrls` | yes | One keyless zip URL. This zip is a **zip-of-zips** (`google_bus.zip` + `google_rail.zip` nested inside) — `GtfsStaticLoader`'s nested-zip detection (see `nested-zip-extraction.md`) handles the unwrap; the config entry itself needs no special marker or extra field for this. |
| `EmitsTelemetry` | yes | `true`. |
| `RailRealtime` | absent | MUST NOT be present — there is no separate rail-realtime API; `M1`/NHSL rides the same feed as buses. |
| `RailRouteIdMap` | absent | MUST NOT be present — verbatim route-id match for all route types, including `M1`. |
| `RouteIdNormalization` | absent | MUST NOT be present (or `[]`) — no transform. |
| `ApiKeyEnvVar` | absent | MUST NOT be present — keyless. |

## Accept / reject vectors

| Scenario | Expected |
|----------|----------|
| Worker starts with this entry | `GtfsRtCity` named `septa` registered via the `else` arm; `Program.cs` unchanged. |
| RT feed returns a vehicle with `route_id="47"` and a matching static `route_short_name="47"` | Vehicle snaps to route `47`; renders + voices. |
| RT feed returns a vehicle with `route_id="M1"` (NHSL) | Vehicle snaps to the `M1` static rail route; renders + voices identically to a bus — no rail-specific code path needed. |
| RT feed returns a vehicle with `route_id="B1"`/`"B2"`/`"B3"`/`"L1"` (Broad St / Market-Frankford) | If SEPTA ever emits one, it resolves and renders exactly like `M1` — no code change required for this to work (FR-008). |
| RT feed returns a vehicle with no `route_id` (deadhead) | Counted `skippedNoRouteId`; not rendered; no error. |
| Static zip fetched but its root has no `trips.txt` and no nested zip entry is found | `BuildCityShapeSetAsync` returns 0 routes for `septa`; existing `fresh.Count == 0` guard keeps last-known-good data; warning logged. No crash, no partial swap. |
| `RailRealtime` accidentally added | REJECT — SEPTA has no separate rail-realtime endpoint to call. |
| `ApiKeyEnvVar` accidentally added | REJECT — a spurious `?api_key=` query would be appended to a keyless endpoint. |
| WebAPI `Cities:` missing the septa entry | REJECT — route shapes never load; live vehicles have nothing to snap to. |
| Worker and WebAPI `septa` entries diverge (e.g. different `StaticZipUrls`) | REJECT — shapes (WebAPI) and live vehicles (Worker) would disagree; entries MUST be byte-identical. |
