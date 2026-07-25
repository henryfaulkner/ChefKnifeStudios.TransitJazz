---

description: "Task list for Route Audio Checkpoints (008)"
---

# Tasks: Route Audio Checkpoints

**Input**: Design documents from `/specs/008-route-audio-checkpoints/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/checkpoints-json.md, quickstart.md

**Tests**: The feature spec does NOT request automated tests (the POC validation is the manual quickstart protocol). No `tests/` tasks are generated. The "Independent Test" criterion for each user story refers to the manual procedure in `quickstart.md`.

**Organization**: Tasks are grouped by user story. P1 (US1 — hear audio on crossing) is the MVP and is fully self-contained. P2 (US2 — see markers + pulse) is additive on top of US1. P3 (US3 — edit checkpoints without recompile) is satisfied implicitly by the design but has one explicit validation task.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Different file, no dependency on incomplete tasks — safe to parallelize.
- **[Story]**: `[US1]`, `[US2]`, `[US3]` — maps to spec.md user stories.
- File paths are absolute-from-repo-root.

## Path Conventions

This is a Blazor WebAssembly web app. The relevant trees:

- **RCL (shared client)**: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/`
- **WebApp (host)**: `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/`
- **JS interop assets (RCL `wwwroot`)**: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/`

No server, worker, or shared-model changes for this feature.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm we're on the right branch and the feature dir is ready. No project initialization required — all touched projects already exist.

- [X] T001 Verify on branch `008-route-audio-checkpoints` and working tree is clean of unrelated edits (`git status`).
- [X] T002 Confirm `src/ChefKnifeStudios.TransitJazz.sln` builds clean (`dotnet build`) before any changes — this is the no-regression baseline for SC-003.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Cross-story scaffolding that every user story depends on. Specifically: the `Checkpoint` C# model, the loader that reads `wwwroot/checkpoints.json`, and the JS hook point on `ChefMapAnimator` for receiving the checkpoint list. Without these, neither audio (US1) nor markers (US2) can render.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 [P] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Models/Checkpoint.cs` with the `Checkpoint` and `CheckpointNote` records per `data-model.md` § 1. Reuse the existing `Position` record from `ChefKnifeStudios.TransitJazz.Client.Shared.Models` (do not add a new geo primitive).
- [X] T004 [P] Create the example `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/checkpoints.json` with at least three checkpoints across two routes per the worked example in `contracts/checkpoints-json.md`. Coordinates must visibly sit on the corresponding route polylines (verify by eye against the running map before committing).
- [X] T005 Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Services/ICheckpointLoader.cs` declaring `Task<IReadOnlyList<Checkpoint>> LoadAsync(CancellationToken ct = default)`. Depends on T003.
- [X] T006 Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Services/CheckpointLoader.cs` implementing `ICheckpointLoader`. Inject `HttpClient` (named — reuse the `TransitJazzAPI` named client OR fall back to `IHttpClientFactory.CreateClient()` for the WebApp host) and `ILogger<CheckpointLoader>`. Fetch `/checkpoints.json`, deserialize with `System.Text.Json`, apply the validation rules from `data-model.md` § 1 (unique id, valid lon/lat, scaleDegree/octave range, version=1). Return an empty list with a logged warning on 404/parse error/unknown version (fail-open per contract). Depends on T003, T005.
- [X] T007 Edit `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/vehicle-animator.js`: extend `ChefMapAnimator` with the storage fields and the `configureCheckpoints(containerDivId, checkpointsArray, dotNetRef)` entry point per `data-model.md` § 2 and § 3. Specifically: add `this.checkpoints = {}`, `this.checkpointsByRoute = {}`, `this.cooldown = new Map()`, `this.checkpointDotNetRef = null`, `this.CHECKPOINT_COOLDOWN_MS = 10000`. `configureCheckpoints` MUST snap each checkpoint to its nearest route-polyline vertex using the existing `findNearestIndex` against the already-loaded `routeGeometry[routeShortName]` (warn-and-snap up to 500 m, reject beyond — per `data-model.md` § 1 thresholds), store the snapped `coord` and resolved `routeIndex` on the runtime record, build the `checkpointsByRoute` index, and stash `dotNetRef`. Does NOT add the per-frame detection yet — that's a US1 task. Depends on T003 (mirrors the shape).
- [X] T008 Edit `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor.Helper.cs`: add `public async Task ConfigureCheckpointsAsync(object[] checkpoints, DotNetObjectReference<TransitMap> dotNetRef)` that calls `JsRuntime.InvokeVoidAsync("ChefMapAnimator.configureCheckpoints", ElementId, checkpoints, dotNetRef)`. The `checkpoints` argument is an anonymous-object array shaped `{ id, routeShortName, longitude, latitude, note = { scaleDegree, octave } }` so JSON-marshalling is unambiguous. Wrap in the existing `try/catch + Console.WriteLine` pattern used by the other helper methods. Depends on T007.
- [X] T009 Edit `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Program.cs`: register `builder.Services.AddSingleton<ICheckpointLoader, CheckpointLoader>();` next to the existing `IAudioPlayerJsInterop` registration. Depends on T005, T006.
- [X] T010 Edit `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`: inject `ICheckpointLoader`; add a field `IReadOnlyList<Checkpoint> _checkpoints = Array.Empty<Checkpoint>();`; in `OnInitializedAsync` add `_checkpoints = await CheckpointLoader.LoadAsync()` running in parallel with `LoadRoutesAsync` (use `Task.WhenAll`). Does NOT push to the map yet — the map-ready push is added in a US1/US2 task. Depends on T009.

**Checkpoint**: At end of Phase 2, the app loads `checkpoints.json` at startup, validates entries, and has the JS hook ready — but no audio plays and no markers render yet. Build is clean; `/transit-map` looks identical to main.

---

## Phase 3: User Story 1 — Hear A Sound When A Vehicle Passes A Checkpoint (Priority: P1) 🎯 MVP

**Goal**: Spec User Story 1 — when a live vehicle's animated position crosses a configured checkpoint, the browser plays a short pitched note exactly once, with a 10-second per-(vehicle, checkpoint) cooldown, and gracefully handles browser autoplay restrictions.

**Independent Test**: `quickstart.md` Tests 1, 2, and 3 (audio fires on visible crossing; cooldown holds; pre-gesture handling produces no errors and suppresses audio).

### Implementation for User Story 1

- [X] T011 [P] [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/checkpoint-audio.js` as an ES module per `research.md` § R1. Export `playNote(midiNote, durationMs)`. Module state: a single lazy `AudioContext` created on first user gesture (one-shot `pointerdown`/`keydown`/`touchstart` listener registered at module-load time). `playNote` is a no-op when the context is not yet created (logs `[CheckpointAudio] fired (audio suppressed: pre-gesture) midi=<n>`). When the context exists: build an `OscillatorNode` (`type: 'triangle'`, `frequency: 440 * 2 ** ((midi - 69) / 12)`) → `GainNode` (gain 0 → 0.25 over 10 ms, exponential ramp to 0.0001 over `durationMs - 10` ms) → `audioCtx.destination`; `start(now)`, `stop(now + durationMs/1000 + 0.05)`. Also export a default `export function play(soundUrl)` shim if needed for parity — NO, that's in the existing `audioPlayerJsInterop.js`; this module is checkpoint-specific.
- [X] T012 [P] [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Services/JsInterop/ICheckpointAudioJsInterop.cs` and `CheckpointAudioJsInterop.cs` following the existing `AudioPlayerJsInterop` pattern (lazy `IJSObjectReference` via `Lazy<Task<IJSObjectReference>>`, cache-bust GUID on the import URL, `IAsyncDisposable`, try/catch + `ILogger`). Module path: `./_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/checkpoint-audio.js`. Public surface: `Task PlayNoteAsync(int midi, int durationMs = 200)`. Depends on T011.
- [X] T013 [US1] In `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/vehicle-animator.js` add the `tryFireCrossings(state, currentIdx, now)` private helper per `research.md` § R2 and `data-model.md` § 5 — iterates `this.checkpointsByRoute[state.routeId]`, fires any checkpoint whose `routeIndex` lies in `[min(state.lastRouteIndex, currentIdx), max(state.lastRouteIndex, currentIdx)]` AND whose cooldown key `(state.vehicleId + '|' + cp.id)` is absent OR older than `now - CHECKPOINT_COOLDOWN_MS`. Dispatch fires via `this.checkpointDotNetRef.invokeMethodAsync('OnCheckpointTriggeredAsync', { vehicleId, checkpointId, routeShortName, note })`. Stamp `this.cooldown.set(key, now)` immediately on dispatch (before the async resolves) to make the cooldown atomic against duplicate fires within the same tick. Skip the call when `this.checkpointDotNetRef` is null (feature disabled). Depends on T007.
- [X] T014 [US1] In the same `vehicle-animator.js`, edit `tick()` to: (a) after the position update for each non-idle vehicle compute `var currentIdx = this.findNearestIndex(routeData.coords, newPos);` (only when `routeData` exists), (b) if `state.lastRouteIndex !== undefined` AND `currentIdx !== state.lastRouteIndex`, call `this.tryFireCrossings(state, currentIdx, now);`, (c) write `state.lastRouteIndex = currentIdx;`. Reuse the existing `routeData` lookup if already present in the frame; otherwise add a local `var routeData = this.routeGeometry[state.routeId];`. Depends on T013.
- [X] T015 [US1] In `vehicle-animator.js`, edit `processNearestPointBatch` so that the new-vehicle and route-transfer branches initialise `lastRouteIndex` to `undefined` (so the first tick after a fresh state stamps the index without firing) and so that the cooldown entries for a transferring vehicle are pruned (iterate `this.cooldown.keys()` and `delete` entries prefixed with `rec.vehicleId + '|'`). Depends on T014.
- [X] T016 [US1] Edit `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`: inject `ICheckpointAudioJsInterop`. Add a `DotNetObjectReference<TransitMap>? _dotNetRef` field initialised in `OnAfterRenderAsync(firstRender: true)` (or `OnMapReadyAsync` before `ConfigureCheckpointsAsync` — whichever keeps the lifetime cleanly tied to the page). In `OnMapReadyAsync`, AFTER the existing route-geometry push loop, project `_checkpoints` to the anonymous-object shape expected by `ConfigureCheckpointsAsync` (T008) and call `await _map!.ConfigureCheckpointsAsync(checkpointsObjArray, _dotNetRef)`. Dispose `_dotNetRef` in the existing `Dispose()` method. Depends on T010, T012, T008.
- [X] T017 [US1] In the same `TransitMap.razor.cs`, add `[JSInvokable("OnCheckpointTriggeredAsync")] public async Task OnCheckpointTriggeredAsync(CheckpointTriggerPayload payload)` where `CheckpointTriggerPayload` is a nested or sibling record: `(string VehicleId, string CheckpointId, string RouteShortName, CheckpointNote Note)`. The handler computes the MIDI note via a `ComputeMidi(string routeShortName, CheckpointNote note)` helper that implements the pentatonic-minor algorithm from `data-model.md` § 6 (`sum-of-char-codes mod 12` tonic + `pentatonicMinor[scaleDegree]` interval + `12 * (octave + 1)`), then fire-and-forgets `CheckpointAudioJsInterop.PlayNoteAsync(midi, 200)`. Log `[CheckpointAudio] fired vehicleId=<id> checkpointId=<id> midi=<n>` via the injected `ILogger`. Marker pulse is added in US2 — for US1 the handler only does audio. Depends on T016.

**Checkpoint**: US1 is complete. Running the app produces audio when a vehicle crosses a configured checkpoint. Cooldown suppresses repeats. No console errors before first user gesture. `quickstart.md` Tests 1, 2, 3 pass. Markers are NOT yet visible (that's US2).

---

## Phase 4: User Story 2 — See Checkpoints On The Map (Priority: P2)

**Goal**: Spec User Story 2 — checkpoint markers render on their route lines as distinct amber dots; vehicles render above them; when a checkpoint fires for a vehicle, its marker briefly pulses so the audio can be correlated with a location.

**Independent Test**: `quickstart.md` Test 1 step 4 (pulse on fire), Test 6 row "Two checkpoints close on one route" (both fire visibly), and the "checkpoints visible without audio" smoke check in spec US2 Independent Test.

### Implementation for User Story 2

- [X] T018 [US2] Edit `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/map-interop.js`: inside the `map.on('load', ...)` callback in `createMap`, AFTER the existing `vehicles` source/layer registration, add a `'checkpoints'` GeoJSON source initialised with an empty FeatureCollection, then add `'checkpoints-layer'` (base circle: `circle-radius: 5`, `circle-color: '#fbbf24'`, `circle-stroke-width: 1`, `circle-stroke-color: '#fff'`) BEFORE the `vehicles-layer` (use the third-argument `beforeId` of `addLayer` — `map.addLayer({...}, 'vehicles-layer')`) so vehicles render on top. Then add `'checkpoints-pulse-layer'` (same source, same colour, `circle-radius: 5`, `circle-stroke-width: 1`, `circle-opacity: 0`) with a filter `['==', ['get', 'id'], '']` (matches nothing by default), ABOVE `vehicles-layer` so the pulse can briefly draw over a passing vehicle. Per `research.md` § R3.
- [X] T019 [US2] In the same `map-interop.js`, add `ChefMap.setCheckpointFeatures(containerDivId, checkpointsArray)` that builds a FeatureCollection from `[{ id, coord }]` (each feature: `{ type: 'Feature', id: cp.id, geometry: { type: 'Point', coordinates: cp.coord }, properties: { id: cp.id } }`) and calls `map.getSource('checkpoints').setData(...)`. No-op if the source is absent (defensive). Depends on T018.
- [X] T020 [US2] In the same `map-interop.js`, add `ChefMap.pulseCheckpoint(containerDivId, checkpointId)` per `research.md` § R3: set the pulse-layer's filter to `['==', ['get', 'id'], checkpointId]`, set `circle-opacity` to 0.9, then over a single RAF chain animate `circle-radius` 5 → 18 over the first ~200 ms and `circle-opacity` 0.9 → 0 over the remaining ~400 ms; on completion reset the filter to `['==', ['get', 'id'], '']` and reset paint to base values. Coalesce overlapping pulses by always re-anchoring the start time and the target feature id — at most one pulse animation per map at a time is fine for the POC.
- [X] T021 [US2] In `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/vehicle-animator.js`, edit `configureCheckpoints` (added in T007) to also call `ChefMap.setCheckpointFeatures(containerDivId, builtFeatureArray)` after building the per-checkpoint runtime records with their snapped coords. This is the only place the markers are pushed to the map. Depends on T019.
- [X] T022 [US2] Edit `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor.Helper.cs`: add `public async Task PulseCheckpointAsync(string checkpointId)` that calls `JsRuntime.InvokeVoidAsync("ChefMap.pulseCheckpoint", ElementId, checkpointId)`. Same try/catch pattern as the other helpers.
- [X] T023 [US2] Edit `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`: in `OnCheckpointTriggeredAsync` (added in T017), add a second fire-and-forget call `_ = _map!.PulseCheckpointAsync(payload.CheckpointId)` running in parallel with the audio call. Depends on T017, T022.

**Checkpoint**: US2 is complete. Checkpoints render as amber dots on route lines; the corresponding dot pulses each time a fire is dispatched. Markers can be observed without audio (mute tab) per spec US2 Independent Test. `quickstart.md` Test 1 step 4 passes.

---

## Phase 5: User Story 3 — Configure Checkpoints Without Recompiling (Priority: P3)

**Goal**: Spec User Story 3 — a developer can edit `wwwroot/checkpoints.json` and see the change after restart, with no rebuild of compiled code.

**Independent Test**: `quickstart.md` Test 5 (edit + reload roundtrip < 5 min).

The design satisfies this story implicitly (`CheckpointLoader` reads the static JSON file at every page init — there is no compiled-in checkpoint list and no migration step). The remaining task is to validate that the dev-loop actually works as described, and to document the editor experience as part of the quickstart.

### Implementation for User Story 3

- [X] T024 [US3] Edit `specs/008-route-audio-checkpoints/quickstart.md` Test 5: if dev-mode static-file serving in `Client.WebApp` does NOT pick up `wwwroot/checkpoints.json` edits without a rebuild (verify empirically — Blazor WASM `dotnet watch` behaviour for files under `wwwroot` is the relevant question), update Test 5 to explicitly say "hard-reload after rebuilding the static content only — no recompilation of any `.cs` file is required" and confirm that's still under 5 minutes. If `dotnet watch` does pick it up live, leave the test as written and note "verified live-reload works" next to Test 5. This is a validation-and-doc task, not a code change.

**Checkpoint**: US3 is validated. The quickstart accurately describes the editing loop. No compiled-code change is required to add/move/remove a checkpoint.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final pass for build hygiene, code style consistency, and full quickstart sign-off. No new functionality.

- [X] T025 [P] Run `dotnet build src/ChefKnifeStudios.TransitJazz.sln` from a clean state. Compare warning count against the T002 baseline. Zero new warnings.
- [ ] T026 [P] Sanity-check the lazy ES-module pattern in `CheckpointAudioJsInterop`: load `/transit-map`, watch DevTools → Network, confirm `checkpoint-audio.js` is requested at most once and ONLY after the first checkpoint trigger fires (it must not be eagerly fetched on page load).
- [ ] T027 [P] Sanity-check the dev-tools console on `/transit-map` after a 5-minute live session: no red errors; expected `[CheckpointAudio] fired ...` lines present; expected `[ChefMapAnimator]` lines unchanged in shape vs. main.
- [ ] T028 Run `quickstart.md` Test 4 (no-regression sanity for SC-003): measure time-to-first-vehicle on `main` vs. this branch, both fresh-built, both with hard-reload. Within ±10 %.
- [ ] T029 Run the full `quickstart.md` sign-off checklist (all seven tests). All boxes ticked.
- [ ] T030 Update memory (optional follow-up): if implementation surfaced any non-obvious decision the author wants to keep (e.g., "JS-only detection chosen because per-frame JS↔C# round-trip was too expensive" — already in plan.md complexity note, may not need a separate memory), record it via the memory system. Skip if nothing surprising came up.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies. T001 and T002 can run sequentially or in parallel.
- **Foundational (Phase 2)**: Depends on Setup. **BLOCKS US1 and US2**. T003 and T004 are parallel; T005 → T006 are serial; T007 is independent (different file from T003–T006); T008 depends on T007; T009 depends on T006; T010 depends on T009.
- **User Story 1 (Phase 3)**: Depends on Phase 2 complete. Internal: T011 ∥ T012; T013 → T014 → T015; T016 depends on T010 + T012 + T008; T017 depends on T016.
- **User Story 2 (Phase 4)**: Depends on Phase 2 complete (NOT on US1 — US2 markers can render without audio per the spec's "Independent Test"). If US1 is already done and the trigger handler exists, T023 wires the pulse in. If US1 is not done, US2 can ship markers without the pulse and add T023 once US1 lands. Internal: T018 → T019 → T021; T020 ∥ T021 (different layer ops on the same file but distinct functions); T022 ∥ T020.
- **User Story 3 (Phase 5)**: Depends on Phase 2 complete (the loader fetches the JSON; no code changes here). Can run in parallel with US1 or US2.
- **Polish (Phase 6)**: Depends on all desired user stories being complete.

### User Story Dependencies

- **User Story 1 (P1)**: After Phase 2. Independent of US2 and US3.
- **User Story 2 (P2)**: After Phase 2. Can ship markers independently; pulse-on-fire (T023) requires US1's trigger handler.
- **User Story 3 (P3)**: After Phase 2. No code dependencies — pure validation + doc.

### Within Each User Story

- Models and JS data structures before services that consume them.
- JS hook points (`configureCheckpoints`, `tryFireCrossings`, `pulseCheckpoint`) before the C# call sites that invoke them.
- For US1, the audio module (T011) and its C# interop wrapper (T012) can be built in parallel with the animator detection logic (T013–T015) — they meet at the C# `[JSInvokable]` handler (T017).

### Parallel Opportunities

- **Phase 2**: T003 ∥ T004 (model file vs. data file); T007 ∥ {T003, T005, T006} (animator JS edit vs. C# model/loader).
- **Phase 3 (US1)**: T011 ∥ T012 (JS module vs. C# wrapper, but T012 depends on T011 being present to import — sequentially safer; alternatively scaffold T012 against the not-yet-present module path and complete T011 first); T013/T014/T015 are sequential edits to the same file (`vehicle-animator.js`) and CANNOT be parallelised.
- **Phase 4 (US2)**: T018/T019/T020 are sequential edits to the same file (`map-interop.js`); T022 is in a different file and is parallelisable.
- **Phase 6**: T025 ∥ T026 ∥ T027 (different verification surfaces).

---

## Parallel Example: User Story 1 kick-off

```bash
# After Phase 2 completes, two tracks open up:
# Track A — audio:
Task: "Create checkpoint-audio.js Web Audio module"           # T011
Task: "Create CheckpointAudioJsInterop wrapper"                # T012  (after T011)

# Track B — detection:
Task: "Add tryFireCrossings helper to vehicle-animator.js"     # T013
Task: "Hook tryFireCrossings into ChefMapAnimator.tick"        # T014  (after T013)
Task: "Reset lastRouteIndex on new-vehicle/route-transfer"     # T015  (after T014)

# Both tracks converge at:
Task: "Wire OnCheckpointTriggeredAsync in TransitMap.razor.cs" # T017  (after T016)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T002).
2. Complete Phase 2: Foundational (T003–T010) — **CRITICAL, blocks everything**.
3. Complete Phase 3: User Story 1 (T011–T017).
4. **STOP and VALIDATE**: Run `quickstart.md` Tests 1, 2, 3. If all three pass, US1 is shippable as a standalone demo (audio-only, no markers).
5. Demo / share.

### Incremental Delivery

1. Setup + Foundational → checkpoints load silently, no audio, no markers (sanity-checkable: `[CheckpointLoader] loaded N checkpoints` log line).
2. US1 → audio fires on crossings (MVP demo).
3. US2 → markers + pulse (visual demo; much more compelling).
4. US3 → validate edit-and-reload, finalise quickstart.
5. Polish → full sign-off.

### Parallel Team Strategy

With two developers after Phase 2:

- Developer A: US1 (the JS audio module + C# wrapper + detection wiring).
- Developer B: US2 markers (the map-interop layer additions + helper).
- Converge on `OnCheckpointTriggeredAsync` — Developer A adds the audio path (T017), Developer B adds the pulse path (T023) in sequence (small merge).

Single-developer strategy: sequential P1 → P2 → P3 → Polish.

---

## Notes

- Tests are out of scope for this POC by spec; the manual `quickstart.md` is the acceptance protocol.
- The two `AudioPlayerJsInterop` copies (`Client.Core` vs. `Client.Shared`) are an existing inconsistency — Program.cs wires the `Client.Shared` namespace. This feature follows the `Client.Shared` pattern. Cleaning up the orphan copy in `Client.Core` is out of scope and deliberately left alone.
- The pentatonic-minor scale + tonic-by-hash algorithm is the *POC* derivation. Any future melodic refinement (per-route scales, polyphony limits, swing timing) is out of scope per the spec assumption list.
- Cooldown is per `(vehicleId, checkpointId)` regardless of direction, so route-direction quirks (US1 spec edge case "reverses direction") are correctly suppressed without special-case code.
- All filesystem paths in tasks are repo-relative. The `wwwroot/checkpoints.json` deployable lives in the WebApp project; the JS/CS interop assets live in the `Client.Shared` RCL and are served from `_content/ChefKnifeStudios.TransitJazz.Client.Shared/...` at runtime.

---

## Summary

- **Total tasks**: 30 (T001–T030).
- **Per phase**: Setup 2; Foundational 8; US1 7; US2 6; US3 1; Polish 6.
- **MVP scope**: T001–T017 (17 tasks) deliver Spec User Story 1.
- **Parallelisable**: T003∥T004; T007 independent of T003–T006 in Phase 2; T011∥T012 (with sequencing care); T020/T022 in Phase 4; T025∥T026∥T027 in Phase 6.
- **No automated tests generated** (per spec — POC validates via the manual `quickstart.md`).
- **Format check**: every task uses `- [ ] T### [P?] [USx?] <description with file path>`; setup/foundational/polish tasks intentionally omit the `[USx]` label; user-story tasks all carry it.
