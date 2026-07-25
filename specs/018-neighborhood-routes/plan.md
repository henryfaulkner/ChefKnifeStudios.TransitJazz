# Implementation Plan: Neighborhood Routes

**Branch**: `017-map-style-toggle` (work stays on current branch per user instruction; spec/plan filed under `018-neighborhood-routes/`) | **Date**: 2026-06-14 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/018-neighborhood-routes/spec.md`

## Summary

Build an offline, manually-run developer/analyst tool — a Python script at `tools/neighborhood-routes/generate.py` — that spatially joins Atlanta's 248 official neighborhood polygons (from a City-of-Atlanta GeoJSON, passed in via CLI) against the ~86 MARTA bus route LineStrings fetched live from the MartaJazz API (`/gtfs/routes/shapes`). It emits two committed JSON files: a **lean** file (`neighborhood_routes.json`) mapping each neighborhood to its intersecting routes plus high-signal demographics (small enough for LLM context), and a **full** dump (`neighborhood_routes_full.json`) with all demographic properties verbatim, keyed by stringified objectId. Two existing skills consume the lean file: `mj-data-explorer` (via a new context doc) for neighborhood-level Q&A, and `create-neighborhood-blurb` (updated) to draft demographic-aware blurb copy. Frontend/server/worker/shared .NET code is untouched; no parquet/telemetry/MCP/UI changes. The script is re-run by hand when GTFS shapes or the GeoJSON change.

## Technical Context

**Language/Version**: Python 3.12 (3.12.10 confirmed on the dev machine; script targets 3.10+)
**Primary Dependencies**: `shapely>=2.0` (geometry + `intersects` spatial predicate), `requests>=2.28` (HTTP GET against the MJ API). Pinned in a new `tools/neighborhood-routes/requirements.txt`.
**Storage**: Two committed JSON files in `tools/neighborhood-routes/`. Source GeoJSON is read-only input, not committed (operator-supplied path). No database.
**Testing**: Manual quickstart verification (run script, assert summary counts + spot-check known neighborhoods). No automated test harness — consistent with the tool's offline, infrequently-run nature and the existing `tools/` precedent. Output correctness is verifiable by re-reading the committed JSON.
**Target Platform**: Local developer workstation (Windows dev machine; script is OS-agnostic, pure Python). Not deployed.
**Project Type**: Standalone offline CLI tool + static data artifacts + Claude skill updates. Not part of the .NET solution or its WASM/Docker deployment (mirrors `tools/telemetry-mcp` and `tools/telemetry-query-tool`).
**Performance Goals**: One-shot batch; 248 polygons × 86 LineStrings ≈ 21k intersection tests completes in seconds. No throughput target. Lean file must stay small enough to load into LLM context (target well under typical limits — a few hundred KB).
**Constraints**: Offline / manually triggered (no scheduled job). API failure → clean abort, non-zero exit, no partial files (FR-014). Geometry excluded from both outputs (FR-009). Null demographics preserved as `null`, never zero (FR-011). Lean identifier must resolve into the full file (FR-012).
**Scale/Scope**: ~248 neighborhoods, ~86 routes (as of 2026-06-14), ~130 demographic fields per neighborhood in the full dump. Two output files. Two skill touch-points + one new context doc.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The constitution (v3.1.1) governs the **TransitJazz runtime application** (the 11-project .NET solution + its MapLibre/Tone.js frontend). This feature adds neither runtime code nor a deployable; it is an offline analyst tool under `tools/` plus Claude skill content under `.claude/`. Gate evaluation:

| Principle | Applies? | Assessment |
|-----------|----------|------------|
| I. Decoupled Cloud Architecture | No | Adds no deployable unit; nothing hosted on Azure. |
| II. No Frontend Secrets | Yes (read) | Tool calls a **public, unauthenticated** GET endpoint (`/gtfs/routes/shapes`); no secret/key introduced or committed. ✅ |
| III. Two-Pass Pipeline | No | No Worker/GTFS-RT processing. |
| IV. OpenTelemetry | No | Offline script; stdout summary only, not app observability. |
| V. CI/CD Pipeline | No | No new build artifact (WASM/Docker). Output JSON committed directly, like `tools/` peers. |
| VI. GTFS ID Mapping | Yes | Routes carry both `routeId` (GTFS static id) and `routeShortName`; the lean file stores both, preserving the join-key contract. ✅ |
| VII. OSM Cartography | No | No map rendering; geometry used only for the offline join, excluded from output. |
| VIII–XI. Music / Focus / Zoom / Overlays | No | No app UI or audio. |
| XII. i18n / Settings | No (with note) | No user-facing app copy is added, so the single-`.resx` rule is not triggered. The neighborhood-blurb prose this tool feeds is authored content, not localized UI chrome. |
| Tech Stack Enforcement | Yes | Python tool is **outside** the .NET solution — it does not substitute any app technology. Precedent: `tools/telemetry-mcp` (Go) and `tools/telemetry-query-tool` (Go) are already non-.NET tools living under `tools/` without amendment. ✅ |

**Result: PASS.** No violations; no Complexity Tracking entries required. (Re-checked post-design — still PASS; see end of Phase 1.)

## Project Structure

### Documentation (this feature)

```text
specs/018-neighborhood-routes/
├── plan.md              # This file (/speckit.plan output)
├── spec.md              # Feature spec (/speckit.specify output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── cli.md                     # generate.py CLI contract (args, exit codes, stdout summary)
│   ├── route-shapes-input.md      # consumed shape of GET /gtfs/routes/shapes
│   ├── lean-output.schema.md      # neighborhood_routes.json contract
│   └── full-output.schema.md      # neighborhood_routes_full.json contract
└── checklists/
    └── requirements.md  # spec quality checklist (already created)
```

### Source Code (repository root)

```text
tools/
└── neighborhood-routes/                 # NEW — the offline tool + its committed outputs
    ├── generate.py                      # spatial-join script (header comment: manual re-run trigger)
    ├── requirements.txt                 # shapely>=2.0, requests>=2.28
    ├── neighborhood_routes.json         # lean output (committed)
    └── neighborhood_routes_full.json    # full demographic dump (committed)

.claude/
└── skills/
    ├── mj-data-explorer/
    │   ├── SKILL.md                     # MODIFIED — add a routing row for neighborhood questions
    │   └── references/
    │       └── neighborhood-routes-context.md   # NEW — how/when to read the lean & full files
    └── create-neighborhood-blurb/
        └── SKILL.md                     # MODIFIED — accept name/objectId, read lean file, demographic-aware draft
```

**Structure Decision**: Single standalone tool directory `tools/neighborhood-routes/` (new sibling to `tools/telemetry-mcp` and `tools/telemetry-query-tool`), plus in-place edits to two existing Claude skills under `.claude/skills/`. No changes anywhere in `src/` (the .NET solution). The design doc's outline placed the new mj-data-explorer doc at the skill root; this plan files it under that skill's existing `references/` subfolder to match the skill's established layout (it already keeps `telemetry-schema.md`, `mj-api-schema.md`, etc. there) and references it from `SKILL.md` like the other reference docs.

## Complexity Tracking

> No constitution violations — table intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| (none) | — | — |
