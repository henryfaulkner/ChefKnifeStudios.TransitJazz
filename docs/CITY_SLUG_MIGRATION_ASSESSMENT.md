# City Slug Migration — Plan & Estimation

**Scope:** migrate the city identifier from transit-agency names (`marta`, `wmata`,
`nymta`, …) to city names (`atlanta`, `washington-dc`, `new-york-city`, …), end to
end — URL fragment, client, API, worker, config, telemetry, analytics.

**Confirmed intent:** multi-agency-per-city is the destination. Cities will host
multiple transit authorities merged under one identifier (SF = Muni + BART,
DC = WMATA + regional rail). This document plans the rename as **step one of two**;
the `ITransitCity` composition work is step two and is deliberately out of scope here.

**Status:** planning only. No code changed.

---

## 1. What the identifier actually is

The URL fragment (`#marta`) is not a routing concern — it is the app's **single
city identity token**, and the same literal string is reused, unvalidated, as:

| Role | Location | Notes |
|---|---|---|
| URL fragment | `NavigationManagerExtensions.ResolveCity()` | lowercased; defaults to `marta` |
| SignalR group name | `TransitHub.JoinCityV2` / `LeaveCity` | free-form, no allow-list |
| HTTP query param | `?city=` on GTFS + transit endpoints | `GtfsEndpoints`, `TransitEndpoints` |
| Config key | `Cities[].Name` in 2 `appsettings.json` | worker + WebAPI, must stay byte-identical |
| Route-shape store key prefix | `"{city}:{route}"` | `GtfsStaticLoader.ReconcileCityAsync` |
| Telemetry column value | `city_name` in parquet | **immutable history** |
| Analytics pageview | `trackCityView` → `/{city}` | Umami |
| Resx key prefix | `WmataAudioOverlay*`, `TtcOverlay*` | PascalCase agency prefix |
| DI / strategy branch | `Program.cs` ×2, `MartaCity`, `NymtaCity` | `ITransitCity.Name` |

`CityNames.cs` (7 constants) is the nominal source of truth, but the literal
strings are **also hardcoded** in `CityFab.razor` (`location.hash='marta'`), the
two `appsettings.json` files, and test fixtures — so a constant-only rename
compiles clean and still breaks at runtime. That is the central risk.

## 2. The three genuinely hard boundaries

Everything else is mechanical. These three are not:

**a. Telemetry history is immutable (highest cost).**
`city_name` is written into dated parquet part-files
(`telemetry/dt=YYYY-MM-DD/*.parquet`) that are never rewritten. Renaming mid-stream
splits every historical query at the cutover date. This also directly collides with
feature 051 Phase 3, which is **blocked on ≥3 days of clean `batch_wire_bytes`
baseline** — a rename during that window destroys the baseline. Options: dual-write
both values for a transition period, map at query time in the MCP validator, or
**leave `city_name` on agency slugs** (see §6 — with multi-agency coming this may be
correct on the merits, not merely cheapest).

**b. SignalR group names are a live wire contract.**
Group names are the raw city string with no versioning. A client on `#atlanta`
joining group `atlanta` while the worker publishes to group `marta` produces
**silent failure — a connected client that receives nothing, no error**. Feature
051 already established the precedent for this exact hazard and solved it by
renaming `JoinCity` → `JoinCityV2` so stale peers fail loudly at join. This
migration needs the same treatment or a server-side alias map.
Per the repo's known deploy constraint, wire changes span **3 CI lanes** (server +
worker are atomic; client is separate; `deploy/marta-jazz` must land identically) —
so there is an unavoidable window where old clients meet a new server.

**c. Existing user URLs break.**
`#marta` is a shared/bookmarked link. `ResolveCity()` silently falls back to
`CityNames.Marta` on anything unrecognized, so a stale `#wmata` link would **land
the user in Atlanta** rather than erroring — a bad failure mode. Needs an explicit
legacy-slug alias map in `ResolveCity()`, ideally rewriting the hash so the URL
self-heals.

Two mitigating findings that *reduce* scope:
- The route-shape repository is `InMemoryKeyValueRepository` rebuilt from GTFS on
  startup — the `"{city}:"` key prefix is **not durable**. A deploy re-derives it. No data migration.
- There is no database anywhere in the city path. No schema change, no backfill.

## 3. Why this rename is a prerequisite, not cleanup

`#marta` encodes a **1:1 city-to-agency assumption that is about to be deliberately
broken**. Once Atlanta hosts MARTA plus a regional operator, or DC hosts WMATA plus
regional rail, an agency-named slug is not merely awkward — it is *semantically
wrong*. It names one participant in a set and implies that participant is the whole.
`#wmata` cannot honestly address a view containing regional rail alongside WMATA.

So the rename is not deferred-maintenance or aesthetic tidying. It is the
prerequisite that makes composition expressible: composition needs a stable
identifier that denotes **a place, not a provider**, and every boundary in §1 — the
SignalR group, the `?city=` param, the `Cities[].Name` config key, the route-shape
prefix — inherits whatever that token means. Building multi-agency on top of
agency-named slugs would bake the broken assumption into the very layer meant to
replace it, then require a second rename afterward through the same 3-lane deploy.

Rename now, because multi-agency is coming.

## 4. Sequencing: two changes, in this order

The slug rename and the `ITransitCity` composition work **must ship as separate
changes**, rename first.

**Step one — the rename (this document).** The cheap, mechanical half: ~35–40 files,
almost all find-and-replace, no new abstractions, no behavior change. Risk is
concentrated entirely in cutover mechanics (§2).

**Step two — composition (separate spec).** The expensive half: merging N feeds
under one city, restructuring the `Cities[]` config in both `appsettings.json` from
a flat city list into a city-with-agencies shape, and resolving **per-agency route-key
collisions** — two authorities in one city can each publish a route "1" or "Red",
and the current join key (`route_short_name`, falling back to `route_id`) has no
agency dimension to disambiguate them.

The reason to separate them is failure isolation. Shipping together means debugging
a **silent SignalR group mismatch** (§2b — no error, just a client receiving nothing)
and a **feed-merge bug** simultaneously, inside the same 3-lane deploy where old
clients are already meeting a new server. Those two failures present almost
identically from the user's seat: the map looks wrong or empty. Separated, step one's
blast radius is fully known before step two introduces any new logic — and step two
starts from a stable, correctly-named identifier instead of moving a target while
building on it.

## 5. `nymta` is the proof case — and the hardest single rename

NYMTA already does what every city will eventually do: **one city, many sources**. It
merges two feeds internally (subway synthesis + a 2-feed merge) rather than riding
the config-driven `GtfsRtCity` path that MARTA/WMATA/MBTA/TTC/SEPTA/RTD use. That
merge is implemented as **bespoke DI branches in both `Program.cs` files** plus a
dedicated `NymtaCity` class.

This makes it simultaneously:

- **The clearest rename conceptually.** `nymta` → `new-york-city` is the most obviously
  correct of the seven: the slug already denotes a merged multi-source view, so the
  agency name is already the wrong label for what it addresses.
- **The most tangled in code, and the highest-effort single rename.** Every other city
  is a config-only entry; NYMTA has hardcoded identity in two `Program.cs` DI branches,
  a `NymtaCity.Name` property, a self-referential internal API call
  (`GetSubwayStopOffsets?city={CityNames.Nymta}`), a special case in
  `GtfsStaticLoader` keyed on `city.Name == CityNames.Nymta` for its `/subway/` zip URL,
  and its own resx key family.

Treat `NymtaCity` as the **reference implementation** for step two: it is the existing
answer to "how does one city host several sources," and the composition spec should
generalize its pattern rather than invent one. Budget it as the outlier during step
one — the other six renames are genuinely mechanical; this one is not.

## 6. Telemetry: a decision to make, not a default to fall into

Near-term call is unchanged: **leave `city_name` on agency slugs** for now. It avoids
splitting historical queries at a cutover date and avoids the 051 Phase 3 baseline
collision (§2a), which is reason enough on its own.

But the multi-agency destination changes *why*. Once a city aggregates several
authorities, **per-agency granularity is plausibly the more useful telemetry key
long-term** — "how is BART performing" is a more actionable question than "how is San
Francisco performing," and a city-level column cannot answer it, while an agency-level
column can always be rolled up to city. Under that reading, `city_name` holding an
agency slug is not a compromise; it is closer to the schema you actually want, and the
real fix is eventually *renaming the column* to `agency_name` and adding a separate
city dimension.

Decide this deliberately rather than inheriting it. The concrete choice for step two:
does telemetry carry `agency_name` + `city_name` as two columns, or stay single-keyed?
Note the constraint either way — any change must stay in sync with
`tools/telemetry-mcp/internal/validate/validate.go`'s allow-list.

## 7. Estimate

Assumes the step-one slug rename only, legacy aliases retained, telemetry `city_name`
left on agency slugs (§6). Composition is **not** included.

| Area | Files | Complexity |
|---|---|---|
| `CityNames` + `ResolveCity` legacy alias map | 2 | Low |
| `CityFab.razor` hardcoded `location.hash` literals ×7 | 1 | Low |
| Both `appsettings.json` `Cities[].Name` | 2 | Low — must match byte-for-byte |
| Client: map origins, `AudioUnlockOverlay`, `InfoFab` switches | 3 | Low |
| Resx key prefixes (~40 keys) + switch arms | 3 | Low, tedious |
| Server DI branches + `ITransitCity.Name` | 4 | Low — except NYMTA (§5) |
| SignalR group compat (alias or version gate) | 3 | **High** — silent-failure risk |
| Tests + fixtures (~12 files) | ~12 | Medium |
| Skills/docs (`add-transit-city`, `discover-transit-city`, CLAUDE.md) | ~6 | Low |

**~35–40 files.** Mechanical edit ≈ 1 day. The cost is not the edit — it is the
**cutover**: 3-lane coordinated deploy, legacy-URL aliasing, telemetry continuity,
and verifying no silent group mismatch. Realistic **2.5–4 days** including a staged
rollout, or ~1.5 days if telemetry and legacy URLs are declared out of scope.

**Timing constraint (unchanged):** do not start before 051 Phase 3 ships and its
≥3-day `batch_wire_bytes` baseline window closes. The telemetry collision is real
regardless of how sound the end state is.

**Rollout order** — (1) alias map server-side accepting both slugs, (2) ship
server+worker, (3) ship client emitting new slugs, (4) remove aliases a release later.

## 8. Open decisions

1. **Slug format — settle before any code changes (§9).**
2. Telemetry: single-keyed, or `agency_name` + `city_name` in step two? (§6)
3. How long are legacy `#marta` URLs supported?
4. Does `MartaJazz` branding / the `deploy/marta-jazz` branch rename too? (out of
   scope here, but adjacent and will get asked)

*Resolved: multi-agency-per-city is the goal, sequenced as step two (§3, §4).*

## 9. Decide the slug format first

This is elevated out of the open-decisions list because it is the one choice that is
**expensive to revisit and must be made before the first edit**.

The slug is not a URL nicety. It becomes a **permanent public identifier for a
container of transit authorities**, simultaneously the shared/bookmarked URL, the
SignalR group name, the `?city=` API parameter, and the `Cities[].Name` config key.
Changing it later means a second pass through the same 3-lane deploy and a second
generation of legacy aliases layered on the first.

Open questions: `washington-dc` vs `washington`; `new-york-city` vs `nyc`;
hyphenation and casing rules for future multi-word cities. Worth weighing:
disambiguation across regions, whether abbreviations stay obvious as the city set
grows, and that these strings are user-visible in shared links. Pick a rule, write it
down, apply it uniformly to all seven — including whatever `discover-transit-city`
will generate for future cities, since that skill mints slugs autonomously and should
follow the same rule.
