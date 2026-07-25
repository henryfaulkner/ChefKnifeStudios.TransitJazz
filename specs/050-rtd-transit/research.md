# Phase 0 Research: RTD Denver Transit City

## R1: Does RTD's rail-remap need any code change to `RailRouteIdMap`/`GtfsRtCity`?

**Decision**: No. `CityConfig.RailRouteIdMap` (`Dictionary<string, string>?`) and
`GtfsRtCity.ApplyRailRouteIdMap` already implement exactly the transform RTD needs: for each
vehicle entity, if `RailRouteIdMap` is non-null/non-empty and contains the vehicle's
`Trip.RouteId`, rewrite it to the mapped static value before the route-join step runs. This is
generic — it doesn't branch on city name — so adding an 8-entry `RailRouteIdMap` to RTD's config
entry is sufficient with zero touches to `GtfsRtCity.cs` or `CityConfig.cs`.

**Rationale**: WMATA already exercises this exact mechanism (`BLUE`/`BLUE0` → `B`, etc.) for the
same reason — RT sends line names/prefixes that don't match static's short route names. RTD is a
structurally identical case (`103W` → `W`), just with a different prefix shape (`NNN` + letter
instead of `COLOR` + `0`). No generalization work is needed because the mechanism was already
written generically (a plain dictionary lookup, not a WMATA-specific transform).

**Alternatives considered**:
- *A new `RouteIdNormalization` transform step (like NYMTA's `uppercase`/`plusToSbs`/
  `stripLeadingZeros`)*: rejected — those are stateless string transforms; RTD's mapping
  (`101C`→`C`) isn't a formula (stripping the `101` prefix would also incorrectly affect `A`,
  which has no prefix and already matches verbatim, and the prefixes `101`/`103`/`107`/`113`/
  `117` aren't a fixed-width strip since they vary in the *letter* position too). An explicit
  8-entry dictionary is simpler and exactly matches the finite, known set of lines.
- *A rewritten/generalized `RailRouteIdMap` type to formally support two cities*: rejected —
  the field is already `Dictionary<string,string>?` scoped per `CityConfig` entry; two cities
  independently populating it requires no schema or code change at all.

## R2: Does the static GTFS zip need any `GtfsStaticLoader` change for the 308 redirect?

**Decision**: No. `HttpClient` (used by `GtfsStaticLoader` to fetch `StaticZipUrls` entries)
follows HTTP redirects (301/302/307/308) by default via `HttpClientHandler.AllowAutoRedirect =
true`, which is the .NET default and is not overridden anywhere in this codebase. The downloaded
content at the redirect target is a normal flat zip with `trips.txt`/`shapes.txt`/`routes.txt` at
the root — the same shape every non-SEPTA city already provides.

**Rationale**: Confirmed by the compat report, which notes the redirect was followed
transparently during evaluation. This is unlike SEPTA's zip-of-zips, which required a genuine new
capability (nested-zip unwrap) in `GtfsStaticLoader.BuildCityShapeSetAsync`; RTD's zip needs
nothing beyond what every other config-only city already gets for free.

**Alternatives considered**: Point directly at the resolved
`api/download?feedType=gtfs&filename=google_transit.zip` URL instead of the redirecting one —
rejected as unnecessarily brittle (an internal API-shaped URL RTD could restructure) versus the
publicly documented, stable-looking `files/gtfs/google_transit.zip` path; following the redirect
is the correct, resilient choice and costs nothing.

## R3: Map origin coordinate for Denver

**Decision**: Denver Union Station / downtown transit core —
**39.7539, -105.0009** (Union Station, the hub where RTD's light rail lines, commuter rail lines,
and the densest bus network converge, and the anchor of Denver's downtown transit district).

**Rationale**: Matches the established precedent from TTC (043) and SEPTA (048) — the
transit-dense downtown core, not the geographic centroid of the metro area (which would land in a
low-density suburb or unincorporated area and show few initial vehicles).

**Alternatives considered**: Denver International Airport (RTD's A Line terminus, but far from
the metro's transit density) or a geographic centroid of RTD's service area (spans multiple
counties, would center on a sparse area) — both rejected for the same reasoning TTC/SEPTA
rejected their respective geographic-centroid alternatives.

## R4: Test strategy

**Decision**: No new unit tests. This feature adds no new production code —
`RailRouteIdMap`/`ApplyRailRouteIdMap` and `GtfsRtCity`'s generic construction are pre-existing,
shared, city-agnostic code paths (a plain dictionary lookup, no per-city branching), whether or
not a dedicated unit test currently exists for them; either way, RTD's config entry exercises the
same code WMATA already exercises in production. Verification is via `quickstart.md`: live feed
reachability, static zip fetch (confirming redirect-following works end-to-end against the real
endpoint), rail-remap correctness observed against live traffic (a vehicle reporting `103W`
renders as the `W` line, not "unknown"), and the standard existing-city regression pass.

**Rationale**: Adding tests for a config-only change that exercises only pre-existing shared code
would test the framework, not this feature — consistent with how WMATA, MBTA, and TTC's
onboardings (which also reused pre-existing generic code) added no new tests either. SEPTA was
the exception because it added new production code (nested-zip unwrap); RTD does not, so the
same bar doesn't apply here.

**Alternatives considered**: A dedicated `RailRouteIdMapTests` case for RTD's specific 8-entry
table — rejected as out of scope for a config-only onboarding; if `RailRouteIdMap` coverage is
judged insufficient, that's a pre-existing gap predating this feature (WMATA introduced the
mechanism with no dedicated test either), not something this feature should be the first to
backfill.
