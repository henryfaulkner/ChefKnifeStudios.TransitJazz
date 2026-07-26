# Contract: TTC City Configuration

The `ttc` entry added to the `Cities:` array. Must be added **identically** to both
`Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/appsettings.json` and
`Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json`.

## Canonical JSON

```json
{
  "Name": "ttc",
  "GtfsRtUrls": [ "https://bustime.ttc.ca/gtfsrt/vehicles" ],
  "StaticZipUrls": [ "https://ckan0.cf.opendata.inter.prod-toronto.ca/dataset/7795b45e-e65a-4465-81fc-c36b9dfff169/resource/cfb6b2b8-6191-41e3-bda1-b175c51148cb/download/TTC%20Routes%20and%20Schedules%20Data.zip" ],
  "EmitsTelemetry": true
}
```

## Field contract

| Field | Required | Rule |
|-------|----------|------|
| `Name` | yes | Exactly `"ttc"`; MUST equal `CityNames.Ttc`. Case-insensitive match in `Program.cs`. |
| `GtfsRtUrls` | yes | Exactly the one keyless surface-vehicle endpoint. No `?api_key=` appended (no `ApiKeyEnvVar`). |
| `StaticZipUrls` | yes | One keyless CKAN zip URL; the space in the filename MUST be `%20`. |
| `EmitsTelemetry` | yes | `true`. |
| `RailRealtime` | absent | MUST NOT be present — no live subway feed (FR-008). |
| `RailRouteIdMap` | absent | MUST NOT be present — verbatim route-id match. |
| `RouteIdNormalization` | absent | MUST NOT be present (or `[]`) — no transform. |
| `ApiKeyEnvVar` | absent | MUST NOT be present — keyless. |

## Accept / reject vectors

| Scenario | Expected |
|----------|----------|
| Worker starts with this entry | `GtfsRtCity` named `ttc` registered via the `else` arm; `Program.cs` unchanged. |
| RT feed returns a vehicle with `route_id="504"` and a matching static `route_short_name="504"` | Vehicle snaps to route `504`; renders + voices. |
| RT feed returns a vehicle with no `route_id` (deadhead) | Counted `skippedNoRouteId`; not rendered; no error. |
| RT feed returns `route_id="600"` (not in static) | Counted `skippedUnknownRoute`; not rendered; no error. |
| Static zip URL left with a literal space (no `%20`) | REJECT — fetch may fail; use `%20`. |
| `RailRealtime` accidentally added | REJECT — TTC would attempt a nonexistent rail fetch (violates FR-008). |
| `ApiKeyEnvVar` accidentally added | REJECT — a spurious `?api_key=` query would be appended to a keyless endpoint. |
| WebAPI `Cities:` missing the ttc entry | REJECT — route shapes never load; live vehicles have nothing to snap to. |
