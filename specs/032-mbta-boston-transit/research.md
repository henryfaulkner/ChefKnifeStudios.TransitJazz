# Phase 0 Research: Add Boston (MBTA)

There were no open `NEEDS CLARIFICATION` items — the compat doc (`docs/city-compat/mbta.md`) and the merged 031 code resolve every question. Below are the decisions that scoped the feature.

## Decision 1 — MBTA is the configuration-only (`GtfsRtCity`) path, not a bespoke (`MartaCity`) path

- **Decision**: Add MBTA as a generic config-driven city. No new code class.
- **Rationale**: MBTA's single `VehiclePositions.pb` carries all modes including heavy rail (32 live Red/Orange/Blue trains in the sampled snapshot), all with lat/lon. There is no separate rail realtime source — the structural reason MARTA needed a bespoke `MartaCity` (its JSON rail API) simply does not exist for MBTA. `Program.cs:39-42` already routes every non-`marta` config entry to `GtfsRtCity`, so adding the config entry *is* adding the city — no registration code.
- **Alternatives considered**: A bespoke `MbtaCity` class — rejected, nothing about MBTA is non-standard. A `RailRealtime` config block — rejected, no separate rail feed exists.

## Decision 2 — No `RailRouteIdMap` for MBTA

- **Decision**: Omit `RailRouteIdMap` from the MBTA config.
- **Rationale**: WMATA needed it because its RT rail IDs (`BLUE`,`RED`,…) differ from its static `route_id` (`B`,`R`,…). MBTA's heavy-rail RT IDs `Red`/`Orange`/`Blue` exist verbatim as static `route_id`s (compat doc §Rail). The Green/Silver/combined/shuttle mismatches the doc lists are a `route_short_name` artifact, not a `route_id` one — and we key by `route_id`.
- **Alternatives considered**: Mapping the Green/Silver lines — rejected; they already align by `route_id`. Mapping would *break* alignment.

## Decision 3 — The compat doc's "key by route_id" fix is already in place

- **Decision**: No keying change needed.
- **Rationale**: The compat doc was written against MARTA's older `route_short_name ?? route_id` index and recommends switching to `route_id`. Feature 031 already did this: `GtfsStaticLoader` builds the index by `route_id` and stores `{city}:{routeId}` (`GtfsStaticLoader.cs:121,137`); `route_short_name` survives only as display metadata. So MBTA gets the doc's "100% (106/106)" alignment with zero code change.
- **Alternatives considered**: A `route_id` fallback when `route_short_name` lookup misses — rejected; moot, the index is `route_id`-primary already.

## Decision 4 — No secret

- **Decision**: No `ApiKeyEnvVar` for MBTA.
- **Rationale**: `cdn.mbta.com` `.pb` and `.zip` are public and keyless (compat doc §Auth). The `42aa…` V3 key is for `api-v3.mbta.com`, which this worker does not use. Satisfies Principle II / FR-005 with nothing to store.

## Decision 5 — Reachability requires two tiny source touches (this is honest, not config-only)

- **Decision**: Add `CityNames.Mbta = "mbta"` and one `CityFab` menu item.
- **Rationale**: The *data pipeline* is config-only, but a viewer can't reach a city that isn't in the picker without hand-editing the URL hash. The existing `CityFab` hardcodes Atlanta/DC menu items, so Boston needs one analogous item. The `CityNames` constant keeps the app keying on a single source of truth (used by `CityFab`'s active-city disabling). SC-003 in the spec states this honestly: the only source changes are the constant and the picker entry.
- **Alternatives considered**: Driving the picker from config/API so no source edit is needed — rejected as out-of-scope gold-plating (YAGNI) for a 3-city app; the picker is already a hardcoded list and matching that pattern is the smaller, consistent change.

## Decision 6 — `route_type`-based mode tagging already handles MBTA rail

- **Decision**: Rely on the existing `ParseRouteMetadata` `route_type` → `TransitMode` mapping.
- **Rationale**: The loader tags mode from GTFS `route_type` generically (heavy rail = `route_type=1`), not by city. MBTA's Red/Orange/Blue carry `route_type=1` and will tag as rail automatically, same path WMATA rail uses.
