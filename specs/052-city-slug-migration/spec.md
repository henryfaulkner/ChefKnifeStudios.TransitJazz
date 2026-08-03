# Feature Specification: City Slug Migration

**Feature Branch**: `052-city-slug-migration`
**Created**: 2026-08-02
**Status**: Draft
**Input**: User description: "docs\CITY_SLUG_MIGRATION_ASSESSMENT.md"

## Overview

The app identifies each city by a single token carried in the URL fragment (`#marta`).
That same literal string is reused, unvalidated, as the realtime group name, the API
query parameter, the configuration key, the route-shape store prefix, and the analytics
pageview path. Today those tokens are **transit-agency names**. This feature renames all
seven to **city names**.

This is **step one of two**. Multi-agency-per-city composition is the destination and is
deliberately **out of scope** here (see Out of Scope). The rename is a prerequisite, not
cleanup: an agency-named token encodes a 1:1 city-to-agency assumption that step two
deliberately breaks. Once a city hosts several authorities, a token naming one
participant cannot honestly address the whole.

**Slug rule (settled, applies to all seven and to every future city):** full city name,
lowercase, hyphen-separated, with a region suffix **only** where needed to disambiguate.

| Current | New |
|---|---|
| `marta` | `atlanta` |
| `wmata` | `washington-dc` |
| `mbta` | `boston` |
| `nymta` | `new-york-city` |
| `ttc` | `toronto` |
| `septa` | `philadelphia` |
| `rtd` | `denver` |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A visitor reaches a city and hears it (Priority: P1)

A visitor opens a link ending in `#atlanta`. The map centers on Atlanta, vehicles appear
and move, and crossings produce audio — exactly as `#marta` did before. Every one of the
seven cities behaves this way under its new name.

**Why this priority**: This is the entire feature. If a renamed city does not receive
live vehicles, the rename has broken the product. The realtime group name is the highest
risk boundary because a mismatch fails *silently* — a connected client that receives
nothing, with no error shown anywhere.

**Independent Test**: Load each of the seven new slugs in turn and confirm vehicles
render and move, and that audio triggers on crossings. Fully independent of Stories 2
and 3.

**Acceptance Scenarios**:

1. **Given** a visitor opens `#atlanta`, **When** the map loads, **Then** the map centers
   on Atlanta and live vehicles appear and move within the normal startup interval.
2. **Given** any of the seven new slugs, **When** the client subscribes to live updates,
   **Then** it joins the group matching that slug and the publisher publishes to that same
   group — verified by observing vehicles actually arrive, not merely that no error appears.
3. **Given** a visitor is on a renamed city, **When** a vehicle crosses a trigger point,
   **Then** audio plays as it did under the old slug.
4. **Given** any renamed city, **When** route shapes are requested, **Then** the correct
   city's routes are returned and drawn.
5. **Given** the city picker, **When** the visitor selects a different city, **Then** the
   address bar shows the new city slug and that city loads.

---

### User Story 2 - Each city reads as itself (Priority: P2)

A visitor sees the city's name and its own introductory copy — the audio-unlock text and
the info panel describe *that* city's transit. No visitor-facing surface shows an agency
token where a city name belongs.

**Why this priority**: Correctness of presentation, independent of whether data flows.
A city could stream vehicles perfectly and still show the wrong copy.

**Independent Test**: Visit each new slug and confirm the audio-unlock overlay, the info
panel, and the picker all show that city's correct copy.

**Acceptance Scenarios**:

1. **Given** a visitor opens any renamed city, **When** the audio-unlock overlay appears,
   **Then** it shows that city's copy, not another city's and not a blank/missing string.
2. **Given** a visitor opens the info panel on any renamed city, **When** it renders,
   **Then** it describes that city's transit modes.
3. **Given** the city picker is open, **When** the visitor reads it, **Then** each entry is
   labeled by its city and selecting it navigates to that city's new slug.

---

### User Story 3 - Operators can still interpret telemetry (Priority: P3)

An operator querying historical telemetry gets one continuous, unsplit history per city
across the migration date.

**Why this priority**: Real, but no visitor sees it, and the chosen approach (leave the
telemetry identifier untouched) makes this a *verification* task rather than a build task.

**Independent Test**: Query telemetry spanning the cutover date and confirm each city's
history is continuous, with no gap or duplicate-identity split at the migration boundary.

**Acceptance Scenarios**:

1. **Given** telemetry spanning the cutover, **When** an operator queries one city's
   history, **Then** results are continuous across the migration date with no split.
2. **Given** the migration has shipped, **When** new telemetry is written, **Then** its
   city identifier is unchanged from pre-migration values.
3. **Given** the 051 Phase 3 baseline window, **When** this migration ships, **Then** the
   accumulated baseline remains valid and uninterrupted.

---

### Edge Cases

- **A stale bookmarked link (`#wmata`, `#nymta`, …) is opened.** Legacy slugs are **not**
  aliased (explicit decision). Unrecognized fragments hit the existing silent fallback, so
  the visitor lands in the **default city rather than the one they linked to, with no
  error**. This is a known, accepted consequence — see Assumptions. `#marta` lands in
  Atlanta only because Atlanta *is* the fallback default, which is coincidence, not
  aliasing.
- **An outdated client meets an updated server during the multi-lane deploy.** The join
  step is version-gated, so a stale client **fails loudly at join** instead of connecting
  and silently receiving nothing.
- **The two configuration files disagree.** The city list exists in two places that must
  match exactly; a one-sided edit means one side publishes to a group nobody joined. Must
  be detectable rather than silent.
- **A city is renamed in one boundary but not another** (e.g. picker updated, config not).
  Produces a working-looking app with an empty map.
- **A visitor is mid-session when the update lands.** Their live connection is against the
  old group name; they must recover on reconnect or refresh rather than sit silently empty.
- **Fragment casing/whitespace** (`#Atlanta`, `#ATLANTA`) must resolve identically to
  `#atlanta`, matching today's normalization.
- **The route-shape store retains old-prefixed keys.** It is rebuilt from source on
  startup and is not durable, so a deploy re-derives it — but a mixed-prefix state must
  not survive a restart.

## Requirements *(mandatory)*

### Functional Requirements

**Identity and format**

- **FR-001**: The system MUST identify all seven cities by the city slugs in the Overview
  table.
- **FR-002**: The slug rule (full city name, lowercase, hyphen-separated, region suffix
  only to disambiguate) MUST be recorded as the governing rule for all future cities.
- **FR-003**: The autonomous city-discovery process, which mints slugs for new cities
  without human input, MUST follow the same rule.
- **FR-004**: Every boundary that carries the city token — URL fragment, realtime group
  name, API parameter, configuration key, route-shape prefix, analytics pageview — MUST
  carry the new slug, with no boundary left on the old value except where explicitly
  excluded by FR-016.

**Consistency**

- **FR-005**: A single source of truth MUST define the valid city slugs; visitor-facing
  surfaces MUST NOT hardcode slug literals independently of it.
- **FR-006**: The two configuration files MUST contain byte-identical city lists, and a
  mismatch MUST be detectable by an automated check rather than discovered at runtime.
- **FR-007**: The system MUST NOT retain any unreferenced agency-named slug in a position
  where it could still be resolved as a live city identity.

**Realtime continuity**

- **FR-008**: A client on a given city MUST receive exactly the live vehicle stream
  published for that city.
- **FR-009**: The join step MUST be version-gated so that a client built against the old
  slugs **fails visibly** when joining an updated server, rather than connecting
  successfully and receiving nothing.
- **FR-010**: The failure in FR-009 MUST be observable to the operator (logged/surfaced),
  not silent.
- **FR-011**: A client whose connection was established before the update MUST recover on
  reconnect or refresh rather than remain silently empty.

**Presentation**

- **FR-012**: Each city's introductory copy (audio-unlock overlay, info panel) MUST resolve
  to that city's text, with no missing or fallback-blank strings.
- **FR-013**: The city picker MUST navigate to the new slugs and label each entry by city.
- **FR-014**: Each city's map origin MUST be unchanged by the rename.
- **FR-015**: No visitor-facing surface MUST display an agency token as the city's identity.

**Telemetry**

- **FR-016**: The telemetry city identifier MUST remain on its existing agency values;
  this migration MUST NOT rewrite, split, or dual-write it.
- **FR-017**: Historical telemetry MUST remain queryable as one continuous series per city
  across the cutover date.
- **FR-018**: The mapping from agency-valued telemetry to the new city slugs MUST be
  documented so the discrepancy is intentional and legible, not an inconsistency.
- **FR-019**: The telemetry query allow-list MUST remain valid and unchanged.

**Rollout**

- **FR-020**: The rename MUST be deployable across the multiple independent deploy lanes
  without a window in which a city silently receives no vehicles.
- **FR-021**: The rollout order and its verification steps MUST be documented before
  cutover.
- **FR-022**: Every city MUST be verified as live-and-audible after cutover, not assumed.
- **FR-023**: Migration MUST NOT begin until the 051 Phase 3 baseline window has closed.

### Key Entities

- **City Slug**: The permanent public identifier for a city — simultaneously the shared
  URL fragment, realtime group name, API parameter, and configuration key. Expensive to
  change once published.
- **City Registry**: The authoritative set of valid slugs, and the only place slug literals
  should originate.
- **City Configuration Entry**: One city's operational settings, keyed by slug, duplicated
  across two files that must agree exactly.
- **City Copy Set**: The per-city introductory text, currently keyed by agency-prefixed
  names.
- **Telemetry City Value**: The city identifier in immutable historical records.
  Deliberately **diverges** from City Slug after this feature (FR-016).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All 7 cities load at their new slugs and show moving vehicles within the
  normal startup interval — 7 of 7, verified individually.
- **SC-002**: All 7 cities produce audio on vehicle crossings after the rename.
- **SC-003**: Zero cities experience a silent no-data state at any point during rollout.
- **SC-004**: 100% of visitor-facing surfaces show city names; zero display agency tokens
  as city identity.
- **SC-005**: Each city's introductory copy renders correctly for 7 of 7 cities, with zero
  missing strings.
- **SC-006**: Telemetry history is continuous across the cutover for 7 of 7 cities, with
  zero query-visible splits.
- **SC-007**: An automated check fails when the two configuration city lists disagree.
- **SC-008**: A client built against old slugs fails visibly at join, with the failure
  present in operator-visible output — zero silent-empty outcomes.
- **SC-009**: Zero behavior changes beyond the rename — map origins, audio, filtering, and
  vehicle rendering are indistinguishable from pre-migration.
- **SC-010**: A newly discovered city's autonomously minted slug conforms to the rule.

## Assumptions

- **Legacy URLs are not supported.** Explicitly chosen. Previously shared or bookmarked
  links using agency slugs will **not** reach their intended city; they fall through to the
  default city silently. Accepted as the cost of the cheaper path. Should this prove
  unacceptable in the wild, adding aliasing later is a separate change.
- **Telemetry stays on agency values** (FR-016), accepting a documented, intentional
  divergence between the telemetry identifier and the city slug. Whether telemetry should
  eventually carry both an agency dimension and a city dimension is a step-two decision.
- **The version-gated join protects the realtime handshake only.** It does nothing for URL
  fragments; the two edge cases are independent.
- **The route-shape store needs no migration** — it is rebuilt from source on startup and
  is not durable.
- **No database is involved** in the city path, so there is no schema change or backfill.
- **New York is the outlier.** Unlike the other six config-only cities, it has bespoke
  wiring in several places and carries the most rename touch-points. Its existing
  multi-source design is the reference for step two.
- **Deploys span multiple independent lanes**, so a window exists where an outdated client
  meets an updated server; FR-009 makes that window loud rather than silent.
- **Branding and the deploy branch name are unchanged** by this feature.
- **Verification is manual per city** — there is no automated end-to-end harness covering
  all seven live.

## Out of Scope

- **Multi-agency-per-city composition** — step two, a separate spec. Includes restructuring
  configuration from a flat city list into a city-with-agencies shape and resolving
  per-agency route-key collisions (two authorities in one city can each publish a route
  "1" or "Red").
- **Legacy slug aliasing and URL self-healing** (see Assumptions).
- **Renaming the telemetry column** or adding a separate agency dimension.
- **Branding and deploy-branch renaming.**
- **Any behavior change** beyond the identifier rename.

## Notes on Source Document Accuracy

The source assessment states (§2b) that a prior feature renamed the realtime join method
to a `V2` name, establishing a precedent this migration could follow. **This does not match
the code** — the join method is still unversioned, and no `V2` variant exists. The
precedent is therefore *not* already in place: FR-009's version gate is new work, not a
repeat of an existing pattern. Planning should not assume prior art here.

Two source estimates also ran high against the code: the introductory-copy keys number
**30**, not the "~40" in §7 (the default city's keys are unprefixed), and the realtime
group-name work is smaller than §7's "High" rating implies now that no alias map is being
built. The overall file count (~35–40) is otherwise consistent with what is present.
