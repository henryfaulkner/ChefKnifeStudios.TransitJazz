# Implementation Plan: Emergent Transit Soundscape v1

**Branch**: `009-transit-soundscape` | **Date**: 2026-05-22 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/009-transit-soundscape/spec.md`

## Summary

Replace the 008 checkpoint POC with a derived, route-based musical model. Trigger points are generated procedurally from each route's polyline at runtime — no static `checkpoints.json` and no per-point note metadata. Crossing detection lives in a dedicated JavaScript module that consumes per-tick position events emitted by the existing `ChefMapAnimator`. A separate audio module owns a small Tone.js synth palette: one synth per route (deterministic from `routeShortName`), one pitch per vehicle (deterministic from `vehicleId`, drawn from a shared pentatonic scale so concurrent notes harmonize).

**Important baseline correction discovered during research**: a survey of `src/` and git history confirms the 008 implementation **never landed**. No `Checkpoint*.cs`, no `checkpoints.json`, no `checkpoint-audio.js`, no detection code in `vehicle-animator.js`. The spec's FR-013 / SC-008 ("remove 008 artifacts") is therefore trivially satisfied — there is nothing to delete. The auto-memory note that 008 was "implemented, pending manual quickstart sign-off" was stale and has been corrected. This plan is now a pure-addition feature, which simplifies the task list substantially.

## Technical Context

**Language/Version**: C# / .NET 10.0 (Blazor WebAssembly), JavaScript ES2017+ (browser interop)
**Primary Dependencies (kept)**: MapLibre GL JS v4.x (CDN, already loaded for `Map.razor`), `Microsoft.JSInterop`, the existing `ChefMap` and `ChefMapAnimator` JS namespaces
**New Browser Dependency**: Tone.js v15.x — loaded via the same ES-module pattern already used in the codebase (lazy `import()` inside an `IAsyncDisposable` JsInterop wrapper). Bundle cost is acceptable (~150 KB gzipped) for the value delivered; no sample bundles ship.
**New Static Assets**: None. No `checkpoints.json`. No new images, fonts, or audio files.
**Storage**: None server-side. Client keeps two in-memory structures: (a) `routeTriggerPoints: Map<routeId, TriggerPoint[]>` populated lazily when a route's geometry first loads; (b) `vehicleTrackerState: Map<vehicleId, {routeId, lastTriggeredIndex, lastTriggerTimeMs}>` for cooldown and direction-aware crossing detection. Both live for page lifetime.
**Testing**: Manual verification per `quickstart.md`. Five observation sessions corresponding to SC-001 through SC-007: first-note latency, multi-route timbre identification, harmonic compatibility, stopped-bus suppression, moving-bus cadence, console-error count.
**Target Platform**: Same as production — Chrome/Edge/Firefox latest, WebGL + Web Audio capable. Desktop only (per spec assumption).
**Project Type**: Web frontend additive feature (Blazor WASM RCL + WebApp project). No server, worker, or shared model changes.
**Performance Goals**: SC-006 — no observable regression in time-to-first-vehicle. The new position-event emission inside `ChefMapAnimator.tick` is a single C# `[JSInvokable]` call per moved vehicle per tick (≤ ~50 active vehicles × 60 fps = ≤ 3000 calls/sec worst case, in practice ≪ 1000/sec because most vehicles are idle/extrapolating with unchanged positions). Crossing detection is O(activeVehicles) per tick because each vehicle owns its `lastTriggeredIndex` — we only iterate trigger points in the `(lastIndex, currentIndex]` range, not the whole route.
**Constraints**: SC-007 — zero unhandled errors across a 5-minute session including the pre-interaction phase. The Tone.js context MUST NOT be constructed until the first user gesture; before that, crossing events are detected but audio dispatch is a no-op (no buffering, no queuing — the silent period is silent by design, not by deferral). SC-005 — note cadence ≥ 1 per 5 s and ≤ 1 per 30 s during continuous motion. This is the trigger-point-spacing tuning knob; defaults derived in `research.md` § "Spacing tuning".
**Scale/Scope**: ≤ ~50 active MARTA bus vehicles concurrently, distributed across ~40 routes. With ~200m spacing and the longest MARTA bus routes around 30 km, the largest route holds ~150 trigger points. Total in-memory trigger points across all routes: ≈ 3000–4000. Per-vehicle per-tick work touches at most a handful of trigger points (the ones in the route-index delta since the prior tick).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Decoupled Cloud Architecture | PASS | No new service. No SignalR event changes. Worker is untouched. The feature is additive frontend code in `Client.Shared` (RCL) and `Client.WebApp` (DI registration + page wiring). |
| II. No Frontend Secrets | PASS | No new credentials. Tone.js is a public library loaded as an ES module. The trigger-point spacing constant is non-sensitive configuration. |
| III. Two-Pass Real-Time Data Processing Pipeline | PASS | Worker's V1/V2 passes are untouched. The feature consumes the existing `RouteNearestPointBatchEvent` stream (already processed by `ChefMapAnimator.processNearestPointBatch`) and the animator-derived per-frame `currentPos`. No new event types, no new server-side spatial logic. |
| IV. OpenTelemetry Observability | PASS | All new code lives in the WASM client; OTEL applies to .NET components. Crossing-detection events are logged to the browser console using the existing `[ChefMapAnimator]`-style namespace prefix (`[CheckpointTracker]`, `[TransitSynth]`). |
| V. Azure DevOps CI/CD Pipeline | PASS | Same WASM build pipeline produces the same artifact. Tone.js is loaded by lazy `import()` from a CDN URL hard-coded in `transit-synth.js`; no package-manager change, no new container image. |
| VI. GTFS ID Mapping | PASS | Trigger points are keyed by `routeShortName` (the value already on `RouteShapeFeature.Properties.RouteShortName` and the same key the animator uses for `routeGeometry`). The route → instrument mapping is also keyed by `routeShortName` so the same route always gets the same instrument across visitors. |

**Post-Phase-1 Re-check**: No gate changes after Phase 1 design. The feature introduces no new compliance surface. Tone.js is a frontend-implementation choice in the same category as MapLibre GL JS (a browser library); it does not trigger Constitution §"Tech Stack & Architecture" enforcement because that section governs the Azure deployment surface (Static Web App, Container Apps, etc.), not the in-browser library choice for an existing WASM frontend.

## Project Structure

### Documentation (this feature)

```text
specs/009-transit-soundscape/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 — Tone.js loading, crossing detection algorithm, spacing tuning, instrument palette
├── data-model.md        # Phase 1 — TriggerPoint, VehicleTrackerState, RouteInstrumentMap, VehiclePitchMap, cooldown
├── quickstart.md        # Phase 1 — manual verification protocol for SC-001 … SC-007
├── contracts/
│   └── interop-surface.md   # Phase 1 — the JS↔C# interop calls this feature adds (no REST/SignalR contracts to spec)
├── checklists/
│   └── requirements.md  # already created during /speckit-specify
└── tasks.md             # Phase 2 output (/speckit-tasks, not this command)
```

### Source Code (repository root)

> Note on folder names: the most recent rename commit (`9d59c69`) changed the *folder* names from `ChefKnifeStudios.TransitJazz.Client.*` to `ChefKnifeStudios.MartaJazz.Client.*`, while the assembly / root-namespace names remain `ChefKnifeStudios.TransitJazz.Client.*`. Paths below use the actual on-disk folder names.

```text
src/
├── Client/
│   ├── ChefKnifeStudios.MartaJazz.Client.Shared/        # RCL — reused by WebApp
│   │   ├── Components/
│   │   │   ├── Map.razor                                # unchanged
│   │   │   ├── Map.razor.cs                             # unchanged
│   │   │   └── Map.razor.Helper.cs                      # unchanged (the tracker is wired by TransitMap, not Map)
│   │   ├── Models/
│   │   │   └── TriggerPoint.cs                          # NEW — internal record { Index: int, AlongDistanceM: double }
│   │   ├── Services/
│   │   │   ├── ITriggerPointGenerator.cs                # NEW — interface
│   │   │   ├── TriggerPointGenerator.cs                 # NEW — pure C#: (coords, cumDist, spacingM) → IReadOnlyList<TriggerPoint>
│   │   │   └── JsInterop/
│   │   │       ├── AudioPlayerJsInterop.cs              # unchanged (pre-existing, unrelated)
│   │   │       ├── ICheckpointTrackerJsInterop.cs       # NEW — interface
│   │   │       ├── CheckpointTrackerJsInterop.cs        # NEW — lazy ES-module import; ConfigureRouteAsync, ClearAsync
│   │   │       ├── ITransitSynthJsInterop.cs            # NEW — interface
│   │   │       └── TransitSynthJsInterop.cs             # NEW — lazy ES-module import; UnlockAsync, AssignRouteAsync, TriggerNoteAsync
│   │   └── wwwroot/
│   │       └── js/
│   │           ├── map-interop.js                       # unchanged
│   │           ├── vehicle-animator.js                  # EDITED — at end of tick(), call window.CheckpointTracker?.onTick(positionEvents)
│   │           │                                        #   passing a small array of {vehicleId, routeId, prevPos, currPos} for vehicles
│   │           │                                        #   whose position changed this frame. No detection logic added here.
│   │           ├── audioPlayerJsInterop.js              # unchanged
│   │           ├── checkpoint-tracker.js                # NEW — ES module: per-route trigger-point store; per-vehicle index tracking;
│   │           │                                        #   cooldown; dispatches CrossingEvent batch to C# via stored dotNetRef
│   │           └── transit-synth.js                     # NEW — ES module: lazy Tone.js import; route→synth palette; vehicle→pitch;
│   │                                                    #   unlock-on-gesture flow; triggerNote(routeId, vehicleId)
│   │
│   ├── ChefKnifeStudios.MartaJazz.Client.Core/          # unchanged
│   │
│   └── ChefKnifeStudios.MartaJazz.Client.WebApp/
│       ├── Pages/
│       │   ├── TransitMap.razor                         # EDITED — add a top-level "Click to enable audio" hint overlay
│       │   │                                            #   that is removed on first click anywhere on the page
│       │   └── TransitMap.razor.cs                      # EDITED — inject ITriggerPointGenerator, ICheckpointTrackerJsInterop,
│       │                                                #   ITransitSynthJsInterop; on each route geometry load, generate trigger
│       │                                                #   points and push to tracker; assign instrument; on window-level click,
│       │                                                #   call synth.UnlockAsync. [JSInvokable] OnCrossingsAsync(records[])
│       │                                                #   forwards each crossing to synth.TriggerNoteAsync.
│       ├── Program.cs                                   # EDITED — register ITriggerPointGenerator, ICheckpointTrackerJsInterop,
│       │                                                #   ITransitSynthJsInterop (all Scoped)
│       └── wwwroot/
│           ├── index.html                               # unchanged (Tone.js is loaded by lazy ES-module import inside the
│           │                                            #   JsInterop class, not via a <script> tag)
│           └── appsettings.json                         # unchanged
│
└── ChefKnifeStudios.TransitJazz.Shared/                 # unchanged — no shared events, no shared models
```

**Structure Decision**: The feature lives in the existing Blazor RCL (`Client.Shared`) and the consuming WebApp project, matching the pattern established by the existing map and audio interop services. `TriggerPointGenerator` is a *pure C#* service rather than a JS function because (a) it has zero browser-API dependencies, (b) testing pure C# is easier than testing inside a JS module, and (c) it lets the trigger-point list be authoritative on the .NET side which is the natural home for the spacing-knob configuration. Detection itself stays in JavaScript (inside `checkpoint-tracker.js`) because it executes every animation frame and a JS↔C# round-trip per tick per vehicle would dominate the cost.

No new project, no new csproj entries. Five new files (`TriggerPoint.cs`, `ITriggerPointGenerator.cs`, `TriggerPointGenerator.cs`, two interop interface+class pairs), two new JS modules, three edited files (`vehicle-animator.js`, `TransitMap.razor`, `TransitMap.razor.cs`), one edited file (`Program.cs`). No deletions, no renames.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No Constitution Check violations. No complexity tracking entries.

Two judgement calls worth recording (neither is a violation):

1. **Tone.js as a new library**: The feature could be built on raw Web Audio API (as the 008 plan attempted). Choosing Tone.js trades a ~150 KB bundle for: scale/note primitives, scheduling that survives main-thread jank, prebuilt synth voices with sensible envelopes, and one-line note triggering. For a feature whose entire point is *sounding good*, that's the right trade.
2. **Crossing detection in JS, trigger-point generation in C#**: An end-of-spectrum architecture would put both in one place. They are split because they have different cost profiles (generation is once-per-route-load; detection is once-per-frame-per-vehicle) and different ergonomic homes (generation is pure logic that benefits from C#; detection lives inside the JS animation loop). The shared schema is small enough (`{index, alongDistanceM}` per trigger point) that the split has minimal coordination cost.

## Phase 0 Research

See [research.md](research.md). Four items:

1. **Tone.js loading pattern**: Confirm the lazy ES-module import works against `https://esm.sh/tone@15` (or jsDelivr) from a Blazor WASM page without bundling, and confirm the autoplay-unlock pattern (`Tone.start()` from a user gesture) is the cleanest way to satisfy the autoplay restriction.
2. **Crossing-detection algorithm**: Confirm the per-vehicle "lastTriggeredIndex" approach handles teleport (large index jump → don't fire intervening triggers), direction reversal (negative index delta → don't fire), and mid-route appearance (no prior index → record current position as the baseline, fire nothing) per the spec edge cases.
3. **Spacing tuning**: Derive an initial value for `triggerSpacingMeters` from the SC-005 cadence band (≥ 1 note / 30 s, ≤ 1 note / 5 s) and typical MARTA bus speed range (5–15 m/s). Default proposal: 200 m.
4. **Instrument palette**: Pick a palette of 4–8 Tone.js voices that are audibly distinct and pleasant in combination, plus the deterministic route→voice and vehicle→pitch hashing scheme.

## Phase 1 Design

### Data Model

See [data-model.md](data-model.md). Five entities, all client-side:

- **TriggerPoint**: `{ Index: int, AlongDistanceM: double }`. The `Index` is the vertex index on the route polyline at-or-just-after the trigger position. The `AlongDistanceM` is informational/diagnostic. Generated once per route at the moment its geometry is first loaded; cached for page lifetime.
- **VehicleTrackerState** (JS-side): `{ routeId, lastTriggeredIndex, lastTriggerTimeMs }`. Created on a vehicle's first position event; updated each tick.
- **RouteInstrumentMap** (JS-side): `Map<routeShortName, ToneSynth>`. Created lazily when a route's first vehicle fires its first note (deferred until after the gesture unlock).
- **VehiclePitchMap** (JS-side): `Map<vehicleId, midiNumber>`. Computed deterministically from `vehicleId` on first lookup; cached.
- **CrossingEvent** (transient): `{ vehicleId, routeId, triggerIndex }`. Constructed in the JS tracker, batched per tick, dispatched to C# via `dotNetRef.invokeMethodAsync('OnCrossingsAsync', batch)`, consumed by the page's `[JSInvokable]` handler which calls the synth.

The pitch-derivation algorithm (a deterministic hash of `vehicleId` mod scale length, mapped to a pentatonic scale across two octaves) is specified in `data-model.md` § "Pitch derivation". The route→instrument mapping (`routeShortName` hash mod palette length) is in § "Instrument assignment".

### Contracts

See [contracts/interop-surface.md](contracts/interop-surface.md). This feature exposes no REST endpoints, no SignalR events, and no static JSON files. The "contract" surface is the JS↔C# interop boundary — the four methods this feature adds to the existing interop pattern, their parameter shapes, their idempotency guarantees, and the single `[JSInvokable]` C# callback they invoke. Documenting it as a contract makes the boundary explicit and makes the test surface obvious.

### Quickstart

See [quickstart.md](quickstart.md). The verification protocol:

1. **First-note latency (SC-001)**: AppHost up, browser open at `/transit-map`, click once, time to first audible note ≤ 30 s.
2. **Distinct timbres (SC-002)**: Listen 2 min with ≥ 3 active routes; subjectively confirm distinct instrument families.
3. **Harmonic compatibility (SC-003)**: Listen 2 min focused on a single route with multiple vehicles; confirm no audible dissonance.
4. **Stopped-bus suppression (SC-004)**: Identify a stopped vehicle near a trigger point; observe 60 s; confirm at most one note.
5. **Cadence band (SC-005)**: Identify a moving vehicle in a low-traffic area; time its successive notes; confirm interval in [5 s, 30 s].
6. **No regression (SC-006)**: Cold-load `/transit-map`; compare time-to-first-vehicle against the prior production build using browser devtools network/performance tab.
7. **Zero console errors (SC-007)**: 5 min session covering pre-interaction load, first-click transition, and steady state; browser console shows no errors (warnings are acceptable for autoplay-deferred frames).

### Agent Context

`CLAUDE.md`'s SPECKIT marker block will be updated to point to this plan (`specs/009-transit-soundscape/plan.md`) as the active feature plan. The 008 reference will be removed since 008 never landed and 009 supersedes its intent.
