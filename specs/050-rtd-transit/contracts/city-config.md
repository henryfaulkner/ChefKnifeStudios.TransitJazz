# Contract: RTD City Configuration

The `rtd` entry added to the `Cities:` array. Must be added **identically** to both
`Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/appsettings.json` and
`Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json`.

## Canonical JSON

```json
{
  "Name": "rtd",
  "GtfsRtUrls": [ "https://open-data.rtd-denver.com/files/gtfs-rt/rtd/VehiclePosition.pb" ],
  "StaticZipUrls": [ "https://www.rtd-denver.com/files/gtfs/google_transit.zip" ],
  "RailRouteIdMap": {
    "101C": "C",
    "101E": "E",
    "101T": "T",
    "103W": "W",
    "107R": "R",
    "113B": "B",
    "113G": "G",
    "117N": "N"
  },
  "EmitsTelemetry": true
}
```

## Field contract

| Field | Required | Rule |
|-------|----------|------|
| `Name` | yes | Exactly `"rtd"`; MUST equal `CityNames.Rtd`. Case-insensitive match in `Program.cs`. |
| `GtfsRtUrls` | yes | Exactly the one keyless vehicle-positions endpoint (`open-data.rtd-denver.com` host — the migrated Fall-2025 canonical URL). No `?api_key=` appended (no `ApiKeyEnvVar`). MUST NOT point at the sibling `Alerts.pb`/`TripUpdate.pb` feeds (not vehicle positions) or the separate `cdot/Bustang_*.pb` feeds (a different operator). |
| `StaticZipUrls` | yes | One keyless zip URL. This URL 308-redirects to `www.rtd-denver.com/api/download?feedType=gtfs&filename=google_transit.zip`; `HttpClient`'s default redirect-following handles it transparently — no loader change, no special marker needed (see `research.md` R2). |
| `RailRouteIdMap` | yes | Exactly the 8 entries above. `A` is deliberately **absent** from the map — it already matches its static counterpart verbatim and needs no remap (adding a no-op `"A": "A"` entry would be harmless but is unnecessary noise). |
| `EmitsTelemetry` | yes | `true`. |
| `RailRealtime` | absent | MUST NOT be present — there is no separate rail-realtime API; all 8 rail lines ride the same feed as buses. |
| `RouteIdNormalization` | absent | MUST NOT be present (or `[]`) — the rail mismatch is solved entirely by `RailRouteIdMap`, not a string-transform step (see `research.md` R1 for why a normalization step doesn't fit this case). |
| `ApiKeyEnvVar` | absent | MUST NOT be present — keyless. |

## Accept / reject vectors

| Scenario | Expected |
|----------|----------|
| Worker starts with this entry | `GtfsRtCity` named `rtd` registered via the `else` arm; `Program.cs` unchanged. |
| RT feed returns a vehicle with `route_id="15"` and a matching static `route_short_name="15"` | Vehicle snaps to route `15`; renders + voices (verbatim match, no remap). |
| RT feed returns a vehicle with `route_id="103W"` | `ApplyRailRouteIdMap` rewrites it to `W` before the V2 join; vehicle snaps to the `W` static light-rail route; renders + voices identically to a bus. |
| RT feed returns a vehicle with `route_id="A"` | Resolves directly against static route `A` — no remap entry needed or present. |
| RT feed returns a vehicle with `route_id="BOND"` or `route_id="FREE"` | Counted as unknown/unmatched (existing behavior); not remapped, not specially handled — explicit non-goal per spec FR-008. |
| RT feed returns a vehicle with no `route_id` (deadhead) | Counted `skippedNoRouteId`; not rendered; no error. |
| Static zip fetched via the 308-redirecting URL | `HttpClient` follows the redirect automatically; the resolved response body is processed as a normal flat zip — same code path as MBTA/TTC's static loading. |
| `RailRealtime` accidentally added | REJECT — RTD has no separate rail-realtime endpoint to call. |
| `ApiKeyEnvVar` accidentally added | REJECT — a spurious `?api_key=` query would be appended to a keyless endpoint. |
| `RailRouteIdMap` missing or incomplete (fewer than 8 entries) | REJECT — any omitted rail line falls back to unmatched/unknown instead of resolving to its static route. |
| WebAPI `Cities:` missing the rtd entry | REJECT — route shapes never load; live vehicles have nothing to snap to. |
| Worker and WebAPI `rtd` entries diverge (e.g. different `RailRouteIdMap` contents) | REJECT — shapes (WebAPI) and live vehicles (Worker) would disagree; entries MUST be byte-identical. |
