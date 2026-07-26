---
description: "Task list for Selectable Backfill Texture (049)"
---

# Tasks: Selectable Backfill Texture

**Input**: Design documents from `/specs/049-backfill-texture-selector/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: No automated tests. Per plan.md, there is no client synth-layer test project
and the sound is judged by ear (matches feature 047). Verification is the manual
`quickstart.md` walkthrough (audition + D1–D10 acceptance table). No test tasks are
generated.

**Organization**: Tasks are grouped by user story. Because all three stories ride the
**same** shared engine + settings + interop + FAB, that shared plumbing lives in the
Foundational phase; each user-story phase then adds/verifies only the slice that makes
that story independently demonstrable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3 — user-story phase tasks only
- All paths are repo-relative from `C:\Projects\ChefKnifeStudios.TransitJazz`

## Path Conventions

Frontend-only Blazor WASM slice. Shared code lives in
`src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/`; the WASM app in
`src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/`; the audition tool in
`tools/instrument-compat/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Dial in the percussion sound BEFORE any app code hardcodes its values, and
close out the superseded doc. This de-risks the one novel surface (the `Tone.Transport`
loop) per research R4/R5 and contract `audition-tool.md`.

- [ ] T001 Add the "Backfill" audition section (Noise/Percussion selector + `buildPercussion` recipe wired to the tool's existing `getMasterBus()`, `muted` fire-time re-check, and Enable-Audio unlock) to `tools/instrument-compat/index.html` per contracts/audition-tool.md (AUD-1..AUD-3, AUD-5)
- [ ] T002 Add live percussion knobs (loop interval `1n`/`2n`/`4n`, kick tuning+decay+volume, rim volume+probability, overall `PERCUSSION_VOLUME_DB`) and persist `backfill` + `percussionParams` in the tool's existing localStorage session in `tools/instrument-compat/index.html` (AUD-2, AUD-4)
- [ ] T003 Audition the kit by ear underneath a simulated soundscape (load real instruments + run the density sim) and record the final percussion parameter values per quickstart.md §A / SC-006 (AUD-6 — output feeds T007)
- [ ] T004 [P] Document the new Backfill audition mode in `tools/instrument-compat/DESIGN_DOCUMENT.md` (AUD-7)
- [ ] T005 [P] Add a one-line "SUPERSEDED by specs/049-backfill-texture-selector" banner to the top of `docs/DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md` per research R7 / plan §Scope

**Checkpoint**: Final `PERCUSSION_*` values are known; superseded doc marked.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared engine + settings + interop + FAB plumbing every user story
depends on. Ordered so the JS engine and the persisted model exist before the C# that
drives and persists them.

**⚠️ CRITICAL**: No user story is demonstrable until this phase is complete.

### JS engine — `transit-synth.js` (single file; tasks sequential, same file)

- [ ] T006 Add `PERCUSSION_*` constants (from T003) grouped with the existing `NOISE_*` constants (~L204), plus module state `let _backfillMode = 'noise';` and `let _percussion = null;` in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/transit-synth.js` per contracts/synth-engine.md
- [ ] T007 Add `buildPercussion(T)` (MembraneSynth kick + MetalSynth rim → per-voice filter/volume → `Tone.Volume(PERCUSSION_VOLUME_DB)` → `getMasterBus(T).input`, humanized, on a slow `Tone.Loop`) in `transit-synth.js` per contracts/synth-engine.md
- [ ] T008 Add the `_applyBackfillLayer()` choke point reconciling `_audioEnabled × _backfillMode` → exactly one running layer (muted → stop both; enabled+noise → noise only; enabled+percussion → build-lazy + percussion only) in `transit-synth.js` (INV-2, data-model running-state table)
- [ ] T009 Add the `export function setBackfillTexture(mode)` (normalize to `'noise'`/`'percussion'`; early-return recording the flag if `!_masterBus`; else defensive context-resume + `_applyBackfillLayer()`) in `transit-synth.js` (INV-4)
- [ ] T010 Update `getMasterBus` to call `_applyBackfillLayer()` after wiring the noise node (replacing `if (_audioEnabled) noise.start();` at ~L270) so first build honors the persisted mode, in `transit-synth.js` (INV-1)
- [ ] T011 Update `setAudioEnabled` to route through `_applyBackfillLayer()` (mute stops BOTH layers; unmute restarts WHICHEVER `_backfillMode` selects) in `transit-synth.js` (INV-3)
- [ ] T012 Update `dispose` to tear down `_percussion` (loop + kick + rim + volume, then null it) and add `setBackfillTexture` to the `window.TransitSynth = { … }` export map in `transit-synth.js`

### C# settings + interop

- [ ] T013 [P] Add `public enum BackfillTexture { Noise, Percussion }`, the `[ObservableProperty] [HiddenSetting] BackfillTexture _backfillTexture = BackfillTexture.Noise;` property, and bump `CurrentVersion` 4 → 5 in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Models/Settings.cs` per contracts/settings-interop.md (SETT-1..SETT-3)
- [ ] T014 [P] Add `Task SetBackfillTextureAsync(string mode);` to `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Services/JsInterop/ITransitSynthJsInterop.cs` (INT-1) with an XML-doc summary matching the SetAudioEnabledAsync style
- [ ] T015 Implement `SetBackfillTextureAsync` (invoke `setBackfillTexture` on the shared `_moduleTask` instance; try/catch + `LogError`) in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Services/JsInterop/TransitSynthJsInterop.cs` per contracts/settings-interop.md (INT-2, INT-3) — depends on T014
- [ ] T016 [P] Add EN resource keys `BackfillNoise` ("Ambient noise") and `BackfillPercussion` ("Lo-fi percussion") to `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Resources/RouteFilterResources.resx` (RES-1)

### FAB component + mount

- [ ] T017 Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/FABs/BackfillTextureFab.razor` (`graphic_eq` MatFAB + MatMenu list; reads persisted `BackfillTexture` in `OnInitialized`; active option `Disabled`; on `Select` → `SettingsService.SetSettingValue(nameof(Settings.BackfillTexture), mode)` then `TransitSynth.SetBackfillTextureAsync(mode.ToString().ToLowerInvariant())`; labels via `IStringLocalizer<RouteFilterResources>`; NO event bus) per contracts/backfill-fab.md (FAB-1..FAB-4) — depends on T013, T015, T016
- [ ] T018 [P] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/FABs/BackfillTextureFab.razor.css` mirroring a sibling FAB's container styling, with BOTH light and dark renderings for every color-bearing rule (Principle XIII; FAB-5)
- [ ] T019 Mount `<BackfillTextureFab />` inside the `<MatThemeProvider>` block alongside the existing FABs in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Layout/MainLayout.razor` (MOUNT-1) — depends on T017

**Checkpoint**: Engine, settings, interop, and a mounted FAB exist. User-story slices can now be verified.

---

## Phase 3: User Story 1 — Switch the background texture live (Priority: P1) 🎯 MVP

**Goal**: With audio playing, selecting a texture in the FAB swaps the background layer
live while melodic notes continue.

**Independent Test**: quickstart.md D2/D3/D4 — open the FAB, switch Noise↔Percussion,
confirm the background changes within ~one loop/beat, notes never drop, active option
is disabled, only one texture ever audible.

**Note**: The live-swap path is fully realized by the Foundational plumbing (FAB
`Select` → interop → `_applyBackfillLayer`). This phase verifies the P1 slice end-to-end.

- [ ] T020 [US1] Verify D2 (select Lo-fi percussion → background swaps within ~one loop/beat, melodic notes keep playing) and D3 (active option disabled, re-select is a no-op) per quickstart.md — FR-004, FR-011, SC-001
- [ ] T021 [US1] Verify D4 + D8 (switch back to Ambient noise stops percussion; rapid Noise↔Percussion toggling always converges to exactly one running texture, never doubled/silent) per quickstart.md — FR-010, SC-005

**Checkpoint**: US1 (MVP) fully functional and independently demonstrable.

---

## Phase 4: User Story 2 — Remembered texture choice on return (Priority: P2)

**Goal**: A previously-chosen texture is heard from the first unlock after a reload, with
no re-selection; a fresh profile hears the default; old-version settings fall back cleanly.

**Independent Test**: quickstart.md D1/D7/D10.

- [ ] T022 [US2] Push the persisted texture to JS on startup: add `_ = TransitSynth.SetBackfillTextureAsync(settings.BackfillTexture.ToString().ToLowerInvariant());` beside the existing `SetAudioEnabledAsync` call (~L110) in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs` per contracts/backfill-fab.md (INIT-1, INIT-2)
- [ ] T023 [US2] Verify D7 (select percussion → reload → unlock → percussion plays from first unlock, no re-selection) and D1 (fresh profile hears the default ambient noise) per quickstart.md — FR-005, FR-006, SC-002, SC-003
- [ ] T024 [US2] Verify D10 (simulate a Version-4 saved blob → load → settings fall back to defaults cleanly, backfill = Noise, no error) per quickstart.md — US2 scenario 3 (relies on the T013 CurrentVersion 4→5 guard)

**Checkpoint**: US1 and US2 both work independently.

---

## Phase 5: User Story 3 — Texture is subordinate to the master mute (Priority: P2)

**Goal**: Muting yields total silence regardless of selected texture; unmuting restores
both notes and the selected texture; the last selection wins after a muted switch.

**Independent Test**: quickstart.md D5/D6/D9.

- [ ] T025 [US3] Verify D5 (percussion selected + mute → total silence: no notes, no percussion) and D6 (unmute → both notes and percussion resume, not noise) per quickstart.md — FR-008, FR-009, SC-004 (relies on T011 routing setAudioEnabled through the choke point)
- [ ] T026 [US3] Verify D9 (mute → long idle → switch texture while muted → unmute → last-selected texture plays, context resumed so it actually sounds) per quickstart.md — edge: long-idle switch; FR-009 (relies on the T009 defensive context-resume)

**Checkpoint**: All three stories independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T027 [P] Run the constitution spot-checks in quickstart.md §E: FAB labels sourced from resx (XII, `.es` intentionally absent), FAB correct in both light + dark themes via DarkModeFab (XIII), melodic crossing/held notes unchanged (VIII)
- [ ] T028 Full quickstart.md §D pass (D1–D10) on the built WASM client as the final acceptance gate; confirm `PERCUSSION_*` hold the audition-approved values from T003, not the design-doc placeholders

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately. T003 (audition output) gates T006.
- **Foundational (Phase 2)**: Depends on Setup (needs the final `PERCUSSION_*` values). BLOCKS all user stories.
- **User Stories (Phase 3–5)**: All depend on Foundational completion.
  - US1 (P1) needs only Phase 2.
  - US2 (P2) adds T022 (init push) on top of Phase 2.
  - US3 (P2) needs only Phase 2 (verification of the mute-composition already built in T008/T011).
- **Polish (Phase 6)**: After all desired stories complete.

### Key task-level dependencies

- T003 → T006 (audition values pin the constants)
- T006 → T007 → T008 → T009 → T010 → T011 → T012 (all edit `transit-synth.js`; sequential, same file)
- T014 → T015 (interface before implementation)
- T013 + T015 + T016 → T017 (FAB needs the enum, interop, and resx keys)
- T017 → T019 (mount needs the component)
- T022 depends on T013 + T015 (persisted property + interop)

### Within each user story

- US1/US2/US3 phases are primarily verification of the shared plumbing, plus T022 (US2's init push). No cross-story code dependencies — each is independently demonstrable once Phase 2 (and T022 for US2) is done.

### Parallel Opportunities

- **Phase 1**: T004 and T005 are [P] (different files: DESIGN_DOCUMENT.md, DRUMKIT doc).
- **Phase 2**: The C# trio T013 / T014 / T016 are [P] (Settings.cs, ITransitSynthJsInterop.cs, resx — all different files) and can run alongside the JS engine chain T006–T012. T018 (.razor.css) is [P] with the T017 .razor authoring.
- **Phase 6**: T027 is [P] with final prep.

---

## Parallel Example: Phase 2 Foundational

```text
# JS engine chain (sequential — one file) runs while the C# [P] tasks proceed in parallel:
Task: T013 Add BackfillTexture enum + [HiddenSetting] property + CurrentVersion 4→5 in Settings.cs
Task: T014 Add SetBackfillTextureAsync to ITransitSynthJsInterop.cs
Task: T016 Add EN resx keys BackfillNoise / BackfillPercussion in RouteFilterResources.resx
# Then converge: T015 (interop impl) after T014; T017 (FAB) after T013+T015+T016; T019 after T017.
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1: Setup (audition → pin `PERCUSSION_*`; mark superseded doc).
2. Phase 2: Foundational (engine + settings + interop + FAB) — CRITICAL, blocks all stories.
3. Phase 3: US1 verification (D2/D3/D4/D8).
4. **STOP and VALIDATE**: live texture swap works with notes uninterrupted.
5. Demo the MVP.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 (live swap) → validate → demo (MVP).
3. US2 (add T022 init push) → validate persistence-on-reload → demo.
4. US3 (verify mute composition) → validate → demo.
5. Polish (constitution spot-checks + full D1–D10 gate).

---

## Notes

- No automated tests (by design — sound judged by ear; no client synth test project). Verification = quickstart.md.
- T003's audition output is a hard input to T006 — do NOT ship the design-doc placeholder `PERCUSSION_*` values as final.
- Every `transit-synth.js` task (T006–T012) touches the same file → keep sequential; never parallelize among themselves.
- [P] = different files, no incomplete-task dependency.
- Commit after each task or logical group (per user's standing preference, commits are user-initiated).
- The FAB posts NO event (YAGNI) — do not add `IEventNotificationService` wiring unless a second consumer appears.
