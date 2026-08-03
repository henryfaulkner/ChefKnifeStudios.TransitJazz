# Phase 0 Research: City Slug Migration

All findings below were verified against the code on branch `052-city-slug-migration`,
not inherited from the source assessment. Three of them **contradict** that document; those
are called out explicitly because they change the plan.

---

## R1. The telemetry identifier is coupled to the property being renamed (BLOCKING)

**Decision**: Decouple the telemetry city value from `ITransitCity.Name` by introducing a
separate, explicitly-agency-valued member before renaming anything.

**Finding**: `Worker.cs:103` writes `city_name = result.CityName`, and `CityTickResult.CityName`
is populated from `city.Name` (`Worker.cs:86`, `:92`) — i.e. `ITransitCity.Name`, which is
`CityNames.Marta` etc. (`MartaCity.cs:20`).

**Why this matters**: FR-016 requires telemetry to stay on agency values. But renaming
`CityNames.Marta` from `"marta"` to `"atlanta"` **automatically rewrites `city_name`** — the
exact history split FR-016/FR-017 exist to prevent, and the 051 Phase 3 baseline collision
(FR-023). "Leave telemetry alone" is therefore **not a no-op**; it requires positive work.

The source assessment treats leaving `city_name` untouched as the *cheap* option (§6, §7).
It is cheaper than dual-writing, but it is not free, and nothing in §1–§7 identifies this
coupling.

**Additional finding — value casing**: telemetry `city_name` values are **uppercase agency
names** (`'MARTA'`), per `TelemetryEventSchemaTests.cs:27,97` and
`validate_test.go:23,106` — not the lowercase slugs used everywhere else. So `city_name`
already diverges from the slug in casing today; this feature widens the divergence to the
whole token. The mapping must be documented (FR-018).

**Approach**: add a distinct `TelemetryName` (agency-valued, e.g. `"MARTA"`) to the city
abstraction, and have `Worker.cs` write *that* instead of `Name`. `Name` is then free to
become the city slug without touching a single parquet value.

**Alternatives considered**:
- *Rename `CityNames` constants but leave their string values* — defeats the feature.
- *Map slug→agency at the telemetry write site* — a switch in `Worker.cs` duplicating city
  identity outside the registry; violates FR-005.
- *Accept the rewrite and dual-write* — explicitly rejected by the user's decision.

---

## R2. The `JoinCityV2` precedent claimed by the assessment does not exist

**Decision**: Treat the version gate (FR-009) as **new work**, and rename the hub method as
part of this feature.

**Finding**: `HubMethods` declares only `JoinCity = "JoinCity"` (`CityNames.cs:18`);
`TransitHub.JoinCity` (`TransitHub.cs:21`) is the sole join method. No `V2` variant exists
anywhere in `src/`. There is also **no `LeaveCity` method at all**, despite §1 of the
assessment listing `TransitHub.JoinCityV2 / LeaveCity` as a boundary.

**Impact**: §2b's argument — "051 already established the precedent and solved it by renaming
`JoinCity` → `JoinCityV2`" — is false. Planning must not assume prior art. The rename to
`JoinCityV2` is introduced *here*, for the first time.

**How the gate works**: an old client invokes `"JoinCity"`; the updated hub no longer defines
it, so SignalR fails the invocation and the client surfaces an error — loud, per FR-009/FR-010
— rather than joining a group nobody publishes to and rendering an empty map.

---

## R3. Group publish/join symmetry is the only silent-failure path

**Decision**: Verify group symmetry by observing vehicle arrival, not by absence of errors.

**Finding**: `TransitHub.JoinCity` calls `Groups.AddToGroupAsync(ConnectionId, city)` with the
raw string, unvalidated. The worker publishes to the group named by its config `Cities[].Name`.
Nothing cross-checks the two. A one-sided rename yields a connected client, no exception, no
log line, and an empty map — matching FR-008 and SC-003.

**Mitigation**: the config-parity check (FR-006) catches the two-`appsettings.json` case
statically; the version gate (FR-009) catches the stale-client case loudly. The remaining
case — config and code renamed inconsistently *within* one lane — is covered by the
per-city smoke test (FR-022).

---

## R4. Slug literals are hardcoded in three places outside the registry

**Decision**: Route every slug literal through `CityNames`; add a guard test.

**Findings**:
- `CityFab.razor:48–81` — seven `location.hash='marta'` string literals inside `eval` calls,
  independent of the `CityNames.*` constants used for the `Disabled` checks on the same
  component (lines 17–35). A constant-only rename leaves these pointing at dead slugs.
- Both `appsettings.json` files — `Cities[].Name` ×7 each
  (Worker `:4,14,28,34,59,65,71`; WebAPI `:34,44,71,77,102,109,115`).
- Test fixtures — `city_name = "MARTA"` literals in `ChannelLoadSheddingTests.cs:35,83` and
  `TelemetryEventSchemaTests.cs:27,97`. Under R1 these are **correct as-is** and must NOT be
  updated; they assert the agency value that FR-016 preserves.

**Rationale**: this is §1's "central risk" — a constant-only rename compiles clean and breaks
at runtime — and it is accurate.

---

## R5. Copy keys number 30, not ~40

**Decision**: Rename 30 resx keys; leave the default city's unprefixed keys alone.

**Finding**: `RouteFilterResources.resx` contains exactly 30 agency-prefixed keys — six cities
(Wmata, Mbta, Nymta, Ttc, Septa, Rtd) × five keys (`*AudioOverlayHeader`,
`*AudioOverlayParagraph1–3`, `*OverlayParagraph1`). MARTA has **no** prefixed keys; Atlanta is
the switch-expression default arm (`AudioUnlockOverlay.razor:263–268`, `InfoFab.razor:48–53`).

**Impact**: §7's "~40 keys" overestimates. Also note the prefixes are PascalCase agency names
(`WmataAudioOverlay`), so renaming them to city names (`WashingtonDcAudioOverlay`) is a
cosmetic change to an internal key, invisible to users. **Recommendation: rename them** for
consistency with FR-015's intent, but note this is the lowest-risk item in the feature and
could be dropped without any user-visible effect.

---

## R6. New York carries the only bespoke identity wiring

**Decision**: Treat NYMTA as a distinct work item; the other six are uniform.

**Findings** — `CityNames.Nymta` appears in load-bearing positions no other city has:
- `WebAPI/Program.cs:71,82,103,107` — DI branch + explicit `Name =` assignment
- `TransitDataWorker/Program.cs:29,40,62,66` — same pair
- `GtfsStaticLoader.cs:197` — `city.Name == CityNames.Nymta && zipUrl.Contains("/subway/")`
- `NymtaCity.cs` — its own `Name` property
- a self-referential internal call passing `city={CityNames.Nymta}`

`MartaCity` also appears in DI branches (`Program.cs:103`, `:62`) and as the
`GtfsStaticLoader` fallback (`:126`, `:152`). Because all of these compare against
`CityNames.*` constants rather than literals, they follow the constant rename automatically —
**provided** R1's telemetry decoupling lands first.

---

## R7. Non-durable state and absent state confirmed

**Decision**: No data migration; no backfill.

**Findings**: the WebAPI route-shape store is `IKeyValueRepository<string>` (in-memory), keyed
`{city}:{route_id}` and rebuilt at startup by `GtfsStaticLoader` — a deploy re-derives every
key, so the old prefix cannot survive a restart. No database exists in the city path. Both
mitigating claims in §2 are accurate.

---

## R8. Analytics pageview follows the rename automatically

**Decision**: No code change required; verify the emitted path after cutover.

**Findings**:
- `TransitMap.razor.cs:149` — `await JS.InvokeVoidAsync("trackCityView", NavigationManager.ResolveCity())`
- `wwwroot/js/umami-city.js:22` — `window.trackCityView = function (city) { ... }`

The call site passes `ResolveCity()` straight through, so once the fragment carries the new
slug the analytics path changes with it — no literal to edit. The consequence is a **reporting
discontinuity**: Umami will show `/marta` before cutover and `/atlanta` after, as two separate
paths. This is expected and does not affect FR-008; it is recorded here so it is not mistaken
for a traffic drop.

---

## Summary of source-document corrections

| Assessment claim | Reality |
|---|---|
| §2b: 051 renamed `JoinCity`→`JoinCityV2`, precedent exists | **False** — only `JoinCity` exists; the gate is new work (R2) |
| §1: `TransitHub.JoinCityV2 / LeaveCity` are boundaries | **False** — no `LeaveCity` method exists (R2) |
| §6/§7: leaving `city_name` alone is the cheap/default path | **Incomplete** — it is coupled to `ITransitCity.Name` and requires positive decoupling work (R1) |
| §7: ~40 resx keys | **30** (R5) |
| §2: route-shape keys non-durable; no database | **Accurate** (R7) |
| §1: `trackCityView` analytics boundary | **Accurate** — exists, and needs no edit (R8) |
