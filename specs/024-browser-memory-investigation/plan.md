# Implementation Plan: Browser Memory Footprint Reduction

**Branch**: `024-browser-memory-investigation` | **Date**: 2026-06-21 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/024-browser-memory-investigation/spec.md`

## Summary

Attribute and reduce the TransitJazz Blazor WASM client's flat ~1.2 GB browser-memory footprint. The prior heap-snapshot investigation (`docs/BROWSER_MEMORY_INVESTIGATION_DESIGN_DOCUMENT.md`) ruled out the early suspects and concluded the bulk is the .NET WASM linear-memory high-water mark, which the conservative Mono/WASM GC reserves and never returns. The work is four independently-shippable slices, all frontend-only:

1. **Attribution (P1)** — Promote the existing diagnostic `memory-probe.js` into a usable, supported measurement and enable the cross-origin-isolated context it needs so `performance.measureUserAgentSpecificMemory()` returns the WASM-vs-WebGL/canvas split. This resolves the one open question (which heap owns the 1.2 GB).
2. **Route-data de-duplication (P2)** — The full MARTA route geometry is resident 3–4× simultaneously (`_routeShapeCache` on .NET, `ChefMapAnimator.routeGeometry` coords + `cumDist`, and MapLibre's `routes` source + `_routesFeatureCollection`). Remove at least one full redundant copy without breaking route render or the basemap-toggle re-render that the `_routeShapeCache` exists to support.
3. **Production logging quiet-down (P2)** — `appsettings.json` sets `Logging.LogLevel.Default = "Debug"` and `Program.cs:87` hard-codes `SetMinimumLevel(LogLevel.Debug)`; the JS `_log`/`console.*` calls fire per-batch and per-frame and the console retains references. Set production default to `Information`, drive the .NET minimum level from configuration, and gate the JS hot-path logging behind a runtime debug flag (default off).
4. **Churn reduction (P3)** — `tick()` rebuilds a fresh `features` array + `FeatureCollection` and calls `setData` every RAF even when nothing visible moved; `HandleVehicleBatchAsync` makes a double `.ToArray()` pass; `_pendingBatches` is unbounded if the map never readies. Skip unchanged `setData`, collapse the double pass, and bound the pending buffer.

Reduction is judged by lowered, still-flat resident RSS — measured via the P1 probe — not by the JS heap snapshot alone (which never shows WASM reservation or GPU/VRAM).

## Technical Context

**Language/Version**: C# / .NET 10.0; JavaScript (ES, no build step — plain `wwwroot/js` modules + globals)
**Primary Dependencies**: Blazor WebAssembly, MapLibre GL JS v4 (CDN), Tone.js, MatBlazor; `performance.measureUserAgentSpecificMemory()` (Chromium, cross-origin-isolated only)
**Storage**: N/A (client-only; Blazored.LocalStorage for settings, untouched here)
**Testing**: Manual verification on the running app (production + dev) using the in-app memory probe; before/after RSS comparison over a 30–60 min live session. No automated test project exists for the WASM client; behavior parity is judged by a maintainer.
**Target Platform**: Browser (Chromium primary for the detailed breakdown; graceful fallback elsewhere), desktop + mobile
**Project Type**: Web — Blazor WASM frontend (this feature touches **only** `src/Client/`)
**Performance Goals**: Maintain 60 fps vehicle animation; no visible change to render/audio/basemap behavior (FR-012)
**Constraints**: Footprint must stay **flat** over a sustained session (FR-013, no reintroduced unbounded growth); detailed memory breakdown requires COOP:same-origin + COEP:require-corp (cross-origin isolation), which must not break the MapLibre CDN tile/script loads
**Scale/Scope**: ~186 nearest-point records per ~10 s SignalR batch; full MARTA allowed-route set (`IsAllowedRoute` currently returns true for all); flat baseline ~1.2 GB RSS today

### Open clarifications (carried to research.md, none blocking)

- Exact numeric reduction target for SC-003 is left as "measurably lower than ~1.2 GB"; the P1 probe must run first to set a defensible target. **Resolved in research.md**: target is set after attribution; plan does not hard-code a number.
- Whether cross-origin isolation (COOP/COEP) can be enabled on the Azure Static Web App without breaking the MapLibre CDN. **Resolved in research.md**: the MapLibre script/CSS load same-origin-isolated only if the CDN serves CORP/CORS headers; mitigation is to vendor MapLibre locally OR scope COOP/COEP to a diagnostic build/header set rather than production. See research.md decision.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Relevance | Verdict |
|-----------|-----------|---------|
| **II. No Frontend Secrets** | No new credentials introduced. MapTiler key handling unchanged. | ✅ PASS |
| **IV. OpenTelemetry Observability** | US3 lowers the production **default** log level to `Information` and gates verbose JS hot-path `console.*` behind a debug flag. Structured .NET logging remains; only Debug-severity hot-path noise is suppressed. Warnings/errors still flow (FR-008). Per the constitution's Localization rule, "Logging and developer-facing console messages are exempt." | ✅ PASS |
| **VII. OSM Cartography — data layers persist across style swap** | US2 de-dups route geometry. The `_routeShapeCache` exists specifically so routes re-render after a basemap style swap (`RenderRoutesAsync`). FR-005 + Edge Case make preserving that re-render a hard requirement; the chosen de-dup must keep a single authoritative source able to re-add the `routes` layer after `setStyle`. | ✅ PASS (guarded by FR-005) |
| **VIII. Generative Transit Music** | Audio untouched; `TransitSynth.PreloadAsync(_routeShapeCache.Keys)` must still receive route keys after de-dup. | ✅ PASS (preserve key source) |
| **IX. Persistent Multi-Selection** | Selection/filter state untouched; churn-skip in `tick()` must not skip a frame where selection emphasis changed. | ✅ PASS (skip only when truly nothing visible changed) |
| **XI. Snappy, Reversible Overlays** | No overlay timing changes. | ✅ PASS |
| **XII. Internationalized, Settings-Driven Presentation** | The memory probe is a **diagnostic** (console/dev-facing), not a user-facing settings control, so it introduces no user copy and needs no resx string. If any reduction work surfaces user-visible text, it MUST go through `RouteFilterResources.resx` (single-file rule). The debug flag is a dev toggle, not a settings-panel control. | ✅ PASS |
| **Single resx / No inline copy** | No new user-facing strings planned. | ✅ PASS |
| **I, III, V, VI** | Server/Worker/CI/GTFS-mapping untouched (frontend-only feature). | ✅ N/A |

**Result: PASS — no violations, Complexity Tracking not required.**

## Project Structure

### Documentation (this feature)

```text
specs/024-browser-memory-investigation/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── memory-probe.md       # window.MemoryProbe surface + measureUA contract
│   ├── debug-flag.md         # runtime debug-logging flag contract (JS + .NET)
│   └── route-geometry-dedup.md  # which copy is dropped + the preserved re-render contract
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit-specify)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root) — files this feature touches

```text
src/Client/
├── ChefKnifeStudios.MartaJazz.Client.WebApp/
│   ├── wwwroot/
│   │   ├── index.html                      # US1: COOP/COEP note + probe wiring; US3: window.__MJ_DEBUG bootstrap
│   │   └── appsettings.json                # US3: Logging.LogLevel.Default Debug → Information
│   ├── Program.cs                          # US3: drive min level from config, not hard-coded Debug
│   └── Pages/
│       └── TransitMap.razor.cs             # US2: route-cache de-dup; US4: collapse double ToArray, bound _pendingBatches
└── ChefKnifeStudios.MartaJazz.Client.Shared/
    └── wwwroot/js/
        ├── memory-probe.js                 # US1: harden measureUA / report as the supported attribution path
        ├── vehicle-animator.js             # US3: gate _log behind debug flag; US4: skip unchanged setData
        ├── map-interop.js                  # US2: route source as the single retained GeoJSON copy (if cache dropped)
        └── transit-synth.js                # US3: gate console.* behind debug flag
```

**Structure Decision**: Frontend-only change set under `src/Client/` across the WebApp project (config, Program.cs, TransitMap page) and the Shared RCL's `wwwroot/js` interop modules. No `Shared/`, `Server/`, `Worker/`, or `contracts`-level changes. This matches the spec's frontend-only assumption and the constitution's three-unit architecture (only the Blazor WASM unit is modified).

## Complexity Tracking

> No constitution violations — section intentionally empty.
