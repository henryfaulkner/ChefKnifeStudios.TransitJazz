# Implementation Plan: Checkpoint Crossing Trail

**Branch**: `027-checkpoint-note-trail` | **Date**: 2026-06-23 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/027-checkpoint-note-trail/spec.md`

## Summary

When a bus crosses a checkpoint, draw a transient, route-colored line segment on the map that is anchored at the checkpoint (projected onto the route polyline) and grows its head forward along the route over the duration of the crossing note, then vanishes immediately when that duration elapses. Final length scales with the bus's current speed × note duration (clamped by `MIN_SPEED`/`MAX_LEN_M`); width matches the bus dot (12px). The trail fires on every crossing while **checkpoint pulse visibility is ON**, independent of audio mute/lock state (resolved clarification).

**Technical approach**: Introduce a new RCL ES module `checkpoint-trail.js` that is a direct structural sibling of the existing `checkpoint-pulse.js` (same `requestAnimationFrame` loop, same `ensureLayer / start / reset / setVisible` API, same lazy-import idiom via `map-interop.js`). The trail renders as a MapLibre `line` layer over a single GeoJSON source whose features are rebuilt each RAF tick from active trail state. The crossing entry point is the existing `TransitMap.OnCrossingsAsync` JSInvokable, which already fires `Map.PulseCheckpointAsync` gated on `_checkpointsVisible`; we add a sibling `Map.StartCrossingTrailAsync` call alongside it. The one non-trivial cross-cutting change is making the **note duration in seconds** available on the crossing path regardless of audio state — today the duration is a Tone.js note string chosen inside `transit-synth.js` behind the `_unlocked` gate. We expose a pure, audio-independent `durationSecondsFor(vehicleId)` helper (same deterministic `djb2(vehicleId) % durations.length` selection, mapped to seconds at the fixed Tone default tempo) so the trail and the synth agree on duration and the trail still works while muted/locked. The new trail layer is added to the `setMapStyle` capture/restore set so it survives basemap swaps (Principle VII).

Frontend-only. No server, worker, or shared-library changes. No new settings, UI controls, or persistence.

## Technical Context

**Language/Version**: C# / .NET 10.0 (Blazor WebAssembly) + browser ES modules (ES2020), MapLibre GL JS  
**Primary Dependencies**: MapLibre GL JS (basemap + GeoJSON layers), Tone.js v15 (existing synth — referenced only for the shared tempo constant), MatBlazor (unchanged)  
**Storage**: N/A — trails are transient client-side visual ephemera; no persistence  
**Testing**: Manual quickstart verification on the running WASM app (project has no automated JS/Blazor UI test harness; consistent with features 016/017/021)  
**Target Platform**: Browser WASM (desktop + mobile), Azure Static Web App  
**Project Type**: Web application — Blazor WASM frontend; this feature touches only `Client.Shared` (RCL) and `Client.WebApp` (TransitMap page)  
**Performance Goals**: 60 fps map animation preserved; trail tick is one `source.setData` per RAF, mirroring `checkpoint-pulse.js`; no per-frame re-fetch or allocation spikes  
**Constraints**: Trail removed within one animation frame of note end (SC-002); no interference between concurrent trails (SC-006); never blanks or re-fetches data layers on basemap swap (Principle VII)  
**Scale/Scope**: One new JS module (~130 lines), one new interop wrapper method on `Map`, ~10 lines added to `TransitMap.OnCrossingsAsync`, one shared duration helper extracted in `transit-synth.js`, trail layer added to two existing `setMapStyle` restore blocks

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against TransitJazz Constitution v3.2.0:

| Principle | Relevance | Compliance |
|---|---|---|
| **VII. OpenStreetMap-Based Cartography** | Trail is a new GeoJSON `line` layer added on top of the active basemap. | ✅ Trail source/layer added on top of the basemap and registered in the `setMapStyle` capture/restore set so it persists across a basemap swap without re-fetch or re-projection (mirrors how vehicles/trigger-points/routes are restored). |
| **VIII. Generative Transit Music (Deterministic & Non-Authored)** | Trail consumes the same crossing event and note duration as the soundscape. | ✅ No change to note generation, scale, or instrument assignment. Duration selection (`djb2(vehicleId) % durations.length`) stays deterministic; we only expose it in seconds. The trail is a visual echo of the existing crossing note — no per-route authoring. |
| **XI. Snappy, Reversible Overlays** | Trail appears and disappears with the crossing note. | ✅ Trail grows in over the note duration and is removed **immediately** on note end (no exit animation) — consistent with the "instant out" rule. Toggling checkpoint visibility off clears active trails immediately (`reset`). |
| **XII. Internationalized, Settings-Driven Presentation** | Trail reuses the existing checkpoint visibility setting. | ✅ No new setting and **no new user-facing copy** — so no `.resx` additions are required. Gated on the existing checkpoint-visibility state already plumbed through `SetCheckpointVisibilityAsync`. |
| **Tech Stack & Architecture** | Files live in `Client.Shared` RCL + `Client.WebApp`. | ✅ No new projects, no tech substitutions, no server/worker/shared changes. Uses the established lazy-ES-module interop idiom. |

**Result**: PASS. No violations; Complexity Tracking section is not required.

## Project Structure

### Documentation (this feature)

```text
specs/027-checkpoint-note-trail/
├── plan.md              # This file (/speckit.plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── trail-interop.md      # ChefMap.startCrossingTrail / setCrossingTrailVisibility + Map.cs wrappers
│   ├── trail-module.md       # checkpoint-trail.js public API + tuning constants
│   └── duration-helper.md    # transit-synth.js durationSecondsFor(vehicleId) contract
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/
├── wwwroot/js/
│   ├── checkpoint-trail.js        # NEW — RAF-driven growing line layer (sibling of checkpoint-pulse.js)
│   ├── checkpoint-pulse.js        # UNCHANGED reference sibling (pattern source)
│   ├── map-interop.js             # EDIT — add startCrossingTrail + setCrossingTrailVisibility;
│   │                              #        lazy-import checkpoint-trail.js; ensureLayer on map load;
│   │                              #        add trail source/layer to BOTH setMapStyle restore blocks
│   └── transit-synth.js           # EDIT — extract durationSecondsFor(vehicleId) (audio-independent);
│                                  #        reuse it inside triggerNote so durations stay in sync
└── Components/
    └── Map.razor.Helper.cs        # EDIT — add StartCrossingTrailAsync + SetCrossingTrailVisibilityAsync wrappers

src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/
└── Pages/
    └── TransitMap.razor.cs        # EDIT — in OnCrossingsAsync, alongside PulseCheckpointAsync (gated on
                                   #        _checkpointsVisible), call StartCrossingTrailAsync with the
                                   #        bus's current speed + note duration seconds; in the checkpoint
                                   #        visibility handler, the existing SetCheckpointVisibility path
                                   #        also clears trails (trail setVisible(false) resets)
```

**Structure Decision**: Web-application layout, frontend-only. All new behavior lives in the `Client.Shared` RCL (the JS module + the `Map` interop wrapper) plus the `TransitMap` page in `Client.WebApp`. This matches the established pattern set by feature 021 (checkpoint flash/pulse): a dedicated `wwwroot/js` module, lazily imported by `map-interop.js`, surfaced through typed `Map.razor.Helper.cs` wrappers, and driven from `TransitMap.OnCrossingsAsync`.

## Complexity Tracking

> Not required — Constitution Check passed with no violations.
