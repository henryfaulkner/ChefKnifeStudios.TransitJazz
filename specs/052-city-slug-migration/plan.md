# Implementation Plan: City Slug Migration

**Branch**: `052-city-slug-migration` | **Date**: 2026-08-02 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/052-city-slug-migration/spec.md`

## Summary

Rename the city identity token from transit-agency names (`marta`, `wmata`, …) to city names
(`atlanta`, `washington-dc`, …) across every boundary that carries it: URL fragment, SignalR
group, `?city=` parameter, `Cities[].Name` config key, route-shape store prefix, and Umami
pageview path.

The mechanical edit is small. Two things make it non-trivial, and research found both:

1. **Telemetry is coupled to the property being renamed.** `Worker.cs:103` writes
   `city_name = result.CityName`, sourced from `ITransitCity.Name`. Renaming `Name` would
   silently rewrite `city_name`, splitting parquet history and destroying the 051 Phase 3
   baseline — precisely what FR-016/FR-023 forbid. The plan therefore **splits the property**:
   `Name` (slug, all live boundaries) and a new `TelemetryName` (agency, frozen, parquet only).
   "Leave telemetry alone" is positive work, not a no-op.

2. **The SignalR group is an unversioned wire contract whose failure mode is silent.** A
   join/publish mismatch yields a connected client receiving nothing, with no error on either
   side. Mitigated by renaming `JoinCity` → `JoinCityV2` so stale clients fail loudly. Contrary
   to the source assessment, **no `V2` precedent exists** — this is new work.

Legacy slug aliasing was explicitly declined; old bookmarked links will silently land in the
default city. Telemetry stays on agency values.

## Technical Context

**Language/Version**: C# / .NET 10.0; JavaScript (ES modules); Go 1.x (telemetry tooling, untouched)
**Primary Dependencies**: Blazor WebAssembly, MatBlazor, SignalR, MapLibre GL JS, Parquet.Net, xUnit
**Storage**: In-memory `IKeyValueRepository<string>` (route shapes, rebuilt at startup); Azure Blob parquet (telemetry, append-only, immutable)
**Testing**: xUnit across 4 test projects — `Shared.Tests`, `Client.Shared.Tests`, `Server.WebAPI.Tests`, `Server.TransitDataWorker.Tests`
**Target Platform**: Blazor WASM (Azure Static Web Apps) + ASP.NET Core WebAPI + Worker container (ACR)
**Project Type**: Web application — WASM frontend, WebAPI, background worker
**Performance Goals**: Unchanged. This feature must be performance-neutral (SC-009)
**Constraints**: 3 independent deploy lanes (server+worker atomic, client separate, `deploy/marta-jazz` identical); no window where a city silently receives no data; must not start before the 051 Phase 3 baseline window closes (FR-023)
**Scale/Scope**: 7 cities; ~35–40 files; 30 resx keys; 14 config entries; 7 hardcoded JS literals

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applies | Assessment |
|---|---|---|
| I. Decoupled Cloud Architecture | Yes | Unchanged. Same three units; only an identifier value moves. **PASS** |
| II. No Frontend Secrets | No | No credentials touched. **N/A** |
| III. Two-Pass Pipeline | Yes | Neither pass altered; city scoping is by the same key with a new value. **PASS** |
| IV. OpenTelemetry Observability | Yes | Strengthened — FR-010 requires join failures be operator-visible. Telemetry schema unchanged (FR-019). **PASS** |
| V. GitHub Actions CI/CD | Yes | Adds a config-parity check (FR-006) to the existing test run. Same two artifacts. **PASS** |
| VI. GTFS ID Mapping (`RouteJoinKey`) | Yes | Untouched. `RouteJoinKey` is route identity; this feature changes only **city** identity. `_routeIndex` stays keyed by city→`RouteJoinKey`; only the outer key's value changes. `RailRouteIdMap` unaffected. **PASS** |
| VII. OSM Cartography | Yes | No basemap or layer change. Map origins preserved verbatim (FR-014). **PASS** |
| VIII. Generative Transit Music | Yes | Tone determinism hashes `RouteJoinKey + segmentIndex` — **not** the city slug — so audio is bit-identical after the rename. Verified: no city slug feeds any tone hash. **PASS** |
| IX. Persistent Multi-Selection | No | Filtering untouched. **N/A** |
| X. Zoom-Adaptive Controls | No | No control layout change. **N/A** |
| XI. Snappy, Reversible Overlays | Yes | Overlay copy re-keyed, timing untouched. **PASS** |
| XII. Internationalized Presentation | Yes | 30 resx keys renamed **in place** in the single `RouteFilterResources.resx`; no new resource file, no inline copy introduced. **PASS** |
| XIII. Dark-Mode Parity | No | No color-bearing CSS added. **N/A** |

**Result: PASS** — no violations, no Complexity Tracking entries required.

Two notes for reviewers:
- Principle VIII's determinism is the reason SC-009 ("zero behavior changes") is verifiable
  rather than aspirational: the tone hash never sees the city token.
- Principle XII mandates Spanish support. `.es` resources remain deferred (consistent with
  015/016/017); this feature neither advances nor regresses that gap, and adds no
  non-localized copy.

**Post-Phase-1 re-check**: still PASS. The `TelemetryName` split (data-model E3) adds one
property to an existing abstraction — no new project, no new pattern, no new dependency. It
*serves* Principle IV by keeping the observability history continuous.

## Project Structure

### Documentation (this feature)

```text
specs/052-city-slug-migration/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 — 8 findings, 4 corrections to the source assessment
├── data-model.md        # Phase 1 — entities + the Name/TelemetryName split
├── quickstart.md        # Phase 1 — cutover + per-city verification
├── contracts/
│   ├── city-identity.md    # Slug rule, the 7 values, single-source-of-truth, telemetry split
│   └── signalr-cutover.md  # Version gate, deploy order, silent-failure verification
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── ChefKnifeStudios.TransitJazz.Shared/
│   └── CityNames.cs                          # 7 constant VALUES + HubMethods.JoinCityV2
│
├── Client/
│   ├── ChefKnifeStudios.TransitJazz.Client.Core/
│   │   └── Services/
│   │       ├── NavigationManagerExtensions.cs   # ResolveCity() fallback -> new slug
│   │       └── SignalRNotificationService.cs    # :101,:105 invoke JoinCityV2
│   ├── ChefKnifeStudios.TransitJazz.Client.Shared/
│   │   ├── Components/AudioUnlockOverlay.razor  # :263-268 switch arms -> new resx prefixes
│   │   ├── Components/FABs/InfoFab.razor        # :48-53 switch arms
│   │   ├── Components/FABs/CityFab.razor        # :48-81 SEVEN hardcoded location.hash literals
│   │   └── Resources/RouteFilterResources.resx  # 30 agency-prefixed keys
│   └── ChefKnifeStudios.TransitJazz.Client.WebApp/
│       └── Pages/TransitMap.razor.cs            # :82-88 map origins, :105 default hash
│
├── Server/
│   ├── ChefKnifeStudios.TransitJazz.Server.WebAPI/
│   │   ├── SignalR/TransitHub.cs                # JoinCity -> JoinCityV2
│   │   ├── Program.cs                           # :71,82,103,107 DI branches
│   │   ├── GtfsStatic/GtfsStaticLoader.cs       # :126,152 fallback; :197 NYMTA subway
│   │   └── appsettings.json                     # :34,44,71,77,102,109,115 Cities[].Name
│   └── ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
│       ├── Program.cs                           # :29,40,62,66 DI branches
│       ├── Worker.cs                            # :103 city_name -> TelemetryName  [CRITICAL]
│       ├── Cities/                              # ITransitCity + MartaCity/NymtaCity: add TelemetryName
│       └── appsettings.json                     # :4,14,28,34,59,65,71 Cities[].Name
│
└── [4 test projects — see Testing Strategy]

.claude/skills/
├── add-transit-city/SKILL.md                    # document the slug rule
└── discover-transit-city/SKILL.md               # FR-003: autonomous minting follows the rule
```

**Structure Decision**: Existing 11-project solution, unchanged. No new projects, files, or
abstractions — except one property (`TelemetryName`) on the existing city abstraction. The work
is concentrated in `CityNames.cs` (the value change) plus the boundaries that hardcode slugs
independently of it.

## Testing Strategy

Built on the `util-testing` standard: classify by layer, **push coverage to the lowest layer
that can prove the behaviour**, assert observable behaviour over implementation, and treat
flakiness as a defect.

### Risk-to-layer mapping

The dominant risk is not "does the code compile" — a constant-only rename compiles clean and
fails at runtime. It is **boundary disagreement**. Layer choice follows directly:

| Risk | Lowest layer that can prove it | Why not lower |
|---|---|---|
| Slug values malformed / non-conforming | **Unit** | Pure string rule |
| Telemetry value changed by the rename | **Unit** | Property read, fully isolatable |
| Two `appsettings.json` disagree | **Component** (file-reading test) | Needs the real files; no external deps |
| Copy key missing after re-prefixing | **Component** (resx resolution) | Needs the real resource assembly |
| Hardcoded literal left behind | **Component** (source-scanning guard) | Needs the real source tree |
| Join/publish group mismatch | **Contract** (hub) | Crosses the SignalR boundary |
| A city silently receives nothing | **Manual smoke** | Requires live upstream feeds — genuinely uncontrolled |

### Tier 0 — Unit (every commit)

Project: `ChefKnifeStudios.TransitJazz.Shared.Tests`

| Test | Asserts | Type |
|---|---|---|
| `every_city_slug_conforms_to_format_rule` | All 7 values match `^[a-z0-9]+(-[a-z0-9]+)*$` | Boundary/negative |
| `city_slugs_are_unique` | No duplicate values in the registry | Invariant |
| `city_slugs_contain_no_agency_names` | No value is `marta`/`wmata`/`mbta`/`nymta`/`ttc`/`septa`/`rtd` — catches a half-finished rename | Negative |
| `city_slugs_survive_uri_fragment_round_trip` | `Uri.EscapeDataString` → unescape → lowercase is identity (guards `washington-dc`, `new-york-city`) | Boundary |
| `join_hub_method_is_versioned` | `HubMethods.JoinCity` value is `"JoinCityV2"` | Contract-guard |

Project: `ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests`

| Test | Asserts | Type |
|---|---|---|
| `telemetry_name_is_agency_valued_not_slug` | Each city's `TelemetryName` equals its frozen agency string and is **not** its `Name` | **The FR-016 guard** |
| `telemetry_name_values_are_frozen` | Exact expected set (`MARTA`, `WMATA`, …) — fails if anyone "tidies" them to slugs | Regression |
| `per_city_cycle_writes_telemetry_name_as_city_name` | A tick over a fake city emits `city_name == TelemetryName`, not `Name` | **Behaviour, not implementation** |

`ResolveCity()` unit tests (`Client.Shared.Tests`): returns the fragment lowercased; returns the
default slug for empty/whitespace/malformed input; `#ATLANTA` and `#Atlanta` both resolve
`atlanta`. Uses a stubbed `NavigationManager` — no browser.

**Existing tests that MUST NOT change**: `TelemetryEventSchemaTests.cs:27,97` and
`ChannelLoadSheddingTests.cs:35,83` assert `city_name = "MARTA"`. Under the split these remain
correct. **Editing them to a city slug would silently defeat FR-016** — call this out in review.

### Tier 1 — Component (CI smoke)

| Test | Asserts | Notes |
|---|---|---|
| `city_config_names_match_across_both_appsettings` | The **set** of `Cities[].Name` in Worker and WebAPI `appsettings.json` is identical | **SC-007.** Compares sets, not whole files — the files legitimately differ elsewhere. Reads the real files as embedded/copied content |
| `every_registry_slug_has_a_config_entry` | No city in `CityNames` lacks a config entry, and vice versa | Catches a registry/config drift the parity test alone would miss |
| `every_city_resolves_audio_overlay_copy` | For all 7 slugs, the `AudioUnlockOverlay` key resolves to a non-empty string ≠ the key name | **SC-005**; catches a half-renamed prefix |
| `every_city_resolves_info_overlay_copy` | Same for `InfoFab` | SC-005 |
| `no_agency_slug_literal_remains_in_client_source` | Scans `CityFab.razor` for `location.hash='<agency>'` | Guards contract C3 — the §1 "central risk" |
| `every_city_has_a_map_origin` | All 7 slugs present in `_cityCenter`, coordinates **unchanged** from pre-migration values | **FR-014** — pins origins against accidental edits |

### Tier 2 — Contract (CI, by trigger)

Project: `ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests` (extends existing `WorkerTransitHubTests`)

| Test | Asserts |
|---|---|
| `join_city_v2_adds_connection_to_group_named_by_slug` | Group name == the slug passed, verbatim — the publish/join symmetry that fails silently in production |
| `legacy_join_city_method_is_absent` | No `JoinCity` method on the hub — proves the shim contract C2 forbids was not reintroduced |
| `join_city_v2_replays_cached_batch_for_that_city` | Existing replay behaviour preserved under the new name |

Doubles: stub `ILastBatchCache`, spy on `IGroupManager` to capture the group name — verifying an
interaction at a real boundary, which is what a spy is for.

### Tier 3 — Manual smoke (cutover gate)

Automated tests cannot prove FR-008/SC-001 — vehicles arriving requires live upstream feeds,
an uncontrolled external dependency. Per `quickstart.md`, for **each of the 7 cities**: vehicles
appear and move, count is non-zero, a crossing produces audio, shapes render, join is logged
with the expected group name.

**Rule: "no errors" is not evidence.** The silent failure looks exactly like a healthy log.
Only observed vehicle arrival closes SC-003.

### Reliability

Per `util-testing`'s flakiness rubric, these tests are deterministic by construction: no clock
dependency (no `DateTime.UtcNow` in assertions), no shared mutable state (each reads immutable
constants or its own file handle), no ordering sensitivity (set comparisons, not sequence), no
concurrency, no randomness, no network. Any intermittency here indicates a real defect — do not
add retries.

### Coverage adequacy

| Category | Covered by |
|---|---|
| Happy path | Slug format, config parity, copy resolution, group join |
| Negative / invalid | `city_slugs_contain_no_agency_names`, malformed-fragment `ResolveCity`, `legacy_join_city_method_is_absent` |
| Boundary | Empty/whitespace fragment, casing, URI round-trip of hyphenated slugs |
| Error paths | Unknown-fragment fallback; join failure against a stale client |
| Concurrency | N/A — no concurrent behaviour introduced |
| Time-sensitive | N/A — no clock/schedule/duration behaviour introduced |

### Explicit non-goals

- No E2E automation across 7 live cities — high cost, low reliability, depends on third-party
  feeds. Manual smoke is the deliberate trade.
- No performance testing — SC-009 asserts neutrality; nothing on a hot path changes.
- No new test project. All work extends the 4 existing ones.

## Implementation Sequence

Ordering is load-bearing. Step 1 must precede step 2 or telemetry is silently rewritten.

1. **Split identity from telemetry** — add `TelemetryName` to the city abstraction and its
   implementations; point `Worker.cs:103` at it. **Values unchanged; behaviour unchanged.**
   Land the telemetry guard tests here, green, *before* any slug moves.
2. **Change the 7 constant values** in `CityNames.cs`, plus `HubMethods.JoinCity` →
   `JoinCityV2`. The DI branches, `GtfsStaticLoader` NYMTA special case, map-origin dictionary,
   and switch arms all reference constants and follow automatically.
3. **Fix the hardcoded literals** — `CityFab.razor` ×7, both `appsettings.json` ×7 each.
4. **Rename hub method + client invocation** — `TransitHub`, `SignalRNotificationService:101,105`.
5. **Re-prefix the 30 resx keys** and both switch expressions in lockstep.
6. **Update the skills** — slug rule in `add-transit-city` and `discover-transit-city` (FR-003).
7. **Deploy** per `contracts/signalr-cutover.md` C5: server+worker → client → `deploy/marta-jazz`
   → verify all 7.

**Gate**: do not begin before the 051 Phase 3 `batch_wire_bytes` baseline window closes (FR-023).

## Complexity Tracking

> No Constitution Check violations. Table intentionally empty.

The one structural addition — `TelemetryName` — is not complexity for its own sake: it is the
minimum mechanism that satisfies FR-016 given the coupling found in research R1. The simpler
alternative (leave the property single and accept the rewrite) was rejected because it splits
parquet history at the cutover date and destroys the 051 Phase 3 baseline.
