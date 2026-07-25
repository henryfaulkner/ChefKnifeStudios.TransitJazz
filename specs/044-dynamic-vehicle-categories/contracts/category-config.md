# Contract: Per-City Category Configuration (WebAPI `appsettings.json`)

Configuration is **WebAPI-only** (D3). The Worker and client require no config change and receive categories transitively.

## Shape

`RouteTypeCategories` is an **optional** object added to a city's entry in the existing `Cities:` array. Keys are GTFS `route_type` values **as strings**; values are category keys (lowercase, whitespace-free).

```json
{
  "Cities": [
    {
      "Name": "ttc",
      "StaticZipUrls": ["https://.../ttc-gtfs.zip"],
      "RouteTypeCategories": {
        "0": "streetcar",
        "1": "rail",
        "3": "bus"
      }
    },
    {
      "Name": "marta",
      "StaticZipUrls": ["https://.../marta-gtfs.zip"]
    }
  ]
}
```

- `RouteTypeCategories` is **purely additive** — the real `ttc` entry keeps its existing `GtfsRtUrls`/`EmitsTelemetry`/etc. fields; add the block alongside them.
- Only **TTC** gets a block on day one (D5a). MARTA/WMATA/MBTA/NYMTA omit it and keep today's exact behavior.
- Keys need only enumerate the `route_type`s a city actually uses (GTFS defines 0–7, 11–12); unlisted values fall to `"bus"` + warning (D5b).

## Which config files to edit

| File | Cities today | Action |
|---|---|---|
| `Server.WebAPI/appsettings.json` | 5 (marta, wmata, mbta, nymta, **ttc**) | **Add** `RouteTypeCategories` to the `ttc` entry only. |
| `Server.WebAPI/appsettings.Development.json` | 4 (marta, wmata, mbta, nymta — **no ttc**) | **No edit today.** If TTC is ever added here, its block must be added too, or local dev silently falls back to rail/bus (no streetcar section). |

## Binding & classification semantics

- Bound into WebAPI's private `CityStaticEntry` record via `LoadCityEntries()` (raw `IConfiguration.GetSection`), alongside the existing fields.
- Classification is per-city but the classifier stays **city-agnostic** — per-city behavior comes only from the passed-in map, never a `switch` on city name (Principle: no city hardcoded in shared logic; FR-019).
- **Empty map `{}`** behaves as "configured with nothing" → every `route_type` is unmapped → all routes `"bus"` + warnings. (A city wanting the rail/bus default must **omit** the key, not supply `{}`.)

## Fallback decision table (authoritative)

| Condition | Result |
|---|---|
| No `RouteTypeCategories` key on the city | Default rule: `route_type ∈ {0,1,2}` → `"rail"`, else `"bus"` |
| Key present, `route_type` **is** a map key | The mapped category value |
| Key present, `route_type` **not** a map key | `"bus"` + `LogWarning`; city continues loading |
| Route can't be found at join time (Worker, downstream) | `"unknown"` (not config-driven — see wire-contract / D6) |
