# Phase 1 Data Model: Toronto TTC Transit City

This feature introduces **no new types**. It reuses existing entities with a new configured instance. Documented here for completeness.

## Configured instance (new)

### TTC `CityConfig` entry

The only new "data" is one element in the `Cities:` config array, bound to the existing
`ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Cities.CityConfig`.

| Field | Value for TTC | Notes |
|-------|---------------|-------|
| `Name` | `"ttc"` | Matches `CityNames.Ttc`. Falls into the `else` arm → `GtfsRtCity`. |
| `GtfsRtUrls` | `[ "https://bustime.ttc.ca/gtfsrt/vehicles" ]` | Keyless surface (bus + streetcar) vehicle positions. |
| `BusGtfsRtUrls` | `[]` (omitted) | Only used by the nymta two-feed merge. N/A for TTC. |
| `StaticZipUrls` | `[ "https://ckan0.cf.opendata.inter.prod-toronto.ca/dataset/7795b45e-e65a-4465-81fc-c36b9dfff169/resource/cfb6b2b8-6191-41e3-bda1-b175c51148cb/download/TTC%20Routes%20and%20Schedules%20Data.zip" ]` | **Space percent-encoded as `%20`.** CKAN resource id may rotate. |
| `RailRealtime` | *omitted (null)* | No public live subway feed → no rail-realtime fetch (FR-008). |
| `RailRouteIdMap` | *omitted (null)* | No RT→static id remap needed. |
| `ApiKeyEnvVar` | *omitted (null)* | Keyless → plain unauthenticated GET. |
| `ApiKeyQueryParam` | *default `"api_key"`* | Unused because `ApiKeyEnvVar` is null. |
| `EmitsTelemetry` | `true` | Real GPS, parity with all live-vehicle cities. |
| `RouteIdNormalization` | `[]` (omitted) | RT `route_id` == static `route_short_name` verbatim → no transform. |

This entry is added to **both** `TransitDataWorker/appsettings.json` (drives live-vehicle fetch)
and `WebAPI/appsettings.json` (drives `GtfsStaticLoader` shape loading). They must stay in sync.

## Reused entities (unchanged)

- **`CityNames`** (`Shared/CityNames.cs`): gains one `public const string Ttc = "ttc";`. No structural change.
- **`GtfsRtCity`**: instantiated per the config; no code change.
- **`RouteShapeFeature` / `RouteShapeProperties`**: TTC route shapes populate these via `GtfsStaticLoader`. `JoinKey` = `route_short_name` (fallback `route_id`) matches RT verbatim.
- **`RouteNearestPointBatchEvent.RouteNearestPointRecord`**: carries `TransitMode` per route. TTC buses → `Bus`, streetcars (`route_type=0`) → `Rail` (as-built classifier; see research R1). No wire-format change — `TransitMode` already exists on the record.
- **`TransitMode` enum** (`{ Bus = 0, Rail = 1 }`): unchanged. (A future `Tram` member is the deferred streetcar-voicing follow-up and would be a wire-contract change — explicitly out of scope.)

## State & relationships

No new state. The Worker's per-city `_routeIndex`, `_routeMode`, `_vehicleStates`, and stale-pruning
all key by city name and now simply include a `ttc` bucket, populated exactly like `mbta`/`wmata`.

## Validation rules (from requirements)

- FR-004: TTC live `route_id` matched to static `route_short_name` **verbatim** (no transform). Enforced by empty `RouteIdNormalization` + existing `JoinKey`.
- FR-005 / FR-006: route-less and unknown-route vehicles skipped + counted by existing `skippedNoRouteId` / `skippedUnknownRoute` counters.
- FR-008: no rail-realtime fetch — enforced by omitting `RailRealtime`.
- FR-010: keyless — enforced by omitting `ApiKeyEnvVar`; static URL space percent-encoded.
- FR-011: single-source failure tolerated by existing per-URL try/catch in `GtfsRtCity.FetchVehiclesAsync` + last-good static retention.
