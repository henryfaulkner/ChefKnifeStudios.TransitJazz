# Contract: `nymta-bus` City Configuration

Added to the `Cities:` array in **both** `TransitDataWorker/appsettings.json` and `WebAPI/appsettings.json` (Worker consumes RT + normalization; WebAPI's `GtfsStaticLoader` consumes `StaticZipUrls`). Development variants mirror as needed.

## Cities: entry

```jsonc
{
  "Name": "nymta-bus",
  "GtfsRtUrls": [ "https://gtfsrt.prod.obanyc.com/vehiclePositions?key=${NYMTA_BUS_API_KEY}" ],
  "StaticZipUrls": [
    "http://web.mta.info/developers/data/nyct/bus/google_transit_manhattan.zip",
    "http://web.mta.info/developers/data/nyct/bus/google_transit_bronx.zip",
    "http://web.mta.info/developers/data/nyct/bus/google_transit_brooklyn.zip",
    "http://web.mta.info/developers/data/nyct/bus/google_transit_queens.zip",
    "http://web.mta.info/developers/data/nyct/bus/google_transit_staten_island.zip",
    "http://web.mta.info/developers/data/busco/google_transit.zip"
  ],
  "RouteIdNormalization": [ "uppercase", "plusToSbs", "stripLeadingZeros" ],
  "EmitsTelemetry": true
}
```

**Credential handling (see research R4)** — pick ONE at implementation time after verifying which the Worker's config layering supports:
- **Preferred**: `${NYMTA_BUS_API_KEY}` substitution in the `GtfsRtUrls` string (key stays in env, never committed), `ApiKeyEnvVar` omitted.
- **Fallback** (if `${}` substitution isn't supported): set `"ApiKeyEnvVar": "NYMTA_BUS_API_KEY"` + `"ApiKeyQueryParam": "key"` (new `CityConfig` field) and use a plain `"...vehiclePositions"` URL. Requires the ~3-line `FetchFeedAsync` change.

The committed repo MUST NOT contain a live key under any option.

## Field-level assertions

| Field | Assertion |
|-------|-----------|
| `Name` | equals `CityNames.NymtaBus` (`"nymta-bus"`); distinct from `"nymta"` |
| `GtfsRtUrls` | exactly one citywide obanyc URL |
| `StaticZipUrls` | exactly 6 URLs (5 NYCT borough + 1 Bus Co) |
| `RouteIdNormalization` | `["uppercase","plusToSbs","stripLeadingZeros"]` in that order |
| `EmitsTelemetry` | `true` |
| `RailRouteIdMap` | absent/null (unused) |

## Behavioral contract (registry + pipeline)

- The registry factory (`Program.cs`) registers `nymta-bus` via the existing `else` arm as a `GtfsRtCity` — **no `Program.cs` edit**.
- For every OTHER existing city (empty/absent `RouteIdNormalization`), `ApplyRouteIdNormalization` early-returns → byte-identical behavior (regression guard for SC-004).
- `CityFab.razor` gains one button navigating to `#nymta-bus`; label sourced from `RouteFilterResources.resx` key `CityNymtaBus`.
