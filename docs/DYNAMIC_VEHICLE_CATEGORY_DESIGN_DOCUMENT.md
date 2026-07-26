# Dynamic Per-City Vehicle Categories — Feature Specification & Decision Record

**Status:** Design complete, not yet implemented
**Author:** Henry Faulkner
**Date:** 2026-07-14
**Source:** `grill-me` design interview (12 questions resolved)
**Trigger:** TTC (Toronto) onboarding (`specs/043-toronto-ttc-transit/`) exposed that
streetcars (`route_type=0`) have nowhere to go in a Bus/Rail-only world; spec 043
deliberately deferred the fix rather than editing city-shared code. This document is
that follow-up.

---

## 1. Goal

Replace the single hardcoded `TransitMode { Bus, Rail }` enum — one classification rule
shared by every city — with a **per-city, config-driven, open-ended set of vehicle
categories**. A city can define as many categories as it needs (`"bus"`, `"rail"`,
`"streetcar"`, `"ferry"`, ...), and that flows all the way through the wire contract, the
Worker's fallback logic, and the client's `RouteFilter` UI / running-vehicle-count labels
— without any of those layers hardcoding a fixed list of category names.

**Non-goals (explicitly deferred):**
- Per-category audio/instrument voicing (transit-synth.js already assigns instruments
  per-**route**, not per-mode — confirmed no code path branches on Bus/Rail for sound).
  TTC "dedicated streetcar voicing" remains a separate future feature, exactly as spec
  043 left it.
- Migrating existing cities (MARTA/WMATA/MBTA/NYMTA) onto explicit config. They get the
  new behavior for free via an unchanged fallback rule; only TTC needs a real config
  block on day one.
- Unifying WebAPI's ad-hoc `IConfiguration` city-parsing with the Worker's typed
  `CityConfig` binding. Two parallel config-reading paths already exist in this codebase
  pre-change; this feature does not collapse them (see §4.1).

---

## 2. Current Architecture (single global classifier)

**The one and only classification site**, `GtfsStaticLoader.ParseRouteMetadata`
(`src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs:324-326`):

```csharp
// GTFS route_type: 0=tram/light-rail, 1=subway/heavy-rail, 2=commuter-rail — all Rail
var routeType = routeTypeIdx >= 0 && cols.Length > routeTypeIdx ? cols[routeTypeIdx] : "";
var mode = routeType is "0" or "1" or "2" ? TransitMode.Rail : TransitMode.Bus;
```

This is a three-line string switch, identical for every city, with **zero city-awareness**
— it runs inside `BuildZipRouteFeatures(cityName, ...)` (line 204) purely to stamp a mode
onto parsed routes; `cityName` is threaded through for cache-key prefixing only, never
consulted by the classifier itself.

### 2.1 The `TransitMode` enum and its wire-contract role

```csharp
// src/ChefKnifeStudios.TransitJazz.Shared/Events/RouteNearestPointBatchEvent.cs:7
public enum TransitMode { Bus = 0, Rail = 1 }
```

Lives in `Shared`, referenced by both server and client. It is a **MessagePack wire-contract
member** (`RouteNearestPointRecord.TransitMode`, same file, line 44) — the exact kind of
field feature 040 called out as "frozen" and requiring coordinated deploys across
[the 3 CI lanes](#56-wire-migration-strategy) (server+worker atomic, client separate,
MartaJazz ships from `deploy/marta-jazz`).

### 2.2 Full reference map (as of this design)

| Layer | File | Role |
|---|---|---|
| Definition | `Shared/Events/RouteNearestPointBatchEvent.cs:7,44` | enum; MessagePack wire field |
| Definition | `Shared/GtfsData/RouteShapeFeature.cs:16` | `RouteShapeProperties.Mode` — hand-serialized as a JSON string by `BuildLineStringFeature` (`GtfsStaticLoader.cs:478`), re-parsed to enum via `JsonStringEnumConverter` on read only |
| Classifier | `WebAPI/GtfsStatic/GtfsStaticLoader.cs:208,217,299-301,326,457` | the only classification logic + defensive default |
| Consumer | `TransitDataWorker/Worker.cs:32-33,187,193,204,244,339,415,448` | builds `_routeMode` dict from WebAPI's shape JSON; **never classifies independently** |
| Tests | `WebAPI.Tests/GtfsStaticLoaderTests.cs` | ~10 assertions on `TransitMode.Bus`/`.Rail` (Bus/Rail/shuttle/Kingston cases) |
| Tests | `WebAPI.Tests/EventEnvelopeMessagePackTests.cs` | wire round-trip |
| Client map pins | `Client.WebApp/Pages/TransitMap.razor.cs:498` | `r.TransitMode.ToString().ToLowerInvariant()` → JS |
| Client ViewModel | `Client.Shared/ViewModels/RouteFilterViewModel.cs` | `RouteItem.Mode`; `SelectAll`/`ClearSelection`/`HasSelectionFor`; `_railVehicleIds` binary split |
| Client UI | `Client.Shared/Components/RouteFilters.razor`, `.razor.cs` | two hardcoded `@if` sections |
| Client UI | `Client.Shared/Components/TransitRunningLabel.razor` | two hardcoded count rows |
| Client JS | `Client.Shared/wwwroot/js/map-interop.js:72,74,167,169` | MapLibre paint expr matches literal `'Rail'` — but the property is lowercase `'rail'` (razor.cs:498), so **the match never fires today: dead rail tier** (§3.13) |
| Client JS | `Client.Shared/wwwroot/js/vehicle-animator.js:348,360,586` | passthrough only, not consumed for logic |

**Confirmed NOT a second classification path** (verified by direct code inspection, not
assumed): `RailRealtimeAdapter`/`MartaCity.FetchRailEntitiesAsync` (MARTA's bespoke rail
JSON merge), `CityConfig.RailRouteIdMap` (WMATA route-ID aliasing), and NYMTA's dual
`GtfsRtUrls`/`BusGtfsRtUrls` feeds and subway-synthesis path all build raw
`FeedEntity`/`VehiclePosition` objects with **no `TransitMode` field at all** — mode is
resolved later, uniformly, only when the Worker joins a vehicle to its route via
`_routeMode`. There is exactly one place classification happens.

### 2.3 The two fallback-default sites (distinct from the classifier)

Separate from "which category does `route_type=X` map to," these three sites answer "what
happens when a route can't be classified/found at all":

```csharp
// GtfsStaticLoader.cs:217 — before metadata lookup overwrites it
var mode = TransitMode.Bus;

// Worker.cs:415 and :448 — route not found in the mode map
modeMap != null && modeMap.TryGetValue(routeJoinKey, out var m) ? m : TransitMode.Bus
```

### 2.4 The two-section UI, traced to its origin

`RouteFilters.razor` (lines 30-66) renders **exactly two hardcoded `@if` blocks**, each
gated by `.Any(r => r.Mode == TransitMode.Rail/.Bus)` and each with its own irregular CSS
class (`route-filters__rail`, `route-filters__buses` — not even a consistent
`route-filters__{mode}` pattern). `TransitRunningLabel.razor` (lines 14-29) mirrors this
with two hardcoded count rows keyed off `ActiveRailCount`/`ActiveBusCount`. Both trace back
to **spec 029-route-filter-split**, which reused (not created) `TransitMode` and defined
the "section renders only if non-empty" rule still in force.

### 2.5 Existing per-city config surface

`WebAPI/appsettings.json` (lines 32-107) carries a `Cities:` array of **5** entries
(`marta`, `wmata`, `mbta`, `nymta`, `ttc`); `appsettings.Development.json` (lines 23-93)
carries only **4** — the same set **minus `ttc`**. Both are parsed by
`GtfsStaticLoader.LoadCityEntries()` (line 116) via raw `IConfiguration.GetSection` into a
**private, WebAPI-only** record:

```csharp
// GtfsStaticLoader.cs:28
sealed record CityStaticEntry(string Name, string[] StaticZipUrls, string? ApiKeyEnvVar);
```

This is a *different, smaller* type than the Worker's typed `CityConfig`
(`TransitDataWorker/Cities/CityConfig.cs`), which the Worker binds via
`Configuration.GetSection("Cities").Get<List<CityConfig>>()`. WebAPI ignores every
`CityConfig` field it doesn't need (`RailRealtime`, `RailRouteIdMap`, `EmitsTelemetry`,
`RouteIdNormalization`, `BusGtfsRtUrls`) — these two parsers of the *same config section*
already coexist today, pre-change.

`ITransitCity` (`TransitDataWorker/Cities/ITransitCity.cs`) — the Worker-side per-city
strategy interface from the multi-city feature — has **no route-type/category member of
any kind**:

```csharp
public interface ITransitCity
{
    string Name { get; }
    Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct);
    bool EmitsTelemetry { get; }
}
```

It is irrelevant to this change — classification never touches the Worker's realtime-fetch
abstraction; it's resolved entirely upstream, once, in WebAPI.

---

## 3. Design Decisions

Each decision is numbered to its originating grill-me question and states the choice, the
rejected alternative, and the rationale.

### 3.1 (Q1) Category cardinality: N per-city categories, not a Bus/Rail remap

**Decision:** Cities define an open-ended set of category names, not a remapping onto a
fixed two-value type. `TransitMode` stops being a closed C# enum and becomes data.

**Rejected:** Keeping exactly two buckets (Bus, Rail) with per-city remap authority over
which `route_type` falls into which bucket. Rejected because it can't represent TTC
streetcars as a genuine third thing — only as "Rail with a different name" — which doesn't
solve the actual problem (streetcars need their own filter section and count, not to
usurp Rail's).

**Consequence:** This is the load-bearing decision. It cascades into the wire format, the
Worker's fallback types, and a full rewrite of both `@if`-pair UI components into loops.

### 3.2 (Q2) Wire encoding: string category key, not int + lookup table

**Decision:** `RouteNearestPointRecord` carries category as a plain `string` (e.g.
`"streetcar"`), not an int code paired with a separately-delivered per-city lookup table.

**Rejected:** Int code + `Dictionary<int,string>` delivered once at city-select/startup.
Smaller wire payload (in the spirit of feature 040's size-reduction work), but introduces
a synchronization dependency — the client must have received the table before any record
is interpretable, and there's no existing mechanism in this codebase for delivering a
per-city static lookup table ahead of the first batch.

**Rationale:** Self-describing, debuggable, no new sync primitive. MessagePack strings are
already used elsewhere on this exact contract. Categories are short strings; the payload
cost is acceptable against the complexity avoided. `RouteShapeProperties.Mode` already
travels as a **string on the GeoJSON leg** — but note the mechanism: `BuildLineStringFeature`
*hand-writes* `"mode":"Bus"` via string interpolation (`GtfsStaticLoader.cs:478`), and the
`JsonStringEnumConverter` only re-parses that string back to the enum on the *deserialize*
side. So for the WebAPI→Worker/client GeoJSON leg this decision is a **type correction**
(string in → string out, dropping the round-trip through the enum), not a new behavior. The
MessagePack SignalR leg (`RouteNearestPointRecord`) is the one that genuinely changes wire
encoding, int→string (§4.3). See §5.1/§5.4.

### 3.3 (Q3) Config ownership: WebAPI-authoritative, zero Worker config changes

**Decision:** The per-city `route_type → category` map lives **only** in WebAPI's config.
WebAPI classifies once, at static-GTFS-load time, and stamps the category string onto
`RouteShapeFeature.Properties`. The Worker needs no new configuration at all.

**Verified precondition:** The Worker's `_routeMode` dict (soon `_routeCategory`) is built
exclusively by deserializing `RouteShapeFeature` JSON fetched from WebAPI's
`/gtfs/.../all-route-shapes` endpoint (`Worker.cs:288,297,620,628`) — confirmed by direct
trace, not assumption. The Worker performs **no independent GTFS static parsing**. It
already receives category transitively.

**Rejected:** Duplicating the config block into both WebAPI's and Worker's `appsettings.json`
keyed by city name. More config surface, real drift risk (a category added to one file and
not the other), for no benefit given the Worker has no independent use for it.

### 3.4 (Q4) Config location: extend `CityStaticEntry`, don't migrate to shared `CityConfig`

**Decision:** Add `Dictionary<string,string>? RouteTypeCategories` directly to
`GtfsStaticLoader`'s private `CityStaticEntry` record, parsed in `LoadCityEntries()`
alongside the existing fields.

**Rejected:** Migrating `GtfsStaticLoader` to bind through the Worker's typed `CityConfig`
class instead (which would also collapse the two-parallel-parsers situation from §2.5).
Correct-in-the-abstract, but scope creep: it means refactoring the loader's entire
config-reading mechanism (raw `IConfiguration` walk → `IOptions<List<CityConfig>>`) as a
side effect of a route-classification feature. Deferred as a separate, independent cleanup
if ever pursued.

### 3.5 (Q5a/Q5b) Fallback rules

**Q5a — city has no `RouteTypeCategories` config block at all:**

**Decision:** Fall back to the exact current hardcoded rule: `route_type` 0/1/2 → `"rail"`,
else → `"bus"`. Zero migration cost, zero behavior change for MARTA/WMATA/MBTA/NYMTA.
Only TTC (and any future city that needs it) gets an explicit block.

**Rejected:** Requiring every city to migrate to explicit config on day one. Rejected as
unnecessary authoring burden with no corresponding benefit — the four existing cities'
current behavior is already correct for their real-world fleets.

**Q5b — city HAS config, but a `route_type` value appears that isn't listed in its map:**

**Decision:** Default to `"bus"` + log a warning. Matches today's overall fallback-to-Bus
behavior; keeps ingestion running even if a feed adds an unanticipated `route_type`
(GTFS defines 0-7 and 11-12; a city's config need not enumerate all of them, only the ones
it actually uses).

**Rejected:** Fail loudly / skip the city's entire load on an unmapped value. Rejected — an
entire city going dark over one new/unexpected `route_type` value in upstream GTFS is a
worse failure mode than one route being mis-bucketed as bus.

### 3.6 (Q12) Route-join-failure fallback: new `"unknown"` category

**Decision:** The two **join-failure** sites (`Worker.cs:415`, `:448` — a vehicle whose
route isn't found in `_routeCategory` at all, distinct from §3.5's "route_type not listed")
now default to a real `"unknown"` category, not `"bus"`.

**Rejected:** Keeping the silent `"bus"` default (which was the initial instinct, matching
§3.5's rule for consistency). Reconsidered once the N-category UI machinery was already
being built anyway: an `"unknown"` category costs nothing extra to render (it flows through
the exact same dynamic loop as any other category) and turns a previously-invisible
data-quality signal — vehicles that failed to join to a route — into a visible, countable
filter section instead of silently inflating the bus count.

**Note:** `GtfsStaticLoader.cs:217`'s defensive default (`var mode = TransitMode.Bus;`,
overwritten by metadata almost immediately) is a different case — it's not a join failure,
it's a pre-initialization placeholder — and stays `"bus"` since it's essentially always
overwritten before use.

### 3.7 (Q6) Audio/voicing: explicitly out of scope

**Decision:** No `transit-synth.js` changes. Verified by direct inspection: the synth
module never branches on `TransitMode`/bus/rail anywhere — instrument assignment is
per-**route** (feature 009's route=instrument design), not per-mode. A new `"streetcar"`
category requires zero audio-layer changes. The "dedicated streetcar/tram voicing" item
spec 043 tracked as a follow-up (`tasks.md:112`, `quickstart.md:50`) remains a separate,
future feature.

### 3.8 (Q7) Category display order: `route_type` numeric ascending (client-derivable)

**Decision:** Categories render sorted by the **smallest GTFS `route_type` that maps to
that category**, ascending. `route_type` is already an `int` the client can derive with no
new wire field: WebAPI stamps it onto each `RouteShapeFeature.Properties` (a new
`RouteType` int, see §4.3) alongside `Category`, and the client sorts the distinct
categories by `min(RouteType)` within each. Because GTFS `route_type` orders naturally as
rail-family-low / bus-high (0=tram, 1=subway, 2=commuter-rail, 3=bus), TTC's
`{0: streetcar, 1: rail, 3: bus}` renders `[Streetcar (0)] [Rail (1)] [Bus (3)]` — the
same order the config author would intuitively want — and MARTA's fallback-classified
`{rail: 0/1/2, bus: 3}` renders `[Rail] [Bus]`, matching today's exact section order.

**Why not config declaration order (the original instinct, now rejected):** The client
receives only per-route category strings over the `/gtfs/routes/shapes` payload; that
payload's element order is the key-value store's iteration order (`GtfsEndpoints.cs:100`),
**not** the config's `RouteTypeCategories` key order. Config lives *only* in WebAPI (§3.3),
and nothing in the pipeline carries its declaration order to the client — so "render in
declaration order" is unimplementable without adding a per-city ordered-category list to
the wire/API, which the §3.3 WebAPI-only-config boundary is specifically designed to avoid.
Sorting by an int the client already has (once `RouteType` is on the wire) needs no new
delivery mechanism and no ordering coupling between the two config parsers.

**Rejected:**
- *Config declaration order* — see above; not derivable client-side without a new wire
  field, contradicting §3.3.
- *Alphabetical by category key* — deterministic and client-derivable, but arbitrary; puts
  `bus` before `rail` before `streetcar`, which has no relationship to real-world
  prominence and reverses today's Rail-first section order (a visible regression for the
  four existing cities).
- *Explicit `Order` field per category* — most authoritative, but turns config from a
  simple `Dictionary<string,string>` into a list-of-objects AND still needs the order
  delivered to the client (a new wire field) — the same coupling `route_type`-ascending
  avoids for free.

**Edge case (ambiguous min):** if a city maps *different* `route_type`s to the *same*
category (e.g. `{0: rail, 1: rail}`), `min(RouteType)` collapses them to one section keyed
by the lowest — correct. If two *distinct* categories somehow share a `min` (impossible
under a `route_type → single category` map, since each `route_type` maps to exactly one
category), the tie is broken by category-key ordinal ordering for determinism.

### 3.9 (Q8) Category display label: config supplies the key, resx supplies the label

**Decision:** City config only ever carries the raw category **key** (`"streetcar"`) —
the same string used for wire transport, counting, and CSS data-attributes. Display text
comes from a resx entry keyed by that same string
(`IStringLocalizer<RouteFilterResources>["streetcar"]`), following the existing
localization convention used for every other UI string in the app.

**Rejected:** Config carrying the label directly (e.g. `{key: "streetcar", label:
"Streetcars"}`), bypassing resx. Faster to add a category (config-only, no resx/code
deploy), but breaks the all-copy-goes-through-resx convention and makes the new category's
label untranslatable through the existing pipeline — inconsistent with how every other
string in this app is authored.

**Consequence:** Adding a genuinely new category still requires a small code-adjacent
change (a resx entry, and typically a CSS rule — see §3.10/§3.11) — not fully zero-code.
Accepted as a reasonable tradeoff; this is not a frequent operation.

### 3.10 (Q9) CSS binding: generic wrapper class + `data-category` attribute

**Decision:** Every `RouteFilters` section shares one wrapper class
(`route-filters__section`), with the category exposed via `data-category="streetcar"`.
Category-specific styling is authored as an attribute selector:
`.route-filters__section[data-category="streetcar"] { ... }`.

**Rejected:** Using the category key as a literal CSS class suffix
(`route-filters__streetcar`), matching today's `route-filters__rail` pattern most closely.
Rejected because it makes arbitrary config strings a de-facto CSS-identifier contract
(lowercase, no spaces/special characters) with no validation anywhere — a config author
typing `"Light Rail"` would silently produce a broken/unmatched class name. The
attribute-selector approach accepts any string safely.

### 3.11 (Q10) Unstyled/unlabeled category: graceful fallback, not a hard failure

**Decision:** A category with no matching CSS rule renders with base/neutral
`.route-filters__section` styling (no category-specific override applies — nothing breaks).
A category with no resx entry falls back to the raw key string via `IStringLocalizer`'s
built-in missing-key behavior.

**Rejected:** Treating a missing resx/CSS pairing as a config error (throw or fail loudly
at startup). Rejected as unwarranted rigidity for what is really just "the PR that adds a
category is incomplete" — the app should keep working, looking plain, until polish lands,
not go dark over cosmetic incompleteness.

### 3.12 (Q11) Count-label text: per-category running-noun key with a template fallback

**Decision:** Each category supplies **two** resx entries: its filter-section **label**
(`rail` = `"Rail"`, §3.9) and a **running-count noun** under a constructed key
(`RunningNoun_rail` = `"trains running"`, `RunningNoun_bus` = `"buses running"`,
`RunningNoun_streetcar` = `"streetcars running"`). `TransitRunningLabel` renders the count
(existing `__count` element) beside a `RunningNoun(category)` helper (§4.7) that returns
`Loc[$"RunningNoun_{category}"]`. When that key is missing, the helper falls back to a shared
`"VehiclesRunningTemplate"` = `"{0} running"` interpolated with the category **label**
(`string.Format(Loc["VehiclesRunningTemplate"], Loc[category])`), so a brand-new category
still renders sensibly (count `3` + `"Streetcar running"`) until its polished noun lands —
the same graceful-degradation posture as §3.11. (The count stays a separate element, as
today; only the trailing noun goes through the helper.)

**Why not label-only (the original single-template instinct, now rejected):** Interpolating
the *section label* into one `"{0} running"` template would regress today's hand-tuned copy:
the rail **label** is `"Rail"` but the rail **running-noun** is `"trains"` — a single
template produces `"Rail running"` instead of `"trains running"`, and `"Bus running"`
instead of `"buses running"` (labels are singular section headers; count sentences want the
plural vehicle noun). The label and the count-noun are genuinely different words, so one
string can't serve both without a copy regression on the four existing cities.

**Consequence:** Adding a category takes **two** resx entries for polished copy (label +
running-noun), but only **one** (label) is mandatory — the template fallback covers the
noun. This restores the pre-change copy exactly (`NumTrainsRunning`/`NumBusesRunning`
become `RunningNoun_rail`/`RunningNoun_bus` with identical values) while keeping new
categories zero-blocking.

**Rejected:** A pure `Num{Category}Running` convention with **no** fallback (matching
today's literal `NumTrainsRunning`/`NumBusesRunning` keys) — correct for the four known
categories but hard-fails a new category with a `MissingManifestResourceException`-style
blank until someone adds its bespoke sentence, violating §3.11's "keep working, look plain"
principle.

### 3.13 (Q13) Map vehicle-dot styling: binary big/small, re-keyed to the wire value

**Decision:** `map-interop.js`'s MapLibre paint expressions
(`['match', ['get', 'transitMode'], 'Rail', 9, 6]`, lines 72/74/167/169) keep exactly two
size/stroke tiers, re-keyed to match the wire value `'rail'` and reading the renamed
GeoJSON property `'category'` (`['match', ['get', 'category'], 'rail', 9, 6]`).

**⚠️ This re-key is a behavior CHANGE, not a preservation — and it fixes a latent bug.**
The expression matches literal **`'Rail'`** (capital), but the value written to the GeoJSON
`transitMode` property is *already lowercase today*: `TransitMap.razor.cs:498` sends
`r.TransitMode.ToString().ToLowerInvariant()` → `"rail"`. So the match **never fires right
now** — every vehicle (rail included) already renders at the default size 6 / stroke 1, and
the rail 9/2 tier is effectively dead code. Re-keying to `'rail'` will make rail dots render
larger **for the first time**. This is desirable (it's what the tier was always meant to do),
but it must be called out as a *visible map change* shipping with this feature, not a silent
no-op — reviewers comparing before/after screenshots will see rail dots grow, and that is
correct.

**Case sensitivity note:** dropping `.ToLowerInvariant()` on the C# side (§5.5, now that the
value passes through verbatim as `r.Category`) means the JS boundary becomes
**case-sensitive** — `'rail'` matches, `'Rail'` would not. This is safe *only because* the
`rail`/`bus` category keys are authored lowercase by convention; it ties directly to the
unenforced-lowercase-key open item (§7). To keep the pre-existing case-insensitivity as a
belt-and-suspenders guard, the paint expression MAY instead match on a downcased property
(`['match', ['downcase', ['get', 'category']], 'rail', 9, 6]`) — MapLibre supports
`downcase` in expressions. Recommended, since it costs nothing and removes the silent
dependency on config authors never capitalizing a key.

**Rejected:** Extending the match expression to give every category its own distinct dot
size (mirroring the N-section filter UI). Rejected as the same generalization problem
recurring in a second surface (MapLibre paint expressions) for a purely cosmetic
map-marker detail; not worth the added JS maintenance for this change.

### 3.14 (Q13, wire migration) Atomic cutover, no dual-field transition period

**Decision:** `RouteNearestPointRecord.TransitMode` (enum) is renamed/retyped outright to
`Category` (string) in one change. Server + Worker + Client deploy together in the same
coordinated window — the same discipline already used for this project's other
wire-contract changes (see `project_signalr_wire_deploy_constraint` — wire changes span 3
CI lanes: server+worker atomic, client separate, MartaJazz ships from `deploy/marta-jazz`).

**Rejected:** Adding `Category` as a new field alongside the deprecated `TransitMode`,
with client code preferring `Category ?? Mode.ToString()` until all lanes confirm
deployment, then removing `Mode` in a follow-up. Safer mid-rollout, but doubles payload
size during the transition (partially undoing feature 040's whole point) and adds
temporary fallback code that has to be remembered and removed later. Rejected in favor of
matching this project's existing wire-contract deploy discipline instead of working around it.

### 3.15 (Q4, client typing) Fully dynamic client types, no fixed enum anywhere

**Decision:** No `TransitMode`-equivalent enum survives on the client. Every API that used
to take `TransitMode` takes a plain `string category` instead; every fixed pair of named
properties becomes a dictionary keyed by category string.

**Rejected:** A client-side "superset" enum listing every category any city currently
uses. Rejected because it reintroduces exactly the coupling this whole change exists to
remove — a client code change and redeploy would be required every time *any* city
introduces a new category, defeating the purpose of making this config-driven.

---

## 4. Target Architecture

### 4.1 Config shape (WebAPI `appsettings.json`, additive to existing `Cities:` array)

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

MARTA (and WMATA/MBTA/NYMTA) omit `RouteTypeCategories` entirely and keep today's exact
behavior via the fallback rule (§3.5). Only TTC needs the new block. Display order is **not**
driven by JSON key order — it's `route_type`-ascending, derived client-side from the
`RouteType` int now carried on each route (§3.8) — so TTC's streetcars (`route_type=0`)
render first regardless of how the config object is keyed. The real `ttc` entry also keeps
its existing `GtfsRtUrls`/`EmitsTelemetry` fields (elided here for brevity);
`RouteTypeCategories` is purely additive to whatever the entry already has.

### 4.2 `GtfsStaticLoader.cs` changes

```csharp
// Extended CityStaticEntry (line 28)
sealed record CityStaticEntry(
    string Name,
    string[] StaticZipUrls,
    string? ApiKeyEnvVar,
    IReadOnlyDictionary<string, string>? RouteTypeCategories);

// Classifier (was lines 324-326)
static string ClassifyCategory(string routeType, IReadOnlyDictionary<string, string>? cityMap, string cityName, ILogger logger)
{
    if (cityMap is not null)
    {
        if (cityMap.TryGetValue(routeType, out var category))
            return category;
        logger.LogWarning("Unmapped route_type {RouteType} for city {City}, defaulting to bus", routeType, cityName);
        return "bus";
    }
    // No config block for this city: fall back to today's exact rule (§3.5 Q5a).
    return routeType is "0" or "1" or "2" ? "rail" : "bus";
}
```

**Serialization is hand-written, not converter-driven.** `BuildLineStringFeature`
(`GtfsStaticLoader.cs:478`) emits mode with a raw string interpolation
(`sb.Append($",\"mode\":\"{mode}\"")`), NOT through `JsonStringEnumConverter` — so the
serialize leg is a trivial swap to `sb.Append($",\"category\":{JsonSerializer.Serialize(category)}")`
(quoted via the serializer to stay safe against arbitrary category strings) plus a new
`sb.Append($",\"routeType\":{routeType}")`. The `JsonStringEnumConverter` in
`JsonOptions.Get()` only ever mattered on the **deserialize** leg (Worker/WebAPI reading the
stored GeoJSON back into `RouteShapeProperties.Mode`); once `Mode` becomes a plain string
that converter is dead weight for this type. See §5.1 for whether it can be removed outright
(it can't blindly — verify no other enum relies on it).

### 4.3 Wire contract (`Shared/Events/RouteNearestPointBatchEvent.cs`)

```csharp
// TransitMode enum: REMOVED entirely.

[MessagePackObject]
public sealed record RouteNearestPointRecord(
    // ... Key(0)–Key(9) unchanged ...
    [property: Key(10)] string Category = "bus"); // was: [Key(10)] TransitMode TransitMode = TransitMode.Bus
```

**MessagePack wire-encoding note:** `Category` keeps the *same positional* `Key(10)`, but
the encoding of that slot changes from a packed integer (enum, 1 byte) to a MessagePack
string (~5–10 bytes for `"streetcar"`). This is the payload-size cost §3.2 accepted; it is
per-record, so it partially offsets feature 040's thinning — acceptable for short category
strings but worth noting since 040 fought for every byte. `EventEnvelopeMessagePackTests`
(the `Key(10)` positional round-trip) must be updated in lockstep, or deserialization
silently corrupts (see §5.2).

```csharp
// Shared/GtfsData/RouteShapeFeature.cs:16
public sealed record RouteShapeProperties(
    string RouteId,
    string? RouteShortName,
    string? Color,
    string? TextColor,
    string Category = "bus",   // was: TransitMode Mode = TransitMode.Bus
    int RouteType = 3,         // NEW — raw GTFS route_type, drives client display order (§3.8)
    string? City = null)
```

**Why `RouteType` on `RouteShapeProperties` (and NOT on `RouteNearestPointRecord`):** the
client's category *display order* (§3.8) is computed once from the route catalog
(`RouteFilterViewModel.BuildRouteItems` reads `RouteShapes`, §4.5), never from the per-tick
vehicle batch — so the ordering int rides `RouteShapeProperties` (fetched once at startup)
and adds **zero** bytes to the high-frequency `RouteNearestPointRecord` batch. `RouteType`
defaults to `3` (bus) so any pre-existing stored GeoJSON without the field deserializes as
bus-ordered, harmless. `BuildLineStringFeature` (`GtfsStaticLoader.cs:451`) must emit the
new `"routeType"` JSON property alongside `"category"`, and `ClassifyCategory` (§4.2) is
called with the `routeType` string already in hand, so no extra parsing is needed — the int
is `int.Parse(routeType)` (default 3 on parse failure).

### 4.4 Worker fallback sites (`Worker.cs:415,448`, `217`-equivalent)

```csharp
// Route not found in the map at all → "unknown" (§3.6), not "bus"
categoryMap != null && categoryMap.TryGetValue(routeJoinKey, out var c) ? c : "unknown"
```

### 4.5 Client ViewModel (`RouteFilterViewModel.cs`)

```csharp
public class RouteItem
{
    public string RouteJoinKey { get; init; }
    public string Label { get; init; }
    public string Color { get; init; }
    public bool IsSelected { get; set; }
    public string Category { get; init; }   // was: TransitMode Mode
}

public interface IRouteFilterViewModel : IViewModel, IDisposable
{
    IEnumerable<RouteItem> RouteItems { get; }
    void SelectRoute(RouteItem routeItem);
    void SelectAll(string category);         // was: TransitMode mode
    void ClearSelection(string category);
    void SetHoveredRoute(RouteItem? routeItem);
    bool HasSelectionFor(string category);
    // ... unchanged members ...
    IReadOnlyList<string> CategoryOrder { get; }                       // NEW — §3.8 display order
    IReadOnlyDictionary<string, int> ActiveCountsByCategory { get; }   // was: ActiveBusCount, ActiveRailCount
}
```

Internal accumulator changes:
- `HashSet<string> _railVehicleIds` → `Dictionary<string, string> _vehicleCategory` (vehicleId → category key).
- `RecomputeActiveTransitCounts()` builds `Dictionary<string,int>` by grouping over
  `_vehicleCategory.Values` instead of the binary `Contains(id) ? rail : bus` trick — this
  is the one real data-structure change, not just a type-widen, since the binary partition
  no longer holds once there are 3+ categories.

**MVVM reactivity — the two `[ObservableProperty]` count fields can't just become a
dictionary.** Today `_activeBusCount`/`_activeRailCount` are `[ObservableProperty]` fields;
`TransitRunningLabel.OnViewModelPropertyChanged` re-renders when `PropertyChanged` fires for
those specific names (`RouteFilterResources.razor` line 104 filters on
`nameof(ActiveBusCount) or nameof(ActiveRailCount)`). A `Dictionary<string,int>` property
only raises `PropertyChanged` when the whole reference is **reassigned**, never when its
contents mutate. So:
  - `ActiveCountsByCategory` must be backed by an `[ObservableProperty]` field whose setter
    is fed a **freshly-built dictionary** each recompute (`ActiveCountsByCategory = newDict;`),
    never mutated in place — otherwise the label goes stale.
  - `TransitRunningLabel`'s `PropertyChanged` filter must broaden from the two removed names
    to `nameof(IRouteFilterViewModel.ActiveCountsByCategory)` (and `CategoryOrder` if the
    label ever orders rows). Forgetting this filter update is the most likely
    silent-stale-count bug in the rewrite — call it out in the PR checklist.
  - `CategoryOrder` is built once in `BuildRouteItems` from the route catalog:
    `RouteShapes` grouped by `Category`, each category's sort key = `min(RouteType)` over
    its routes, ascending, ordinal tie-break (§3.8). It changes only when the route catalog
    reloads, so it needn't be `[ObservableProperty]`-reactive on every tick — but assign it
    alongside `RouteItems` inside `BuildRouteItems` so the two never disagree.

### 4.6 `RouteFilters.razor` — loop, not `@if` pair

```razor
@{
    // Ordered category list is precomputed by the ViewModel (§3.8, route_type-ascending) so
    // the component never derives an ad-hoc order from data that doesn't carry it.
    var categories = RouteFilterViewModel.CategoryOrder;
}
<div class="route-filters @(_isDark ? "route-filters--dark" : "")">
    @foreach (var category in categories)
    {
        @if (RouteFilterViewModel.RouteItems.Any(r => r.Category == category))
        {
            <div class="route-filters__section" data-category="@category">
                <MatSubtitle1 Class="route-filters__section-label route-filters__section-label--clickable"
                              @onclick="() => HandleSelectAll(category)">
                    @Loc[category]  @* resx miss → raw key, §3.11 *@
                </MatSubtitle1>
                @if (RouteFilterViewModel.HasSelectionFor(category))
                {
                    <MatIconButton Icon="close" Class="route-filters__clear-btn"
                                   @onclick="() => HandleClearSelections(category)" />
                }
            </div>
            <div class="route-filters__pills" data-category="@category">
                @foreach (var routeItem in RouteFilterViewModel.RouteItems.Where(r => r.Category == category))
                {
                    @RoutePill(routeItem)
                }
            </div>
        }
    }
</div>
```

*(Category order source: `IRouteFilterViewModel.CategoryOrder` (§4.5) is built once in
`BuildRouteItems` by grouping `RouteShapes` on `Category` and sorting each group by
`min(RouteType)` ascending (§3.8). The component just iterates it — it never tries to
reconstruct order from `RouteItems`, which carries per-route category strings but no
ordering signal on its own.)*

### 4.7 `TransitRunningLabel.razor` — loop, not two rows

```razor
<div class="transit-running-label @(_isDark ? "transit-running-label--dark" : "")">
    @* Iterate in CategoryOrder (§3.8) so rows match the filter sections; skip empty counts. *@
    @foreach (var category in RouteFilterViewModel.CategoryOrder)
    {
        var count = RouteFilterViewModel.ActiveCountsByCategory.GetValueOrDefault(category);
        @if (count > 0)
        {
            <div class="transit-running-label__row">
                <MatBody2 class="transit-running-label__icon" data-category="@category"></MatBody2>
                <MatBody2 class="transit-running-label__count">@count</MatBody2>
                <MatBody2 class="transit-running-label__text">@RunningNoun(category)</MatBody2>
            </div>
        }
    }
</div>

@code {
    // §3.12: prefer the per-category running-noun ("trains running"); fall back to the shared
    // template interpolated with the category label so a new category never renders blank.
    string RunningNoun(string category)
    {
        var noun = Loc[$"RunningNoun_{category}"];
        return noun.ResourceNotFound
            ? string.Format(Loc["VehiclesRunningTemplate"], Loc[category])
            : noun.Value;
    }
}
```

CSS: `.transit-running-label__icon[data-category="rail"] { background-color: #1a237e; }`
etc. — same attribute-selector approach as §3.10, plus a neutral default rule for
unstyled categories (§3.11).

### 4.8 `map-interop.js` — re-keyed, not expanded

```js
// was: ['match', ['get', 'transitMode'], 'Rail', 9, 6]   ← never matched (property is 'rail'), §3.13
// recommended: downcase-guard so a capitalized config key still matches (§3.13, §7)
'circle-radius':       ['match', ['downcase', ['get', 'category']], 'rail', 9, 6],
'circle-stroke-width': ['match', ['downcase', ['get', 'category']], 'rail', 2, 1],
```

The GeoJSON property is renamed `transitMode` → `category` for consistency with the new wire
field name (both `vehicle-animator.js:348,360,586` write sites and all four `map-interop.js`
read sites — lines 72/74 and the setStyle-restore duplicates at 167/169 — update together).
**Reminder (§3.13):** this makes the rail size/stroke tier fire for the first time (rail
dots grow) — a deliberate, visible change, not a no-op re-key. If `downcase` is omitted, the
match becomes case-sensitive and depends on the lowercase-key convention (§7).

---

## 5. Full Change Inventory

### 5.1 `Shared` project
- `Events/RouteNearestPointBatchEvent.cs` — remove `enum TransitMode`; retype the
  `[property: Key(10)] TransitMode TransitMode` field → `[Key(10)] string Category` (same
  slot; int→string wire encoding change, §4.3).
- `GtfsData/RouteShapeFeature.cs:16` — retype `RouteShapeProperties.Mode` → `Category`
  (string) **and add `int RouteType = 3`** (drives client display order, §3.8/§4.3).
- `JsonOptions.cs` — the `JsonStringEnumConverter` was only load-bearing for the (now
  removed) `Mode` enum on the *deserialize* leg. **Do not blindly delete it**: grep for any
  other enum that round-trips through `JsonOptions.Get()` first; if none, remove the
  converter and its comment, otherwise just drop the `Mode`-specific comment.

### 5.2 `WebAPI` project
- `GtfsStatic/GtfsStaticLoader.cs` — extend `CityStaticEntry` (line 28) with
  `RouteTypeCategories`; replace the 3-line switch at **line 326** with the config-driven
  `ClassifyCategory` (§4.2); retype the `ParseRouteMetadata` return tuple (lines 299-301,
  which currently carries `TransitMode Mode`) to carry `string Category` **and** the
  `int RouteType`; update the pre-init default (line 217, `var mode = TransitMode.Bus`) and
  the `BuildLineStringFeature`/`BuildZipRouteFeatures` signatures (lines 204-208, 457) to
  string; hand-emit `"category"` + `"routeType"` in `BuildLineStringFeature` (line 478,
  §4.2).
- `appsettings.json` — add `RouteTypeCategories` to the `ttc` entry (see §4.1). Note
  `appsettings.Development.json` has only **4** cities (marta/wmata/mbta/nymta — **no ttc**),
  so there's nothing to edit there for TTC; but if TTC is ever added to the dev file, its
  `RouteTypeCategories` block must be added alongside or dev silently falls back to the
  rail/bus rule (no local streetcar section).
- `WebAPI.Tests/GtfsStaticLoaderTests.cs` — **the `Dictionary<string,(…,TransitMode)>` tuple
  type appears in the test FIXTURES (`metaA`/`metaB`/`meta`), not just assertions** — retype
  every fixture tuple and its `TransitMode.Bus/.Rail` literals to the new
  `(…, string Category, int RouteType)` shape (~12 sites). Add TTC-shaped cases:
  `route_type=0 → "streetcar"`, `1 → "rail"`, `3 → "bus"` from a configured map, plus the
  unmapped-`route_type`-within-a-configured-city → `"bus"` + warning-log path (§3.5 Q5b).
- `WebAPI.Tests/EventEnvelopeMessagePackTests.cs` — the `Key(10)` positional round-trip
  (line 28 uses `TransitMode.Rail` as the 11th ctor arg) must become the `string` category,
  or MessagePack silently mis-deserializes.

### 5.3 `TransitDataWorker` project
- `Worker.cs` — retype `_routeMode`/`modeMap` **and all the sites that flow through it**:
  the field (lines 32-33), the `BuildRouteIndex` 4-tuple return type (lines 187, 193, 204,
  244), the `ProcessSpatialReconciliationAsync` parameter (line 339), and the two fallback
  sites (lines 415, 448) — `Dictionary<string, TransitMode>` → `Dictionary<string, string>`,
  fallbacks `TransitMode.Bus` → `"unknown"` per §3.6. (§2.2's line map is the complete set.)
- No `CityConfig`/`ITransitCity` changes (§3.3 — config stays WebAPI-only).

### 5.4 `Client.Shared` project
- `ViewModels/RouteFilterViewModel.cs` — full rewrite per §4.5: `RouteItem.Category`
  (string) + `RouteItem.RouteType` (int, for ordering); `_railVehicleIds` →
  `_vehicleCategory` dict; the two `[ObservableProperty]` count fields →
  `[ObservableProperty]`-backed `ActiveCountsByCategory` (reassigned, never mutated in
  place — §4.5 reactivity note); add `CategoryOrder` (built in `BuildRouteItems`, §3.8);
  `SelectAll`/`ClearSelection`/`HasSelectionFor` retyped to `string category`.
- `Components/RouteFilters.razor`, `.razor.cs` — `@if` pair → `@foreach` over
  `CategoryOrder` (§4.6); the `SectionLabel`/`RoutePill` fragments retype `TransitMode` →
  `string`; CSS class scheme → generic wrapper + `data-category` (§3.10).
- `Components/TransitRunningLabel.razor` — two rows → loop over `CategoryOrder` (§4.7);
  **broaden the `OnViewModelPropertyChanged` filter (line 104) from
  `nameof(ActiveBusCount) or nameof(ActiveRailCount)` to
  `nameof(ActiveCountsByCategory)`** — forgetting this is the most likely stale-count bug;
  add the `RunningNoun` helper (§4.7); migrate the inline `--rail`/`--bus` icon CSS (lines
  55-80) to `[data-category="…"]` selectors + a neutral default.
- `Resources/RouteFilterResources.resx` (+ any locale variants) — remove `Rail`/`Buses`/
  `NumTrainsRunning`/`NumBusesRunning`; add `rail`, `bus`, `streetcar` (labels),
  `RunningNoun_rail` = `"trains running"`, `RunningNoun_bus` = `"buses running"`,
  `RunningNoun_streetcar` = `"streetcars running"`, and `VehiclesRunningTemplate` =
  `"{0} running"` (fallback, §3.12). (Note: `SettingBusesVisible` in the settings resx is
  a *different* key for the settings blade — do not touch it.)
- `Components/RouteFilters.razor.css` — migrate `route-filters__rail`/`__buses` to the
  generic `route-filters__section` + `[data-category]` scheme (§3.10).

### 5.5 `Client.WebApp` project
- `Pages/TransitMap.razor.cs:498` — `r.TransitMode.ToString().ToLowerInvariant()` →
  `r.Category`. **Dropping `.ToLowerInvariant()` makes the JS boundary case-sensitive**
  (today it's not); safe only under the lowercase-key convention (§7). If the paint
  expression adopts `downcase` (§3.13), this is fully covered; otherwise it's a live
  dependency on config authors never capitalizing a key.

### 5.5b `Client.Shared` project — JS assets (both live under `Client.Shared/wwwroot/js/`, NOT `Client.WebApp`)
- `wwwroot/js/vehicle-animator.js:348,360,586` — GeoJSON `transitMode` property renamed
  `category`. Line 586's fallback `rec.transitMode || 'bus'` becomes
  `rec.category || 'unknown'` to match §3.6's join-failure category (was silently `'bus'`).
- `wwwroot/js/map-interop.js:72,74,167,169` — match expressions re-keyed `'transitMode'`→
  `'category'`, `'Rail'`→`'rail'` (or `['downcase', ['get', 'category']]`) per §4.8/§3.13.
  **This makes rail dots grow for the first time — a visible change, see §3.13.**

### 5.6 Wire migration strategy

Per §3.14: this is a breaking MessagePack change. Deploy server (WebAPI + Worker) and
client atomically in the same window, per this project's existing wire-contract discipline
for `RouteNearestPointBatchEvent` changes (see feature 040's payload-reduction rollout as
precedent). No dual-field transition period; no backward-compat shim.

---

## 6. Localization Note

New resx keys (`streetcar` label; `RunningNoun_rail`/`RunningNoun_bus`/`RunningNoun_streetcar`;
`VehiclesRunningTemplate`; and any future category labels/nouns) are EN-only for this change,
consistent with the project's existing pattern of deferring non-EN locales (`.es` deferred
per features 015/016). Existing `Rail`/`Buses` label keys are removed in favor of lowercase
category-key labels (`rail`, `bus`) — same displayed strings, different key casing so the
key equals the wire value. Existing `NumTrainsRunning`/`NumBusesRunning` become
`RunningNoun_rail`/`RunningNoun_bus` with **identical values** (`"trains running"` /
`"buses running"`), so the pre-change count copy is preserved verbatim (§3.12) rather than
regressing to a label-interpolated `"Rail running"`.

---

## 7. Open Items / Explicitly Deferred

- **Category key validation.** Nothing enforces that a `RouteTypeCategories` config value
  is lowercase, has no whitespace, etc. A config author typing `"Streetcar"` (capitalized)
  would produce a working-but-inconsistent category distinct from a hypothetical `"streetcar"`
  elsewhere. This now matters *more* than at first design, because dropping
  `.ToLowerInvariant()` at `TransitMap.razor.cs:498` (§5.5) makes the JS/map boundary
  **case-sensitive** — a capitalized key would break the rail-dot size match. Two cheap
  mitigations are recommended (neither scoped as blocking): (a) the paint expression matches
  `['downcase', ['get', 'category']]` (§3.13), and/or (b) `ClassifyCategory` lowercases its
  return (`category.ToLowerInvariant()`) so the wire value is normalized regardless of config
  casing. Full validation (whitespace, CSS-safety) still deferred; acceptable given config is
  authored by the same people who own `appsettings.json`, not end users.
- **Per-category audio/voicing** (§3.7) — tracked as a separate future feature, same as
  spec 043 left it.
- **Unifying WebAPI's `CityStaticEntry` and Worker's `CityConfig` parsing** (§3.4) — not
  in scope; both parallel config-reading paths continue to exist post-change.
- **N-category map-dot *sizing*** (§3.13) — the map keeps a binary rail-sized-vs-not tier
  (no per-category dot size); giving every category its own dot size is deferred. Note this
  is distinct from the rail-tier *bug fix* also in §3.13, which IS shipping (the re-key makes
  the existing rail tier actually fire for the first time).

---

## 8. Testing Notes

- `GtfsStaticLoaderTests.cs`: retype every fixture tuple (`metaA`/`metaB`/`meta`) and its
  `TransitMode.Bus/.Rail` literals to the new `(…, string Category, int RouteType)` shape;
  add a TTC-shaped fixture asserting `route_type=0 → "streetcar"`, `route_type=1 → "rail"`,
  `route_type=3 → "bus"` from a configured `RouteTypeCategories` map, plus an
  unmapped-value-within-a-configured-city case asserting the `"bus"` + warning-log fallback
  (§3.5 Q5b). Assert `RouteType` is carried through (e.g. streetcar route → `RouteType == 0`).
- New Worker-side test: a route absent from `_routeCategory` entirely resolves to
  `"unknown"`, not `"bus"` (§3.6).
- New client-side test (if a `RouteFilterViewModel` test suite exists/is added): 3+ distinct
  categories produce 3+ non-empty `ActiveCountsByCategory` entries; **`CategoryOrder` sorts
  by `min(RouteType)` ascending** — a `{streetcar:0, rail:1, bus:3}` catalog yields
  `[streetcar, rail, bus]`, and a MARTA-shaped `{rail:1, bus:3}` yields `[rail, bus]` (Rail
  still first, no regression, §3.8). Also cover: two `route_type`s mapping to one category
  collapse to one section keyed by the lower `RouteType`.
- New client-side reactivity test: after `RecomputeActiveTransitCounts` reassigns
  `ActiveCountsByCategory`, `PropertyChanged` fires for `nameof(ActiveCountsByCategory)` —
  guards the §4.5 stale-count trap.
- Running-noun fallback test (or manual): a category **with** a `RunningNoun_{cat}` entry
  renders it verbatim (`rail` → "trains running"); a category **without** one renders the
  template (`streetcar` with no noun key → "N Streetcar running"), never blank (§3.12).
- Manual verification: TTC in a real browser session — streetcar routes appear as their own
  filter section **first** (`route_type=0` sorts ahead of rail=1 and bus=3, §3.8), their own
  running-count row, and — because the rail-tier paint match now actually fires (§3.13) —
  **rail dots render larger than bus/streetcar dots** (a visible change from today, where the
  capital-`'Rail'` mismatch left every dot the same size). Confirm streetcar dots are
  bus-sized (binary tier, §3.13 deferred sizing).
