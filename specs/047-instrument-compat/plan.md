# Implementation Plan: Instrument Compatibility Audition Tool

**Branch**: `047-instrument-compat` | **Date**: 2026-07-25 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/047-instrument-compat/spec.md`

## Summary

A single self-contained static HTML page (`tools/instrument-compat/index.html`) that lets a developer/sound-designer audition candidate instruments for the TransitJazz soundscape — add an instrument by pasting labeled sample URLs, hear it solo through the app's exact synthesis chain, then hear it inside a synthetic Off/Low/Medium/High density stream alongside other added instruments — with no build step, no backend, and zero changes to the TransitJazz application. Technical approach is fully dictated by `tools/instrument-compat/DESIGN_DOCUMENT.md`: reproduce the app's Tone.js v15 signal chain (per-voice Filter→StereoWidener→Volume→Reverb into a shared Compressor→Filter→Destination master bus with a continuous pink-noise bed), the exact C-minor-pentatonic `SCALE` array and `noteForPosition` mapping, and the humanized velocity/timing jitter — verbatim, so the tool's output is acoustically indistinguishable from the live app.

## Technical Context

**Language/Version**: JavaScript (ES2022+, native ES modules, top-level `await`) — no TypeScript, no transpilation
**Primary Dependencies**: Tone.js v15, loaded at runtime via `import('https://esm.sh/tone@15')` (identical import site/version to the app's `transit-synth.js`) — no other runtime dependency
**Storage**: Browser `localStorage` (instrument specs, density level, mute state) under a single namespaced key
**Testing**: Manual acceptance-checklist verification (§6 of the design doc / this plan's quickstart) by opening the page in a browser; no automated test framework — this is a throwaway audition bench, not shipped product code
**Target Platform**: Modern desktop browsers with Web Audio API support (Chrome/Edge/Firefox/Safari current versions); mobile is best-effort per spec Assumptions
**Project Type**: Single static HTML file (no frontend framework, no build step) — standalone developer tool, analogous in kind to `tools/telemetry-mcp/` (its own isolated tool directory, no ties to the main .NET solution)
**Performance Goals**: High density (~7–9 crossings/sec, per real single-city MARTA telemetry — see spec.md Clarifications / research.md) must sustain smoothly on a modern desktop browser without audio glitching or UI jank; no other throughput target
**Constraints**: Must NOT autoplay audio (browser autoplay-block + iOS Safari gesture-window rule — `Tone.start()` must run synchronously inside the Enable Audio click handler, no `await` before it); must reproduce the app's DSP constants byte-for-byte (see Fidelity Notes, design doc §7); must not require CORS the tool doesn't already rely on the sample host to provide
**Scale/Scope**: Single HTML file, one page, no routing; realistically a handful to a few dozen instrument cards per session

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

This feature is **out of scope for the TransitJazz application constitution** in the same way `012-telemetry-mcp-bridge` and `013-logging-sidecar-service`'s tooling were scoped as standalone developer tools: it lives entirely under `tools/instrument-compat/`, is not part of the Blazor/WebAPI/Worker solution (Principle I), is not deployed as a Static Web App or Container Image (Principle V), has no SignalR/GTFS/spatial-reconciliation concerns (Principles III, VI), and does not touch the live map, settings blade, filtering, or localization surfaces (Principles VII, IX–XIII). It is a client-side-only audio audition bench that never runs alongside or modifies the deployed app.

The one principle that **does** bind, by direct cross-reference rather than governance scope, is:

- **Principle VIII (Generative Transit Music)**: This tool's entire purpose is to stay faithful to the app's tone system — the same `SCALE`, the same position→pitch mapping, the same per-voice/master-bus DSP chain. The tool doesn't implement Principle VIII's checkpoint/segment/held-note generation logic (it has no routes or segments), but every constant and function it reproduces (scale array, `noteForPosition`, filter/reverb/compressor values, humanization jitter) MUST match the live `transit-synth.js` values verbatim. This is a fidelity constraint inherited from the spec's FR-006/FR-007, not a new principle obligation — verified in Phase 1 by diffing constants against the design doc's §3, §7 ("Fidelity Notes — do NOT drift from the app").

No other constitution gates apply. No violations to justify — **Complexity Tracking is not needed**.

**Post-Phase-1 re-check**: Passes. Data model and quickstart introduce no new dependency, deployment surface, or architectural element beyond what's captured above; fidelity constants are enumerated verbatim in `data-model.md`.

## Project Structure

### Documentation (this feature)

```text
specs/047-instrument-compat/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command) — UI/interaction contract only, no network API
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
tools/instrument-compat/
├── DESIGN_DOCUMENT.md   # already exists — the technical ground truth this plan implements
└── index.html           # the entire tool: inline <style>, and a single <script type="module"> with
                          # the engine (getTone/getMasterBus/buildInstrument/triggerNote), the density
                          # scheduler, the add-instrument form + card rendering, and localStorage persistence
```

**Structure Decision**: Single self-contained file, no `src/`, no `tests/` directory, no build tooling of any kind — matching the design document's explicit constraint (§4: "Single file... No framework... Runs by opening the file"). This mirrors the existing precedent of `tools/telemetry-mcp/` and `tools/memory-probe/`-style standalone tool folders in this repo: each tool directory is independent of the main 11-project .NET solution and is not wired into `ChefKnifeStudios.TransitJazz.sln`, CI, or deployment. Verification is manual (open in browser, run the acceptance checklist from spec §6 / design doc §6), documented in `quickstart.md`.

## Complexity Tracking

*No constitution violations — this section is not applicable.*
