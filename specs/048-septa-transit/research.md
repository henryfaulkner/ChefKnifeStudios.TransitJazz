# Phase 0 Research: SEPTA Philadelphia Transit City

## R1: How should `GtfsStaticLoader` detect and unwrap a nested zip?

**Decision**: In `BuildCityShapeSetAsync`, after opening the downloaded zip as a `ZipArchive`,
check whether `trips.txt` exists at the archive root (the same check `ParseRouteToShapeMap`
already implicitly performs via `archive.GetEntry("trips.txt")`). If it's absent, look for a
single `.zip`-suffixed entry inside the archive whose name suggests bus/non-rail data (SEPTA's
case: prefer an entry NOT containing `"rail"` in its name, case-insensitive — i.e. select
`google_bus.zip` over `google_rail.zip` when both are present) and, if found, open that entry's
stream as a nested `ZipArchive` and continue processing against the nested archive instead. If
no `trips.txt` is found at the root AND no suitable nested zip entry exists, log a warning and
treat this zip URL as a failed fetch (same as any other per-URL exception today) — the existing
`fresh.Count == 0` guard already keeps last-known-good data in that case (constitution Principle
IV — no silent partial swap).

**Rationale**: Keeps the change to one method, additive-only (early-return path unaffected for
every existing flat zip, since `trips.txt` is always found at the root for them), and avoids a
city-name branch (`if (city.Name == "septa")`) in favor of a structural detection that could
serve any future zip-of-zips agency. The "avoid `rail` in the name" tie-break is specific enough
to pick the right nested zip for SEPTA without over-generalizing into a config-driven selector
that no other city needs yet (YAGNI — a config knob can be added later if a second zip-of-zips
city needs different tie-break logic).

**Alternatives considered**:
- *New `CityStaticEntry.NestedZipUrls` or similar config field, requiring the operator to specify
  the inner path explicitly*: rejected as unnecessary indirection for a single occurrence today;
  structural detection needs no new config surface at all, keeping SEPTA's `Cities:` entry
  byte-identical in shape to every other city's.
- *A SEPTA-specific adapter class/override*: rejected — pulls this out of the shared
  `GtfsStaticLoader` path entirely for a change that's fundamentally about zip-opening mechanics,
  not city-specific business logic (contrast with MARTA's rail-realtime merge, which genuinely is
  city-specific domain logic).
- *Always recursively unwrap every zip entry regardless of whether the root already has
  `trips.txt`*: rejected — unnecessary work and risk for every existing flat-zip city; the
  early-return/no-op path for the common case must stay trivially safe.

## R2: What happens to Regional Rail (`google_rail.zip`)?

**Decision**: Not loaded at all. The nested-zip selection logic (R1) actively prefers the
non-"rail"-named entry, so `google_rail.zip` is simply never opened by this feature. No
filtering-out-after-the-fact logic is needed because it's never brought in.

**Rationale**: Per spec FR-007 and the compat report, Regional Rail (`route_type=2`) has no live
GTFS-RT vehicle presence being onboarded in this feature — there would be nothing to route-match
its static shapes against, so loading it would add dead data to the KV store for no product value.

**Alternatives considered**: Load both nested zips and filter Regional Rail routes out downstream
by `route_type`. Rejected — more code, more KV storage, and no requirement calls for Regional
Rail data to exist anywhere in the system yet.

## R3: Map origin coordinate for Philadelphia

**Decision**: Center City Philadelphia, near SEPTA's 15th & Market / City Hall hub —
**39.9526, -75.1652** (approximately City Hall / Dilworth Park, where SEPTA's Broad Street
Subway, Market-Frankford Line, and the densest bus/trolley network converge).

**Rationale**: Matches the established precedent from TTC (043) — the transit-dense downtown
core, not the geographic centroid of the metro area (which would land in a low-density suburb and
show few initial vehicles).

**Alternatives considered**: Philadelphia International Airport or a geographic centroid of the
SEPTA service area — rejected, same reasoning as TTC's rejection of Toronto's geographic center.

## R4: Nested-zip test strategy

**Decision**: Add unit tests directly against `GtfsStaticLoader`'s internal static helpers
(mirroring the existing `internal static` test seams already used for `ParseRouteToShapeMap`,
`ParseShapes`, `BuildZipRouteFeatures`, etc.) covering:
1. A flat zip containing `trips.txt`/`shapes.txt`/`routes.txt` at the root is processed exactly
   as before (regression guard for existing cities).
2. A zip containing only a nested `google_bus.zip` (itself flat) is detected, unwrapped, and
   processed identically to case 1's flat equivalent.
3. A zip containing both a nested `google_bus.zip` and `google_rail.zip` selects the non-rail one.
4. A zip with neither a root `trips.txt` nor any nested zip entry produces zero routes and logs a
   warning, without throwing.

**Rationale**: This is genuinely new, non-trivial logic (unlike every prior config-only city's
plan, which had nothing new to unit test) — constitution Principle IV expects structured,
observable failure handling, and a silent or crashing failure on a zip-format surprise would
violate the "keep last-known-good, never partial-swap" behavior the loader already guarantees for
every other failure mode.

**Alternatives considered**: Integration-test only (fetch SEPTA's real zip in CI) — rejected as
flaky/slow/dependent on an external endpoint being up; unit tests against synthetic in-memory
zip fixtures are faster and deterministic, consistent with how the existing CSV parsing helpers
are already tested.
