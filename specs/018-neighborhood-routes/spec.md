# Feature Specification: Neighborhood Routes

**Feature Branch**: `018-neighborhood-routes` (spec directory only — all work stays on the current branch per user instruction; no branch switch)
**Created**: 2026-06-14
**Status**: Draft
**Input**: User description: "docs/NEIGHBORHOOD_ROUTES_DESIGN_DOCUMENT.md — DO NOT SWITCH BRANCHES. ALL CHANGES SHOULD BE MADE ON THE CURRENT BRANCH."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pre-compute the neighborhood-to-route mapping (Priority: P1)

A developer/analyst runs an offline tool that joins Atlanta's official neighborhood boundaries against the live MARTA bus route shapes, producing a committed lean dataset that maps every neighborhood to the routes that serve it, alongside the neighborhood's key demographic signals. This lean dataset is small enough to load into an assistant's working context.

**Why this priority**: This is the core deliverable. Without the spatial join and the lean output file, none of the downstream skill consumers can answer neighborhood-route questions. The lean file alone is a viable MVP — it answers "which routes serve neighborhood X" and "which neighborhoods does route Y pass through" without any other component.

**Independent Test**: Run the tool against the source neighborhood boundary file and the route-shapes data source. Confirm a lean output file is produced containing every neighborhood, each with its matched routes (empty list when none match) and rounded demographic fields, and that the run prints a summary (total neighborhoods, count with ≥1 route, names with 0 routes, total unique routes matched).

**Acceptance Scenarios**:

1. **Given** a valid neighborhood boundary file and a reachable route-shapes data source, **When** the tool runs, **Then** a lean output file is written containing one entry per neighborhood, sorted by name ascending, each listing the routes whose shape intersects that neighborhood.
2. **Given** a neighborhood whose boundary is not crossed by any route, **When** the tool runs, **Then** that neighborhood appears in the output with an empty routes list and its name is reported in the run summary.
3. **Given** a neighborhood with one or more missing demographic values, **When** the tool runs, **Then** those fields are recorded as empty/unknown (not zero) in the lean output.
4. **Given** the route-shapes data source is unreachable, **When** the tool runs, **Then** it stops with a clear error and a non-zero result, and no partial output file is written.

---

### User Story 2 - Deep-dive demographic reference (Priority: P2)

When a developer/analyst needs the full demographic detail for a specific neighborhood (race/ethnicity, housing, home value, education, age breakdowns, etc.), they consult a separate full-detail reference file keyed by the same stable neighborhood identifier used in the lean file, without having to re-run the tool or load the full file into context speculatively.

**Why this priority**: Enriches analysis but is not required to answer the primary neighborhood-route questions. The lean file (Story 1) already carries the high-signal demographics. The full dump is the "go deeper" tier.

**Independent Test**: After running the tool, confirm a full-detail file exists, keyed by neighborhood identifier as a string, where each entry contains all source demographic properties verbatim (no renaming, no rounding), and that a lean entry's identifier resolves to the matching full-detail entry.

**Acceptance Scenarios**:

1. **Given** the tool has run successfully, **When** the full-detail file is opened, **Then** it is a dictionary keyed by each neighborhood's stable identifier rendered as a string.
2. **Given** a lean entry with a known identifier, **When** that identifier is used to look up the full-detail file, **Then** the matching full record is returned containing all original demographic property names and values.
3. **Given** any neighborhood record, **When** inspected, **Then** geometry/boundary coordinates are absent from both output files.

---

### User Story 3 - Assistant skills consume the dataset (Priority: P3)

The existing data-exploration skill and the neighborhood-blurb-authoring skill use the lean dataset to answer neighborhood-level questions and to draft neighborhood blurb copy, reading the committed files directly rather than re-running the tool.

**Why this priority**: This is the payoff for end users of the skills, but it depends entirely on Stories 1 and 2 having produced the data. It is the integration layer — valuable but last.

**Independent Test**: Pose a neighborhood-level question (e.g., "which routes serve neighborhood X") to the data-exploration skill and confirm it answers from the lean file; ask the blurb skill to draft copy for a named neighborhood and confirm it uses the lean fields (routes, transit/WFH commute rates, income, planning unit, density) as input.

**Acceptance Scenarios**:

1. **Given** the lean file is committed, **When** the data-exploration skill is asked a neighborhood-route question, **Then** it reads the lean file directly and answers without re-running the tool.
2. **Given** an analyst explicitly requests detailed demographics for one neighborhood, **When** the skill responds, **Then** it consults only that neighborhood's entry in the full-detail file and does not load the full dump speculatively.
3. **Given** a neighborhood name or identifier, **When** the blurb-authoring skill is invoked, **Then** it produces blurb copy that reflects the neighborhood's matched routes and demographic signals from the lean file.

---

### Edge Cases

- **Neighborhood boundaries made of multiple disconnected areas**: handled the same as single-area boundaries; a route intersecting any part counts as serving the neighborhood.
- **Neighborhood with zero matching routes**: included in output with an empty routes list; name surfaced in the run summary as a likely edge/rural area.
- **Missing or null demographic values**: recorded as empty/unknown, never defaulted to zero (a zero income would be misleading).
- **Route-shapes data source unreachable or returns an error**: tool aborts cleanly with a non-zero result and no partial files.
- **Route shapes change over time**: tool is re-run manually to regenerate the committed files; there is no automated/scheduled regeneration.
- **Identifier collisions between lean and full files**: the same stable neighborhood identifier must key both files consistently (string form in the full file).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The tool MUST perform an offline spatial join between Atlanta's official neighborhood boundaries and MARTA bus route shapes, matching a route to a neighborhood when the route's shape intersects the neighborhood's boundary or interior.
- **FR-002**: The tool MUST read neighborhood boundaries from a source boundary file and MUST treat that file as the authoritative source for geometry.
- **FR-003**: The tool MUST obtain current MARTA bus route shapes from the project's route-shapes data source at run time.
- **FR-004**: The tool MUST allow the operator to override the boundary file location, the route-shapes data source location, and the output destination, with sensible defaults for each.
- **FR-005**: The tool MUST produce a lean output file containing one entry per neighborhood, each with: stable identifier, name, planning unit, area, population, median household income, transit-commute percentage, drive-alone percentage, work-from-home percentage, and the list of matched routes (route identifier + human-readable short name).
- **FR-006**: The lean output MUST list neighborhoods sorted by name in ascending order, and MUST represent neighborhoods with no matched routes using an empty routes list.
- **FR-007**: The lean output MUST round percentage fields to one decimal place and income/population to the nearest whole number.
- **FR-008**: The tool MUST produce a full-detail output file containing every source demographic property for each neighborhood verbatim (no renaming, no rounding), keyed by the neighborhood's stable identifier rendered as a string.
- **FR-009**: Both output files MUST exclude geometry/boundary coordinates.
- **FR-010**: Both output files MUST record when they were generated and which source boundary file they were generated from.
- **FR-011**: Missing or null demographic values MUST be recorded as empty/unknown rather than defaulted to zero.
- **FR-012**: The lean file's stable identifier MUST resolve to the corresponding entry in the full-detail file.
- **FR-013**: On run, the tool MUST print a summary including total neighborhoods processed, count with at least one matched route, the names of neighborhoods with zero matched routes, and the total count of unique routes matched across all neighborhoods.
- **FR-014**: If the route-shapes data source is unreachable or fails, the tool MUST stop with a clear error message and a non-zero result, and MUST NOT write partial output.
- **FR-015**: The output files MUST be committed to the repository so consumers can read them directly without running the tool.
- **FR-016**: The data-exploration skill MUST be updated (via a new context document) to know the lean file exists, to read it for neighborhood-level questions, and to consult the full-detail file only for an explicitly requested single neighborhood — never loading the full dump speculatively.
- **FR-017**: The neighborhood-blurb-authoring skill MUST be updated to accept a neighborhood name or identifier, read the lean file, find the matching entry, and use its fields as structured input to draft blurb copy.
- **FR-018**: Regeneration MUST be a manual operator action; the feature MUST NOT introduce scheduled or automated regeneration, real-time processing, telemetry schema changes, a new programmatic query interface, or in-app UI.
- **FR-019**: All changes MUST be made on the current working branch; this feature MUST NOT create or switch to a new branch.

### Key Entities *(include if feature involves data)*

- **Neighborhood**: An official Atlanta neighborhood. Key attributes: stable identifier (join key), name, planning unit, area, population, median household income, commute-mode percentages (transit, drive-alone, work-from-home, plus others in full detail). Relates to zero or more Routes via spatial intersection.
- **Route**: A MARTA bus route shape. Key attributes: route identifier, human-readable short name, and a geographic line shape (used only for the join; not stored in output). Relates to zero or more Neighborhoods.
- **Lean Dataset**: The compact mapping of each Neighborhood to its matched Routes plus high-signal demographics, plus generation metadata. The default file consumed by skills.
- **Full-Detail Dataset**: The complete verbatim demographic record per Neighborhood, keyed by stable identifier (string), plus generation metadata. The deep-dive reference.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A single tool run produces both output files for every neighborhood in the source boundary file, with no neighborhood omitted.
- **SC-002**: 100% of neighborhoods with no matching route appear in the output with an empty routes list and are named in the run summary.
- **SC-003**: Any lean entry's identifier successfully resolves to its full-detail counterpart in 100% of cases.
- **SC-004**: When the route-shapes data source is unavailable, the tool exits without writing any output file, with a clear error, in 100% of such runs.
- **SC-005**: A developer/analyst can answer "which routes serve neighborhood X" and "which neighborhoods does route Y pass through" using only the lean file, without re-running the tool.
- **SC-006**: The lean file is small enough to load into an assistant's working context without exceeding typical context limits, while the full-detail file is only consulted per-neighborhood on explicit request.
- **SC-007**: Numeric fields in the lean file conform to the stated rounding (percentages to one decimal, income/population to whole numbers) in 100% of entries.

## Assumptions

- The source neighborhood boundary file covers the current set of official Atlanta neighborhoods (~248) and is provided/located by the operator at run time; coordinates are in standard geographic (longitude, latitude) form.
- The route-shapes data source returns the current MARTA bus routes (~86 at time of writing) with route identifier, short name, and a line shape per route.
- "Serves a neighborhood" is defined as the route shape intersecting the neighborhood boundary or interior; touching the boundary counts.
- The stable neighborhood identifier from the source data is unique and suitable as the join key across both files.
- Demographic field meanings (commute-mode breakdown) follow standard commute-mode ordering; the lean file surfaces the transit, drive-alone, and work-from-home subset, with the remainder available verbatim in the full-detail file.
- The two skill consumers (data-exploration and blurb-authoring) already exist and only need the new dataset and small instruction updates; no other application, server, worker, or shared code changes are needed.
- All work is performed on the current branch (`017-map-style-toggle`); the spec is filed under the `018-neighborhood-routes` directory for tracking, independent of branch name.
- The output files are committed alongside the tool so consumers never run the tool themselves.
