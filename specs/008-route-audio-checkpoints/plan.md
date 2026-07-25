# Implementation Plan: Route Audio Checkpoints

**Branch**: `008-route-audio-checkpoints` | **Date**: 2026-05-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/008-route-audio-checkpoints/spec.md`

## Summary

Add a frontend-only "checkpoint" layer on top of the existing transit map: a small set of points fixed to specific route polylines, loaded at startup from a static JSON file under `wwwroot/`. Render each checkpoint as a marker on its route line, detect when an animated vehicle's position crosses one (using the same animator-managed positions that already drive `vehicles-layer`), and synthesize a short pitched note in the browser via the Web Audio API whenever a crossing occurs. A 10-second per-vehicle/per-checkpoint cooldown suppresses repeats. The marker corresponding to a firing checkpoint briefly pulses for visual correlation.

The change is contained entirely in the frontend: the existing `ChefMapAnimator` already maintains per-vehicle `currentPos` along route geometry, and the existing `Map` component already loads each route's coordinates into `ChefMapAnimator.routeGeometry`. Adding a checkpoint detection step is a new responsibility hooked into the animator's per-tick state — no server-side spatial logic and no new SignalR events.

This is a POC. The pitch-derivation algorithm is intentionally simple (deterministic map from `routeId` + along-route position to a note in a fixed pentatonic scale). Hand-pick a handful of checkpoints across 2–3 routes; broader checkpoint authoring (per-route generation, in-browser editing, persistence) is out of scope.

## Technical Context

**Language/Version**: C# / .NET 10.0 (Blazor WebAssembly), JavaScript ES2017+ (browser interop)
**Primary Dependencies (kept)**: MapLibre GL JS v4.x (CDN, already loaded for `Map.razor`), `Microsoft.JSInterop`, the existing `ChefMap` and `ChefMapAnimator` JS namespaces
**New Browser APIs**: Web Audio API (`AudioContext`, `OscillatorNode`, `GainNode`) — built into every supported browser; no library shipped
**New Static Assets**: `wwwroot/checkpoints.json` in `ChefKnifeStudios.TransitJazz.Client.WebApp` (served by Static Web App)
**Storage**: None server-side. Client keeps an in-memory map of `(vehicleId, checkpointId) → lastFireTimeMs` for cooldown; lives for page lifetime.
**Testing**: Manual verification per `quickstart.md`. Three observation sessions: (1) audio fires on visible crossing; (2) cooldown holds for oscillating vehicle; (3) autoplay-restricted pre-interaction load produces no console errors.
**Target Platform**: Same as production — Chrome/Edge/Firefox latest, WebGL + Web Audio capable.
**Project Type**: Web frontend additive feature (Blazor WASM RCL + WebApp project). No server, worker, or shared model changes.
**Performance Goals**: SC-003 — no observable regression in time-to-first-vehicle. The checkpoint pass runs inside the existing `ChefMapAnimator.tick`'s vehicle loop and is O(vehicles × checkpoints-on-that-route). With ≤ 20 checkpoints concentrated on a handful of routes, this is well under one frame of work at 60 fps.
**Constraints**: SC-001 — audio fires within 2 seconds of a visible crossing on ≥ 9/10 observed crossings. SC-005 — graceful handling of browser autoplay restriction (no unhandled errors before first user gesture). The synthesizer MUST NOT spawn an `AudioContext` until the first user interaction; before that, trigger events are still detected and dispatched (so the marker pulse from FR-006 still works) but audio is silently suppressed.
**Scale/Scope**: POC scale — ≤ 30 checkpoints total, distributed across ≤ 5 routes. No persistence, no editor, no per-checkpoint audio files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Decoupled Cloud Architecture | PASS | No new service. No SignalR event changes. Worker is untouched. The feature is additive frontend code in `Client.Shared` (RCL) and `Client.WebApp` (static asset + DI). |
| II. No Frontend Secrets | PASS | No new credentials introduced. MapTiler key arrangement unchanged. The `checkpoints.json` file is public configuration, not a secret — it contains route IDs, coordinates, and note-derivation parameters only. |
| III. Two-Pass Real-Time Data Processing Pipeline | PASS | Worker's V1/V2 passes are untouched. The feature consumes the existing `RouteNearestPointBatchEvent` stream and the animator-derived per-frame `currentPos`. No new event types. |
| IV. OpenTelemetry Observability | PASS | All new code lives in the WASM client; OTEL applies to .NET components. Client-side detection events are logged to the browser console (matching the existing animator log conventions) for debugging. |
| V. Azure DevOps CI/CD Pipeline | PASS | Same WASM build pipeline produces the same artifact. `checkpoints.json` ships as part of `wwwroot`. No new container image, no new external dependency. |
| VI. GTFS ID Mapping | PASS | Checkpoints are keyed by `routeShortName` (the same join key used by `RouteShapeFeature.Properties.RouteShortName` and consumed by `TransitMap.razor.cs` line 196 — `key = routeShapeFeature.Properties.RouteShortName ?? routeShapeFeature.Properties.RouteId`). Authors writing `checkpoints.json` use the public-facing short name (e.g., `"74"`), matching how the frontend already indexes routes. |

**Post-Phase-1 Re-check**: No gate changes after Phase 1 design. The feature does not amend any principle and introduces no new compliance surface. The Web Audio synthesis decision is documented in `research.md` and is purely a frontend-implementation choice — it does not invoke a new technology category subject to Constitution §"Tech Stack & Architecture" enforcement.

## Project Structure

### Documentation (this feature)

```text
specs/008-route-audio-checkpoints/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 — Web Audio synthesis, crossing detection, marker rendering
├── data-model.md        # Phase 1 — Checkpoint, TriggerEvent, cooldown table, note-derivation
├── quickstart.md        # Phase 1 — manual verification protocol
├── contracts/
│   └── checkpoints-json.md   # Phase 1 — exact JSON schema for wwwroot/checkpoints.json
├── checklists/
│   └── requirements.md  # already created
└── tasks.md             # Phase 2 output (`/speckit-tasks`, not this command)
```

### Source Code (repository root)

```text
src/
├── Client/
│   ├── ChefKnifeStudios.TransitJazz.Client.Shared/        # RCL — reused by WebApp
│   │   ├── Components/
│   │   │   ├── Map.razor                                  # unchanged
│   │   │   ├── Map.razor.cs                               # unchanged
│   │   │   └── Map.razor.Helper.cs                        # EDITED — add ConfigureCheckpointsAsync(checkpoints, dotNetRef)
│   │   │                                                  #   wraps a new ChefMap.configureCheckpoints JS call;
│   │   │                                                  #   forwards a DotNetObjectReference for trigger callbacks
│   │   ├── Models/
│   │   │   └── Checkpoint.cs                              # NEW — C# record mirroring the JSON schema
│   │   ├── Services/
│   │   │   ├── ICheckpointLoader.cs                       # NEW — interface
│   │   │   ├── CheckpointLoader.cs                        # NEW — fetches /checkpoints.json via HttpClient,
│   │   │   │                                              #   deserializes to IReadOnlyList<Checkpoint>
│   │   │   └── JsInterop/
│   │   │       ├── AudioPlayerJsInterop.cs                # unchanged (kept; see research.md note on duplicate copy)
│   │   │       └── ICheckpointAudioJsInterop.cs           # NEW — interface
│   │   │       └── CheckpointAudioJsInterop.cs            # NEW — lazy ES-module import of checkpoint-audio.js;
│   │   │                                                  #   PlayNoteAsync(midiNote, durationMs)
│   │   └── wwwroot/
│   │       └── js/
│   │           ├── map-interop.js                         # EDITED — add ChefMap.configureCheckpoints,
│   │           │                                          #   ChefMap.pulseCheckpoint; add a checkpoints-layer
│   │           │                                          #   GeoJSON source on map load
│   │           ├── vehicle-animator.js                    # EDITED — checkpoint detection inside tick():
│   │           │                                          #   maintain per-vehicle prior route-index; for each
│   │           │                                          #   checkpoint on the vehicle's route between prior
│   │           │                                          #   and current index, dispatch a trigger via the
│   │           │                                          #   stored dotNetRef; cooldown enforced per
│   │           │                                          #   (vehicleId, checkpointId)
│   │           ├── audioPlayerJsInterop.js                # unchanged
│   │           └── checkpoint-audio.js                    # NEW — ES module: lazy AudioContext, playNote(midi, ms)
│   │
│   ├── ChefKnifeStudios.TransitJazz.Client.Core/          # unchanged
│   │
│   └── ChefKnifeStudios.TransitJazz.Client.WebApp/
│       ├── Pages/
│       │   ├── TransitMap.razor                           # unchanged
│       │   └── TransitMap.razor.cs                        # EDITED — inject ICheckpointLoader +
│       │                                                  #   ICheckpointAudioJsInterop; load checkpoints.json
│       │                                                  #   in OnInitializedAsync (parallel with routes);
│       │                                                  #   in OnMapReadyAsync push checkpoints to the map
│       │                                                  #   after route geometries are loaded;
│       │                                                  #   add [JSInvokable] OnCheckpointTriggeredAsync
│       │                                                  #   that calls the audio interop + pulse-marker
│       ├── Program.cs                                     # EDITED — register ICheckpointLoader,
│       │                                                  #   ICheckpointAudioJsInterop
│       └── wwwroot/
│           ├── checkpoints.json                           # NEW — authored-by-hand POC checkpoint set
│           ├── index.html                                 # unchanged (the new ES module is lazy-imported
│           │                                              #   by the JsInterop class — no <script> tag)
│           └── appsettings.json                           # unchanged
│
└── ChefKnifeStudios.TransitJazz.Shared/                   # unchanged — no shared events, no shared models
```

**Structure Decision**: The feature lives in the existing Blazor RCL (`Client.Shared`) and the consuming WebApp project. The RCL is the right home for the new component-level interop services and the audio/checkpoint JS files because both are reused-in-principle and follow the existing `_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/...` pattern already established by `audioPlayerJsInterop.js` and `map-interop.js`. The `checkpoints.json` data file goes in `Client.WebApp/wwwroot/` (not in the RCL) because it is environment-specific runtime data — the equivalent of `appsettings.json`. A future hosted/multi-tenant scenario would replace this file at deploy time, not at compile time.

No new project, no new csproj entries. New files only — no deletions, no renames.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No Constitution Check violations. No complexity tracking entries.

The one judgement call worth recording here (not a violation): the feature deliberately puts checkpoint-crossing detection in JavaScript inside `vehicle-animator.js`, *not* in C# inside the Blazor component. The animator already owns the per-frame `currentPos` for every vehicle; doing detection there avoids an extra round-trip (JS→C# per frame for every vehicle), and reuses the cumulative-distance machinery already present (`buildCumulativeDistances`, `findNearestIndex`). The trade-off is that the detection algorithm is written twice in spirit (once in C# for any future server-side use, never; once in JS now) — but for a POC the JS-only path is materially simpler and faster.

## Phase 0 Research

See [research.md](research.md). Three items, all small:

1. **Web Audio synthesis**: Confirm the `OscillatorNode` + `GainNode` envelope approach is sufficient for a recognisable musical note in ~200ms, and pin down the autoplay-restriction handling pattern (lazy `AudioContext`, unlock on first user gesture).
2. **Crossing detection algorithm**: Confirm the "route-index-crossed" approach (prior vs. current `findNearestIndex` on the route polyline, dispatch any checkpoint whose index falls in the closed interval) handles the edge cases from spec — vehicle teleport, two-checkpoints-on-one-segment, direction reversal.
3. **Marker rendering with MapLibre**: Confirm a second GeoJSON source/circle-layer is the right MapLibre pattern, and document the pulse/highlight approach (transient paint property animation vs. brief layer overlay).

## Phase 1 Design

### Data Model

See [data-model.md](data-model.md). Three entities, all client-side:

- **Checkpoint**: `id`, `routeShortName`, `position {lon, lat}`, `note { scaleDegree, octave }`. Loaded once from `checkpoints.json` at page init. Off-route coordinates are snapped to the nearest polyline vertex at load time and a warning is logged (per spec edge-case).
- **VehicleCheckpointCooldown**: ephemeral in-memory `Map<(vehicleId, checkpointId), lastFireEpochMs>`. Lives inside `ChefMapAnimator`. Pruned when a vehicle is removed from the animator's tracking.
- **TriggerEvent**: transient — never stored. Constructed at detection, dispatched to C#, consumed for audio + pulse. No history.

The note-derivation algorithm (pentatonic-scale mapping from `routeShortName` hash + `scaleDegree`) is specified in `data-model.md` § "Note derivation".

### Contracts

See [contracts/checkpoints-json.md](contracts/checkpoints-json.md). The only external contract this feature exposes is the schema of `wwwroot/checkpoints.json`. The contract document gives the exact JSON shape, the validation rules a checkpoint must satisfy at load time (FR-007, edge case "off the route line"), and a worked example covering at least three checkpoints on two different routes.

### Quickstart

See [quickstart.md](quickstart.md). The verification protocol:

- Run the AppHost. Open `/transit-map`. Click anywhere on the page to satisfy the browser autoplay gesture requirement.
- Watch a vehicle approach a visible checkpoint marker. Confirm: (a) a note plays, (b) the marker pulses, (c) browser console shows a single `[CheckpointAudio] fired` log for that vehicle/checkpoint pair.
- Wait for the same vehicle to oscillate near the same checkpoint. Confirm no second fire occurs inside 10 s.
- Reload the page. Do NOT click. Wait for a vehicle to fire a checkpoint. Confirm: no console error; the marker still pulses; no audio is heard. Then click. Subsequent fires produce audio.
- Run an SC-003 sanity check: navigate to `/transit-map` cold; compare time-to-first-vehicle with the previous build (no observable regression).

### Agent Context

`CLAUDE.md`'s SPECKIT marker block will be updated to point to this plan (`specs/008-route-audio-checkpoints/plan.md`) as the active feature plan once Phase 1 artifacts are written.
