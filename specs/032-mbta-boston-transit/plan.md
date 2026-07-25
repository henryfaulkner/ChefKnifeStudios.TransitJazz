# Implementation Plan: Add Boston (MBTA) as a Transit City

**Branch**: `031-multi-city-transit` (added on current branch per user request — no branch switch) | **Date**: 2026-06-28 | **Spec**: [spec.md](./spec.md)
**Compatibility source**: [`docs/city-compat/mbta.md`](../../docs/city-compat/mbta.md)
**Builds on**: feature 031 (multi-city transit) — the `ITransitCity` strategy, `Cities:` config array, per-city SignalR groups, and `{city}:{routeId}` keying are already merged.

## Summary

Add Boston / MBTA as a third city. MBTA is the **configuration-only** case that feature 031 was built to make free: a single public, keyless GTFS-RT feed (`VehiclePositions.pb`) carries every mode at once (bus, light rail, commuter rail, heavy rail), every vehicle already has a `route_id` and lat/lon, and the heavy-rail line IDs (`Red`/`Orange`/`Blue`) match the static `route_id` verbatim. The compat doc's only flagged caveat — "key the route index by `route_id`, not `route_short_name`" — is **already how this codebase keys** post-031 (`GtfsStaticLoader` stores `{city}:{routeId}`), so MBTA reaches 100% route alignment as-is with no remapping. Net delivery: **two config entries** (worker + WebAPI `Cities:` arrays) plus **two trivial source touches** to make Boston reachable and selectable (a `CityNames.Mbta` constant and one `CityFab` menu item). No new processing logic, no secret, no rail adapter, no `RailRouteIdMap`.

## Technical Context

**Language/Version**: C# / .NET 10.0; Blazor WASM client (Razor)
**Primary Dependencies**: ASP.NET Core (Minimal API + SignalR), protobuf-net (GTFS-RT), MatBlazor (city picker FAB), existing 031 multi-city machinery (`ITransitCity`, `GtfsRtCity`, `CityConfig`)
**Storage**: In-memory `IKeyValueRepository<string>` for route shapes (keyed `{city}:{routeId}`); in-memory per-city vehicle caches. No new storage.
**Testing**: xUnit (`Server.WebAPI.Tests`, `Server.TransitDataWorker.Tests`); plus manual end-to-end verification (quickstart.md) since the change is config + two lines.
**Target Platform**: Blazor WASM (Azure Static Web App) + ASP.NET Core WebAPI + .NET Worker (Azure Container App) — unchanged.
**Project Type**: Web application — decoupled frontend + backend + worker.
**Performance Goals**: One more city = one more sequential I/O-bound feed fetch (~37 KB protobuf, ~311 vehicles) per 10 s cycle. Negligible; the single worker process already handles MARTA + WMATA.
**Constraints**: No new deployed infrastructure (FR-011); no access key needed or added (FR-005, Principle II — MBTA feeds are public/keyless); MARTA and WMATA byte-identical after the add (FR-009); shared pipeline must not branch on `mbta` (FR-006 — it doesn't: `Program.cs` routes every non-`marta` config to `GtfsRtCity` automatically).
**Scale/Scope**: 3 cities at delivery (MARTA, WMATA, MBTA). Touches: worker `appsettings.json` (+ Development), WebAPI `appsettings.json` (+ Development), `CityNames.cs` (one const), `CityFab.razor` (one menu item). No new files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Impact | Status |
|---|---|---|
| **I. Decoupled Cloud Architecture** | Three deployable units unchanged; one worker process gains one more configured city. | ✅ PASS |
| **II. No Frontend Secrets** | MBTA needs **no** key (public CDN feeds). Client gains only a city string. Nothing committed. | ✅ PASS |
| **III. Two-Pass Pipeline** | V2 spatial reconciliation is per-city and parameterized (031); MBTA flows through it unmodified. | ✅ PASS |
| **IV. Observability** | Existing per-city try/catch logs `{City}` on failure; MBTA inherits it. No telemetry for MBTA (FR-010). | ✅ PASS |
| **V. CI/CD** | No new artifacts; same WASM + Docker image. | ✅ PASS |
| **VI. GTFS ID Mapping** | ⚠️ See note. MBTA aligns by `route_id` verbatim; no remapping. The codebase already keys by `{city}:{routeId}` (031), which is *broader* than the constitution's `route_short_name` text. | ✅ PASS (note below) |
| **VII. No re-fetch of static data** | Client fetches MBTA shapes once per the existing per-city flow. Unchanged. | ✅ PASS |
| **VIII. Generative Music** | Deterministic per-`routeId` tone assignment applies to MBTA routes as-is. | ✅ PASS |
| **IX. Persistent Multi-Selection** | Selection set scopes to MBTA's routes when Boston is the joined city. Unchanged. | ✅ PASS |
| **X / XI. Controls / Overlays** | Unchanged. The new picker entry reuses the existing `CityFab` pattern. | ✅ PASS |
| **XII. i18n** | City label "Boston, MA" follows the existing `CityFab` literal-label pattern (MARTA/WMATA labels are inline today); identifier `mbta` is a stable key, not display text. EN-only this pass, consistent with 015–017/031. | ✅ PASS (consistent with precedent) |

**Constitution VI/III note (pre-existing, not introduced here)**: Principles III and VI still describe the static↔RT join key as `route_short_name`. Feature 031 already moved the actual index to `{city}:{routeId}` (`GtfsStaticLoader.cs:137`), and that is precisely what makes MBTA align at 100%. This is pre-existing constitution-vs-code drift owned by 031, not a violation this feature creates. No amendment is attempted here. Flagging only.

**No violations.** Complexity Tracking omitted — nothing to justify. The feature adds zero abstractions; it consumes the 031 ones.

## Project Structure

### Documentation (this feature)

```text
specs/032-mbta-boston-transit/
├── plan.md              # This file
├── research.md          # Phase 0 — compat findings + the one "is it really config-only?" question
├── data-model.md        # Phase 1 — the MBTA city entry & how it maps to CityConfig
├── quickstart.md        # Phase 1 — exact edits + end-to-end verification
├── contracts/
│   └── mbta-city-config.md   # Phase 1 — the concrete config contract for both Cities: arrays
└── checklists/
    └── requirements.md  # (from /speckit-specify)
```

### Source Code (repository root)

```text
src/
├── ChefKnifeStudios.MartaJazz.Shared/
│   └── CityNames.cs                                  # ADD: const string Mbta = "mbta";
│
├── Server/
│   ├── ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/
│   │   ├── appsettings.json                          # ADD: mbta entry to Cities: array
│   │   └── appsettings.Development.json              # ADD: mbta entry (dev parity)
│   │   # Program.cs UNCHANGED — non-marta config auto-routes to GtfsRtCity (Program.cs:39-42)
│   │
│   └── ChefKnifeStudios.MartaJazz.Server.WebAPI/
│       ├── appsettings.json                          # ADD: mbta entry to Cities: array
│       └── appsettings.Development.json              # ADD: mbta entry (dev parity)
│       # GtfsStaticLoader.cs UNCHANGED — LoadCityEntries() iterates Cities: (loader L59-86)
│
└── Client/
    └── ChefKnifeStudios.MartaJazz.Client.Shared/
        └── Components/FABs/CityFab.razor            # ADD: one MatListItem + HandleMbtaClicked
```

**Structure Decision**: Web application (Principle I). **No new files, no new projects.** All four config files get one analogous array entry; two source files each get a few lines. This is the smallest possible diff that makes a city reachable — exactly the 031 "config-only city" promise cashing out.

## Implementation Order

1. **Config (both `Cities:` arrays)** — add the `mbta` entry to the worker and WebAPI `appsettings.json` + `appsettings.Development.json`. This alone makes MBTA's vehicles flow and its shapes load; verify the worker logs MBTA vehicles and the WebAPI logs "city mbta loaded N route shapes".
2. **`CityNames.Mbta`** — add the stable constant so the rest of the app keys on it consistently.
3. **`CityFab` entry** — add the "Boston, MA" menu item (mirrors `HandleWmataClicked`) so a viewer can select Boston. Verify selection navigates to `#mbta`, reloads, and shows Boston scoped (map, audio, route pills) with zero MARTA/WMATA bleed.

Steps 1–3 are independent enough to land together; the spec's P1 (view Boston) is covered by steps 1–2, and P2 (pick Boston) by step 3.
