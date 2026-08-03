# Telemetry `city_name` vs. City Slug Divergence

Deliberate, permanent divergence introduced by `052-city-slug-migration` (FR-016/FR-018).
Do **not** "fix" this by making `city_name` match the slug — that would silently rewrite
parquet history at whatever date the fix lands.

## Why

`Worker.cs` writes `TelemetryEvent.city_name` from `ITransitCity.TelemetryName`, a property
deliberately split off from `ITransitCity.Name` (the public-facing slug used in the URL
fragment, SignalR group name, `?city=` query parameter, config `Name` key, and route-shape
store prefix). `TelemetryName` is frozen at each city's pre-migration identifier and does not
change when `Name` changes — including this migration, and any future rename.

Parquet part-files are append-only and immutable. If `city_name` had followed the slug rename,
every historical query spanning the cutover date would see a discontinuity — the same city's
history appearing to split into two identities at an arbitrary date. Freezing `TelemetryName`
keeps `city_name` a stable join/filter key across any number of future public-facing renames.

## The mapping

| City slug (`CityNames.*`, public) | Frozen `city_name` (parquet only) |
|---|---|
| `atlanta` | `marta` |
| `washington-dc` | `wmata` |
| `boston` | `mbta` |
| `new-york-city` | `nymta` |
| `toronto` | `ttc` |
| `philadelphia` | `septa` |
| `denver` | `rtd` |

Values were verified against live production telemetry (via the telemetry MCP bridge) at
migration time, not assumed — they are lowercase agency identifiers, matching the
`CityNames.*` values as they stood immediately before this migration.

## Where each one is allowed to appear

| | `Name` (slug) | `TelemetryName` (frozen) |
|---|---|---|
| URL fragment | ✅ | ❌ |
| SignalR group name | ✅ | ❌ |
| `?city=` query parameter | ✅ | ❌ |
| `Cities[].Name` config key | ✅ | ❌ |
| `{city}:` route-shape store prefix | ✅ | ❌ |
| Umami pageview path | ✅ | ❌ |
| `TelemetryEvent.city_name` (parquet) | ❌ | ✅ |
| `TelemetryEvent.cities_processed_csv` (parquet) | ❌ | ✅ |

## For future readers

If TransitJazz onboards a new city, that city's `TelemetryName` is set once, at onboarding,
and then frozen the same way — never re-derived from `Name`. See
`specs/052-city-slug-migration/contracts/city-identity.md` (C4) and
`specs/052-city-slug-migration/data-model.md` (E3) for the full contract this document
summarizes.
