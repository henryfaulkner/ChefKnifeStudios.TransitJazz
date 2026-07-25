# Phase 0 Research: Neighborhood Routes

All technical choices are pinned by `docs/NEIGHBORHOOD_ROUTES_DESIGN_DOCUMENT.md`. There are **no open NEEDS CLARIFICATION items** from Technical Context. This document records the load-bearing decisions and the few judgment calls the plan resolves.

---

## D1. Language & libraries: Python 3.12 + shapely + requests

- **Decision**: Implement `generate.py` in Python (3.10+; 3.12.10 on the dev box) using `shapely>=2.0` for geometry/intersection and `requests>=2.28` for the HTTP fetch. Pin both in `requirements.txt`.
- **Rationale**: The design doc mandates exactly this stack. shapely's `geometry.shape()` + `.intersects()` is the standard, correct primitive for polygon-vs-linestring spatial joins and transparently handles `Polygon` and `MultiPolygon`. `requests` is the simplest reliable HTTP client. This is an offline analyst tool outside the .NET solution, so the .NET tech-stack constraint does not apply (precedent: `tools/telemetry-mcp` and `tools/telemetry-query-tool` are Go).
- **Alternatives considered**: GeoPandas (heavier dependency, sjoin overkill for 248×86); raw `pyproj`/manual math (reinvents shapely); doing the join in .NET inside the app (rejected — this is explicitly NOT an in-app feature, and would pull GIS into the runtime).

## D2. Spatial-join semantic: `polygon.intersects(linestring)`

- **Decision**: A route "serves" a neighborhood iff `neighborhood_polygon.intersects(route_linestring)` is `True`. No buffering, no length threshold.
- **Rationale**: `intersects` returns `True` when any part of the line touches the boundary or interior — the natural meaning of "the route passes through this neighborhood." Matches the design doc §4.3 note verbatim.
- **Alternatives considered**: `contains` (too strict — would drop routes that merely cross a corner); `within` (wrong direction); buffered intersection / minimum-overlap-length (adds tuning knobs the spec doesn't ask for, and risks dropping legitimate edge-crossing routes).

## D3. WGS84 / planar intersection (no reprojection)

- **Decision**: Run `intersects` directly on raw WGS84 lon/lat coordinates from both inputs; do not reproject to a metric CRS.
- **Rationale**: `intersects` is a topological predicate (does/does-not touch), not a measurement — its boolean result is invariant to the planar-vs-geographic distinction at city scale. Both inputs are already WGS84 `(lon, lat)` (GeoJSON spec + the API), so they share a coordinate frame and no transform is needed. Reprojection would only matter if we measured distance/area (we don't).
- **Alternatives considered**: Reproject to EPSG:2240 (Georgia State Plane) — unnecessary complexity for a boolean predicate; adds a `pyproj` dependency.

## D4. Stable join key: `OBJECTID_1` → `objectId`

- **Decision**: Use the GeoJSON `OBJECTID_1` integer as the primary key. Lean file stores it as integer `objectId`; full file keys its dictionary by `str(objectId)`. Lookup contract: `full["neighborhoods"][str(lean["objectId"])]`.
- **Rationale**: Design doc §2.1/§3 specify this. `OBJECTID_1` is a stable integer; JSON object keys must be strings, hence the stringification in the full dump (FR-008, FR-012).
- **Alternatives considered**: Keying by `NAME` (names can collide or change; not guaranteed unique); a synthesized hash (pointless — a stable id already exists).

## D5. Null handling: preserve `null`, never coerce to 0

- **Decision**: Missing/empty demographic values are emitted as JSON `null` in the lean file and stored as-is (`null`) in the full file. Never default to `0`.
- **Rationale**: FR-011 / design doc §4.4 — a `0` median income reads as a real (and misleading) data point. `null` is honest "unknown." Rounding logic (D6) must null-guard before rounding.
- **Alternatives considered**: Omitting the key entirely (breaks the fixed lean schema consumers expect); `0`/`-1` sentinels (misleading or magic).

## D6. Rounding rules (lean only)

- **Decision**: In the lean file, round percentage fields (`transitCommutePercent`, `carAlonePercent`, `workFromHomePercent`) to 1 decimal place; round `population` and `medianHouseholdIncome` to the nearest integer. The full file stores values verbatim (no rounding). Guard `None` before rounding.
- **Rationale**: FR-007 / design doc §3.1. The lean file is for fast human/LLM reading; the full file is the precise source of record.
- **Alternatives considered**: No rounding in lean (noisier, larger); rounding the full file too (loses precision the deep-dive tier exists to provide).

## D7. Commute-field decode (which raw fields map to lean fields)

- **Decision**: Per design doc §5 (ACS commute-mode ordering):
  - `commute__1` → `carAlonePercent` (drove alone)
  - `commute__3` → `transitCommutePercent` (public transportation)
  - `commute__5` → `workFromHomePercent` (worked from home)
  - `householdi` → `medianHouseholdIncome`; `population` → `population`; `SQMILES` → `sqMiles`; `NPU` → `npu`; `NAME` → `name`.
- **Rationale**: These are the high-signal fields the blurb skill keys off (transit dependency, WFH, income). Values are percentages 0–100.
- **Caveat / verify-at-runtime**: The commute-field meanings are inferred from ACS ordering, not a labelled schema. The implementer SHOULD spot-check 2–3 known neighborhoods (the doc cites Ridgewood Heights, Vine City) to confirm the mapping before trusting the lean percentages. The full dump carries every `commute__*` verbatim, so no information is lost if a lean label is later corrected.

## D8. API fetch + failure semantics

- **Decision**: `GET {api}/gtfs/routes/shapes`, parse JSON array of `RouteShapeFeature`. On any failure (network error, non-2xx, unparseable body, empty array), print a clear error to stderr and exit non-zero **before** writing any output file (FR-014). Use a generous timeout (≥30s; the endpoint is an Azure Container App that may cold-start).
- **Rationale**: Partial/empty output would silently corrupt the committed dataset that skills trust. Fail loud, write nothing.
- **Verify-at-runtime**: The endpoint is public/unauthenticated per the design doc. A live probe during this planning session could not be completed in the non-interactive shell; the implementer MUST confirm the live response shape (array vs. `{features:[...]}`, exact property names `routeId`/`routeShortName`) against `contracts/route-shapes-input.md` on first run. The parser should be written to fail clearly if the shape differs, rather than silently producing empty matches.
- **Alternatives considered**: Caching the API response to disk (out of scope — manual re-run is the refresh mechanism); retem retries/backoff (nice-to-have, not required; a clear failure is acceptable for a manual tool).

## D9. Output ordering & atomicity

- **Decision**: Lean `neighborhoods` is a flat **array sorted by `name` ascending** (FR-006). Full `neighborhoods` is a **dict keyed by `str(objectId)`** (insertion order irrelevant). Both files get `generatedAt` (UTC ISO-8601) and `sourceGeoJson` (basename of the input path) metadata (FR-010). Write both files only after the join fully succeeds (supports FR-014's "no partial output").
- **Rationale**: Deterministic, diff-friendly committed artifacts; sorted lean array is human-scannable.
- **Alternatives considered**: Sorting lean by objectId (less human-friendly); streaming writes (unnecessary at this scale and weakens the all-or-nothing guarantee).

## D10. mj-data-explorer context-file placement

- **Decision**: Place the new context doc at `.claude/skills/mj-data-explorer/references/neighborhood-routes-context.md` (not the skill root as the design doc loosely sketched) and add one routing row + a "Files in this skill" entry in `SKILL.md`.
- **Rationale**: The skill already centralizes its loaded docs under `references/` (`telemetry-schema.md`, `mj-api-schema.md`, …) and routes to them from a table in `SKILL.md`. Following that established pattern keeps the skill coherent; the doc's root-level suggestion predates knowing the skill's actual layout.
- **Alternatives considered**: Skill root (inconsistent with existing structure); embedding the guidance inline in `SKILL.md` (bloats the always-loaded router; the data guidance is reference material best loaded on demand).

## D11. create-neighborhood-blurb: reconciling two blurb concepts (judgment call)

- **Context**: The **existing** `create-neighborhood-blurb` skill authors *sonic-character* prose (feature 011) — second-person, present-tense writing about the instruments a neighborhood is hearing — and writes it into the 011 data file's `blurb` property. The **design doc** (§6.2) instead proposes a *demographic/transit* `NeighborhoodBlurb` record (`ToneDescription` + `Significance` + `IsPlaceholder`) drafted from income/commute/route signals.
- **Decision**: Treat the design doc's §6.2 as an **additive demographic-context capability**, not a replacement of the skill's voice. The skill update will: (1) accept a neighborhood name or `objectId`; (2) read `tools/neighborhood-routes/neighborhood_routes.json` and find the matching lean entry; (3) surface the demographic/route signals (routes & shortNames, `transitCommutePercent`, `workFromHomePercent`, `medianHouseholdIncome`, `npu`, density from `population`+`sqMiles`) as **structured input that informs** the prose. The skill's existing target voice, length (2–3 sentences), and the rule that sonic claims must match actual assigned `voices` remain authoritative. The `NeighborhoodBlurb` C# record in the doc is illustrative of the *signals to use*, not a new code artifact to emit (no .NET code changes are in scope per FR-018).
- **Rationale**: FR-017 says "use its fields as structured input to draft blurb copy" — input, not a rewrite of the output format. Replacing the established, user-approved voice with a demographic readout would contradict the existing skill's own consistency rules and the 011 spec scope it cites. Surfacing demographics as *context the author may draw on* satisfies the design doc without breaking the skill.
- **Verify-with-user-if-needed**: If the user actually wants a *new, separate* demographic blurb artifact (distinct from the 011 sonic blurb), that is a larger change — flag it during `/speckit.tasks` rather than silently overwriting the existing skill's purpose.
- **Alternatives considered**: Full replacement of the skill with the demographic `NeighborhoodBlurb` flow (rejected — destroys existing approved behavior and 011 integration); a brand-new third skill (rejected — design doc §6.2 explicitly says "update the existing skill").

## D12. No tests harness / verification approach

- **Decision**: No automated test framework. Verification is the quickstart: run the script against a real GeoJSON + live API, assert the printed summary (totals, zero-route names, unique routes) is sane, and spot-check 2–3 known neighborhoods in the lean file + an `objectId` round-trip into the full file.
- **Rationale**: Offline, infrequently-run tool whose output is a committed, human-inspectable artifact; matches the `tools/` precedent (those tools have quickstarts, not unit suites). Investing in a Python test harness for a single batch script is disproportionate.
- **Alternatives considered**: pytest with a synthetic mini-GeoJSON + mocked API (reasonable but heavier than warranted; can be added later if the join logic grows).
