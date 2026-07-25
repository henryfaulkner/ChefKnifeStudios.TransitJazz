# Phase 1 Data Model: Add Boston (MBTA)

No new types. MBTA is expressed entirely through existing 031 entities. This file documents the concrete values that populate them.

## MBTA `CityConfig` instance (worker `Cities:` array)

`CityConfig` (worker, `Cities/CityConfig.cs`) — populated for MBTA as:

| Field | MBTA value | Note |
|---|---|---|
| `Name` | `"mbta"` | Stable lowercase identifier = `CityNames.Mbta`. The SignalR group key and the `{city}:…` prefix. |
| `GtfsRtUrls` | `[ "https://cdn.mbta.com/realtime/VehiclePositions.pb" ]` | **One** URL — all modes in one feed. |
| `StaticZipUrls` | `[ "https://cdn.mbta.com/MBTA_GTFS.zip" ]` | **One** zip. |
| `RailRealtime` | *(omitted / null)* | No separate rail feed. |
| `RailRouteIdMap` | *(omitted / null)* | Heavy-rail IDs align verbatim. |
| `ApiKeyEnvVar` | *(omitted / null)* | Public, keyless. |
| `EmitsTelemetry` | `false` | Telemetry stays MARTA-only (FR-010). |

Contrast with the two existing entries: MARTA has `RailRealtime` (bespoke) and `EmitsTelemetry:true`; WMATA has `ApiKeyEnvVar` + `RailRouteIdMap`. MBTA has **none of these** — it is the minimal entry.

## MBTA static entry (WebAPI loader view)

`GtfsStaticLoader` reads the same `Cities:` array into its private `CityStaticEntry(Name, StaticZipUrls, ApiKeyEnvVar)`. For MBTA that resolves to `("mbta", ["https://cdn.mbta.com/MBTA_GTFS.zip"], null)`. The loader keys every shape `mbta:{routeId}`.

## Scoped entities (unchanged shapes, MBTA-populated)

- **Route (scoped)**: `(mbta, routeId)` — e.g. `(mbta, "Red")`, `(mbta, "1")`, `(mbta, "Green-B")`. `routeId` is the RT/static join key; 100% aligned.
- **Vehicle State (scoped)**: keyed within the `mbta` per-city cache — never collides with `marta`/`wmata` vehicle IDs.
- **Route Shape**: GeoJSON carrying `City = "mbta"`, stored at KV key `mbta:{routeId}`, served when the client requests `?city=mbta`.

## Client constant

`CityNames` (`Shared/CityNames.cs`) gains:

```csharp
public const string Mbta = "mbta";
```

Used by `CityFab` to disable the active-city menu item and to set the navigation hash.
