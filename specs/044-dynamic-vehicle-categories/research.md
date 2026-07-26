# Phase 0 Research: Dynamic Per-City Vehicle Categories

The source design document (`docs/DYNAMIC_VEHICLE_CATEGORY_DESIGN_DOCUMENT.md`) is a completed decision record that resolved all 12 grill-me design questions. There were **no open NEEDS CLARIFICATION** entering planning. This file (a) digests the load-bearing decisions in the Decision / Rationale / Alternatives format, and (b) records verification of the design's key code claims against the as-built code, since the design doc used abbreviated paths and some line numbers.

## Part A — Verification of design claims against as-built code

Verified during planning (glob + read), because the design doc's paths were abbreviated (`src/Shared/...`) and its line numbers may have drifted:

| Design claim | As-built reality | Impact on plan |
|---|---|---|
| `enum TransitMode { Bus=0, Rail=1 }` + `RouteNearestPointRecord.TransitMode` at `Key(10)` | **Confirmed.** `src/ChefKnifeStudios.TransitJazz.Shared/Events/RouteNearestPointBatchEvent.cs:7,44` — `[property: Key(10)] TransitMode TransitMode = TransitMode.Bus`. | Retype to `[property: Key(10)] string Category = "bus"`; remove the enum. |
| `RouteShapeProperties.Mode` (enum) to be renamed `Category` + add `RouteType` | **Confirmed** it is a positional record with `Mode = TransitMode.Bus` then `City = null` last (`GtfsData/RouteShapeFeature.cs:16-22`). `JoinKey` is a computed member. | New `int RouteType = 3` must be inserted **before** `City` to keep `City` optional-last, or all call sites break. Category replaces `Mode` in place. |
| Real path prefix | Actual: `src/ChefKnifeStudios.TransitJazz.Shared/`, `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/`, `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/` — **not** the doc's `src/Shared/...`. | Plan uses real globbed paths; tasks must too. |
| Map paint expr matches capital `'Rail'` but wire value is lowercased → rail tier is dead code today | Corroborated by memory `project_map_paint_expr_case_mismatch` (audited 2026-07-18) and design §3.13. **Line numbers not re-verified**; implementer MUST grep for the actual match sites in `map-interop.js`/`vehicle-animator.js` rather than trust 72/74/167/169. | Re-key is a **visible behavior change** (rail dots grow), not a no-op — call out in review. Use `['downcase', ['get','category']]` guard. |
| Only classifier is `GtfsStaticLoader.ParseRouteMetadata`; Worker never classifies independently, reads category transitively from shape JSON | Corroborated by memory `project_route_type_classifier`. Design §3.3 marks it a *verified precondition*. **Line numbers (299-301, 324-326, 415, 448) not re-verified**; implementer greps. | Config stays WebAPI-only (FR-016); Worker needs no new config. |

**Action for implementation:** treat the design doc's line numbers as hints, not addresses. Grep for the symbols (`TransitMode`, `_routeMode`/`modeMap`, `ActiveBusCount`/`ActiveRailCount`, `transitMode` in JS, `NumTrainsRunning`) and edit every hit. The symbol set is complete in the design's §2.2 reference map + §5 change inventory.

## Part B — Decision-record digest

### D1 — Category cardinality: N per-city categories (not a Bus/Rail remap)
- **Decision:** Categories are an open-ended set of strings per city; `TransitMode` stops being a closed enum and becomes data.
- **Rationale:** Only way to represent TTC streetcars as a genuine third thing (own filter section + count), not "Rail with a different name."
- **Alternatives:** Two fixed buckets with per-city remap authority — rejected; can't give streetcars their own section/count.

### D2 — Wire encoding: string category key (not int + lookup table)
- **Decision:** `RouteNearestPointRecord` carries category as a plain `string` at the same `Key(10)` slot.
- **Rationale:** Self-describing, debuggable, no new sync primitive; MessagePack strings already used on this contract. Short strings → acceptable payload cost.
- **Alternatives:** Int code + per-city `Dictionary<int,string>` delivered at startup — smaller wire, but needs a new "deliver table before first batch" sync mechanism that doesn't exist here.

### D3 — Config ownership: WebAPI-authoritative, zero Worker config
- **Decision:** `route_type → category` map lives only in WebAPI config; WebAPI classifies once at static-load, stamps category onto `RouteShapeProperties`; Worker receives it transitively via the shapes JSON it already fetches.
- **Rationale:** Worker has no independent GTFS static parse and no independent use for the map; duplicating config invites drift.
- **Alternatives:** Duplicate the block into Worker config keyed by city — rejected (drift risk, no benefit).

### D4 — Config location: extend private `CityStaticEntry` (don't migrate to shared `CityConfig`)
- **Decision:** Add `IReadOnlyDictionary<string,string>? RouteTypeCategories` to WebAPI's private `CityStaticEntry` record, parsed in `LoadCityEntries()`.
- **Rationale:** Migrating the loader onto the Worker's typed `CityConfig` is scope creep (rewrites the config-reading mechanism as a side effect).
- **Alternatives:** Unify the two parallel config parsers — deferred as an independent cleanup.

### D5 — Fallback rules
- **D5a (no config block):** Fall back to today's exact rule `route_type` 0/1/2 → `"rail"`, else `"bus"`. Zero migration/behavior change for MARTA/WMATA/MBTA/NYMTA.
- **D5b (config present, unmapped `route_type`):** Default to `"bus"` + log a warning; keep the city loading. Rejected alternative: fail/skip the whole city — worse failure mode than one mis-bucketed route.

### D6 — Route-join-failure fallback: new `"unknown"` category
- **Decision:** The two Worker join-failure sites (vehicle whose route isn't in the category map) default to `"unknown"`, not `"bus"`.
- **Rationale:** Costs nothing extra to render (flows through the same dynamic loop), turns a previously-invisible data-quality signal into a visible, countable section.
- **Note:** The WebAPI pre-init placeholder default stays `"bus"` (it's essentially always overwritten before use — not a join failure).

### D7 — Audio/voicing: out of scope
- **Decision:** No `transit-synth.js` change; instruments are per-route, never per-mode. New `streetcar` category needs zero audio work. Dedicated streetcar voicing remains a separate future feature.

### D8 — Display order: `route_type` numeric ascending (client-derivable)
- **Decision:** Categories render sorted by the smallest `route_type` mapping to that category, ascending. WebAPI adds a `RouteType` int to `RouteShapeProperties`; client sorts distinct categories by `min(RouteType)`.
- **Rationale:** GTFS `route_type` orders naturally rail-family-low / bus-high → TTC `{0:streetcar,1:rail,3:bus}` renders `[streetcar, rail, bus]`; MARTA fallback `{rail:0/1/2, bus:3}` renders `[rail, bus]` (today's order preserved). No new ordering wire field on the per-tick batch; the int rides the once-per-startup shape catalog.
- **Alternatives:** Config declaration order (un-derivable client-side without new wire coupling — the payload's order is the KV store's iteration order, not config key order); alphabetical (arbitrary, reverses today's Rail-first order); explicit `Order` field (turns config into list-of-objects AND still needs delivery).
- **Tie-break:** Two distinct categories can't share a `min(RouteType)` under a `route_type→single category` map; ordinal category-key ordering breaks any residual tie for determinism.

### D9 — Display label: config supplies key, resx supplies label
- **Decision:** Config carries only the raw category key (`"streetcar"`); display text = `IStringLocalizer<RouteFilterResources>["streetcar"]`.
- **Rationale:** Keeps all copy in resx (Principle XII), translatable through the existing pipeline.
- **Consequence:** A genuinely new category still needs a small code-adjacent change (a resx entry, usually a CSS rule) — accepted; not frequent.

### D10 — CSS binding: generic wrapper class + `data-category` attribute
- **Decision:** All sections share `route-filters__section`; category exposed via `data-category="streetcar"`; styling via attribute selector.
- **Rationale:** Accepts any string safely; avoids making arbitrary config strings a de-facto CSS-identifier contract (a config author typing `"Light Rail"` would break a class-suffix scheme).

### D11 — Unstyled/unlabeled category: graceful fallback
- **Decision:** No matching CSS → base/neutral `.route-filters__section` styling. No resx entry → raw key via `IStringLocalizer` missing-key behavior. Never a hard failure.

### D12 — Count-label text: per-category running-noun key + template fallback
- **Decision:** Each category has a label (`rail`="Rail") and a running noun (`RunningNoun_rail`="trains running"). `RunningNoun(category)` returns `Loc["RunningNoun_{category}"]`; on miss, falls back to `string.Format(Loc["VehiclesRunningTemplate"], Loc[category])` (`"{0} running"` + label).
- **Rationale:** Label ≠ count-noun ("Rail" vs "trains", "Bus" vs "buses"); a single label-interpolated template would regress today's copy. Existing `NumTrainsRunning`/`NumBusesRunning` become `RunningNoun_rail`/`RunningNoun_bus` with identical values → pre-change copy preserved verbatim.

### D13 — Map vehicle-dot styling: binary big/small, re-keyed to wire value
- **Decision:** Keep exactly two size/stroke tiers; re-key the paint match from `'transitMode'`/`'Rail'` to `['downcase',['get','category']]`/`'rail'`.
- **Consequence:** This makes the rail tier fire for the **first time** (rail dots grow) — a visible, deliberate change, not a no-op. `downcase` guard removes the silent dependency on config authors never capitalizing a key.
- **Alternatives:** Per-category dot size — rejected (same generalization problem in a second surface for a cosmetic detail).

### D14 — Wire migration: atomic cutover, no dual-field period
- **Decision:** Retype `Key(10)` enum→string in one change; server+worker+client deploy together in one window (project's existing wire-contract discipline).
- **Alternatives:** Add `Category` alongside deprecated `TransitMode` with client `Category ?? Mode.ToString()` fallback — safer mid-rollout but doubles that slot's payload and leaves temporary code to remove.

### D15 — Fully dynamic client types, no fixed enum
- **Decision:** No `TransitMode`-equivalent enum on the client; every `TransitMode` API takes `string category`; every fixed count pair becomes a dictionary keyed by category.
- **Rationale:** A client "superset enum" would reintroduce the exact coupling being removed (client redeploy every time any city adds a category).

## Open items carried from design (explicitly deferred — NOT in scope)
- Category-key validation (lowercase/whitespace/CSS-safety) — deferred; mitigated by the `downcase` paint guard and optionally lowercasing `ClassifyCategory`'s return. Config is authored by the same people who own `appsettings.json`, not end users.
- Per-category audio/voicing — separate future feature.
- Unifying WebAPI `CityStaticEntry` and Worker `CityConfig` parsing — not in scope; both coexist post-change.
- Per-category map-dot **sizing** — map keeps the binary rail-vs-not tier.
