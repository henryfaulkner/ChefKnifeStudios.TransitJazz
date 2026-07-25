# Implementation Plan: Selectable Backfill Texture

**Branch**: `049-backfill-texture-selector` | **Date**: 2026-07-25 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/049-backfill-texture-selector/spec.md`

## Summary

Expose the soundscape's background "backfill" filler as a user-selectable choice.
Today `transit-synth.js` runs exactly one fixed background layer — a continuous
pink-noise bed on the master bus, gated by the global audio mute. This feature
generalizes that single node into a **swappable backfill layer** with two mutually
exclusive states — **Noise** (today's bed, the default) and **Percussion** (a new
sparse, humanized lo-fi kit on a `Tone.Transport` loop feeding the same master bus)
— surfaced via a **new `graphic_eq` FAB with a menu**, persisted through the existing
`Settings`/`SettingsService` local-storage mechanism (per the 2026-07-25
clarification), and re-applied on unlock so the saved choice is heard from the first
note. There is **always** a backfill; total silence remains the separate audio
mute's job. The percussion's final voice parameters are dialed in by ear first via a
new **audition mode added to `tools/instrument-compat/`** (which already reproduces
the app's exact master bus), then transcribed by hand into pinned `PERCUSSION_*`
constants. Frontend-only; no server/worker/shared changes.

## Technical Context

**Language/Version**: C# / .NET 10.0 (Blazor WASM client); JavaScript (ES module,
`transit-synth.js`) using Tone.js v15
**Primary Dependencies**: Tone.js v15 (Sampler chain + master bus, already shipped;
this feature newly uses `Tone.Transport` + `Tone.Loop` + `Tone.MembraneSynth` +
`Tone.MetalSynth`), MatBlazor (`MatFAB`/`MatMenu`/`MatList`), CommunityToolkit.Mvvm
(`ObservableObject`/`[ObservableProperty]`), Blazored.LocalStorage
(`ISyncLocalStorageService`)
**Storage**: Browser local storage — one JSON `Settings` blob under
`LocalStorageConstants.SettingsKey`, versioned by `Settings.CurrentVersion` (a single
new enum property added to the existing blob; **bump `CurrentVersion` 4 → 5**)
**Testing**: Manual audition (`tools/instrument-compat/`) for the percussion sound;
manual acceptance walkthrough per `quickstart.md`. No automated test project exists
for the client synth layer today; this feature does not introduce one (matches 047's
stance — the sound is judged by ear).
**Target Platform**: Blazor WebAssembly in the browser (desktop + mobile Safari/
Chrome); iOS-Safari gesture-gated audio unlock already handled by the existing unlock
path.
**Project Type**: Web application (client-only slice; the Blazor WASM frontend under
`src/Client/`)
**Performance Goals**: No added memory/asset cost — percussion is pure synthesis (no
samples), consistent with the app's move off soundfonts. Texture swap audible within
~one loop/beat (SC-001). Note triggers must never drop during a swap.
**Constraints**: Frontend-only (no server/worker/shared edits). Persist the enum
choice only, never live Tone.js nodes (rebuilt on reload). Exactly one backfill layer
runs while unmuted; zero while muted. Safe to call the interop before the master bus
exists (flag recorded, honored on build). English resx keys only (`.es` deferred).
**Scale/Scope**: Two textures shipped (Noise, Percussion); the enum/menu is designed
so a third (vinyl crackle, rain, …) is a one-line-per-option addition later.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The active constitution is v3.3.2. Relevant gates:

| Principle | Relevance | Status |
|---|---|---|
| **VIII. Generative Transit Music (Deterministic & Non-Authored)** | The melodic notes remain deterministic + data-derived, untouched. The backfill percussion is **not** a per-transit-event sound — it is an atmospheric decorative loop, explicitly framed as filler (spec FR-012). It does not author per-route content and does not alter the crossing/held-note system. | **PASS** — the principle governs the *transit→note* mapping; the backfill bed (like today's noise bed) sits underneath it and is out of that mapping's scope, exactly as the existing pink-noise bed already is. |
| **XI. Snappy, Reversible Overlays** | The new FAB opens a `MatMenu` (same shape as `CityFab`). No new transient overlay with custom animation is introduced. | **PASS** — reuses MatMenu; no bespoke motion. |
| **XII. Internationalized, Settings-Driven Presentation** | The FAB labels are user-facing copy. Audio is a settings-driven concern. | **PASS with noted deferral** — labels routed through `IStringLocalizer<RouteFilterResources>` (EN keys added to the single canonical `RouteFilterResources.resx`). Spanish (`.es`) is **deferred**, consistent with features 015/016/017 which shipped EN-only keys; no second resource file is introduced. The setting persists via the existing `SettingsService` (the constitution's settings-driven mandate); it is surfaced on its own FAB rather than the reflection-driven blade because the blade renders bool checkboxes only and this is an enum (`[HiddenSetting]` keeps the blade's boolean-only invariant intact). |
| **XIII. Dark-Mode Parity** | The new FAB's `.razor.css` may add color-bearing rules. | **PASS (obligation noted)** — any color-bearing CSS on `BackfillTextureFab` MUST ship both light and dark renderings in the same change (mirror the sibling FABs' CSS). MatFAB/MatMenu inherit theming from `MatThemeProvider`. |
| **VII. OpenStreetMap Cartography / III. Two-Pass Pipeline / VI. GTFS ID Mapping / others** | Not touched — no map, worker, GTFS, or SignalR change. | **N/A** |

No violations. **Complexity Tracking table not required.**

Notable simplicity choices (recorded so they are not re-litigated):
- **No event bus** for the texture choice — the FAB calls the interop directly.
  Unlike `AudioFab` (whose mute has multiple consumers, so it posts
  `AudioSettingChangedEventArgs`), nothing else reacts to the backfill texture. A
  new event-args type would be added only if a second consumer appears (YAGNI).
- **No PALETTE/percussion-snippet export** from the audition tool — final params are
  transcribed by hand into `transit-synth.js` (matches 047's no-export stance).

## Project Structure

### Documentation (this feature)

```text
specs/049-backfill-texture-selector/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── synth-engine.md          # transit-synth.js backfill layer contract
│   ├── settings-interop.md      # C# Settings enum + interop method contract
│   ├── backfill-fab.md          # BackfillTextureFab UI + persistence-honoring contract
│   └── audition-tool.md         # tools/instrument-compat/ backfill audition-mode contract
├── checklists/
│   └── requirements.md  # (from /speckit-specify)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

Frontend-only. All edits live under `src/Client/` plus the standalone audition tool
under `tools/`. Concrete touch-points (verified against the current tree):

```text
src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/
├── wwwroot/js/transit-synth.js                    # EDIT: _backfillMode/_percussion state;
│                                                  #   setBackfillTexture export; _applyBackfillLayer
│                                                  #   choke point; buildPercussion; getMasterBus +
│                                                  #   setAudioEnabled + dispose updates; export map;
│                                                  #   PERCUSSION_* constants
├── Services/JsInterop/ITransitSynthJsInterop.cs   # EDIT: + SetBackfillTextureAsync(string)
├── Services/JsInterop/TransitSynthJsInterop.cs    # EDIT: implement SetBackfillTextureAsync
├── Models/Settings.cs                             # EDIT: BackfillTexture enum + [HiddenSetting]
│                                                  #   property; CurrentVersion 4 → 5
├── Components/FABs/BackfillTextureFab.razor        # NEW: the FAB + menu
├── Components/FABs/BackfillTextureFab.razor.css    # NEW: FAB styling (light + dark)
└── Resources/RouteFilterResources.resx            # EDIT: EN menu label keys

src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/
├── Layout/MainLayout.razor                        # EDIT: mount <BackfillTextureFab />
└── Pages/TransitMap.razor.cs                      # EDIT: push persisted texture on init
                                                   #   (beside SetAudioEnabledAsync, ~line 110)

tools/instrument-compat/
├── index.html                                     # EDIT: Backfill audition mode (Noise/Percussion
│                                                  #   selector + live percussion knobs, same recipe)
└── DESIGN_DOCUMENT.md                             # EDIT: document the new audition mode

docs/DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md        # EDIT: one-line SUPERSEDED banner → this feature
```

**Structure Decision**: This is a client-only slice of the existing multi-project
web application. No new project is created. The change spans the Blazor RCL
(`Client.Shared`) for engine + interop + settings + the new FAB component, the WASM
app (`Client.WebApp`) for mounting + init, the single canonical client resx, and the
standalone `tools/instrument-compat/` audition surface. This matches the layout the
design document specifies and the file conventions already used by `AudioFab`,
`CityFab`, and the existing `SetAudioEnabledAsync` init path.

## Complexity Tracking

> No constitution violations. Table intentionally omitted.
