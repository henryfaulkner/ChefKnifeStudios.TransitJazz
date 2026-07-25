# Feature Specification: Discover Transit City (autonomous compatibility scout)

**Feature Branch**: `046-discover-transit-city`
**Created**: 2026-07-25
**Status**: Draft
**Input**: User description: "docs/DISCOVER_TRANSIT_CITY_SKILL_DESIGN_DOCUMENT.md"

## Clarifications

### Session 2026-07-25

- Q: The worker's real fallback for an unmatched route is tagging it category "unknown"
  (never silently "bus") — a deliberate data-quality signal, distinct from the
  decode-level skip counters. Should the compat report templates add an explicit field for
  this app-level "unknown category" semantic, separate from the raw alignment percentage?
  → A: Yes, add it to both templates.
- Q: The real worker's generic city path already fully supports a query-param/header API
  key (config-only, no code change) — so a key-gated feed is not structurally
  incompatible, only unusable to an unattended run with no key in hand. Should the spec
  distinguish "BLOCKED: key-gated, config-only once a key exists" from a harder blocked
  case (no usable feed format exists at all, needs a bespoke adapter)?
  → A: Yes, split into two BLOCKED sub-reasons.
- Q: The worker already ships three generic, config-driven route-ID transforms
  (uppercase, plusToSbs, stripLeadingZeros) applied without any code change. When a
  route-ID mismatch would be fixed by one of these, should the verdict reflect that this
  is a config-only fix rather than a generic "needs new code" partial-compatibility
  bucket?
  → A: Yes, check against existing normalizers first.
- Q: The worker has two distinct rail-integration mechanisms — a config-only route-ID
  remap dictionary vs. a bespoke adapter class parsing an agency-specific non-GTFS-RT
  feed. Should the report distinguish which mechanism a candidate's rail feed would need?
  → A: Yes, name both mechanisms explicitly.
- Q: One city (NYMTA) uses a third, bespoke-only mechanism — splitting a single GTFS-RT
  feed into a subway-synthesis path and a separate citywide-bus path. Should stage 3
  explicitly detect and classify this pattern for new candidates?
  → A: Out of scope — flag as a follow-up note/open item only if encountered, do not build
  formal detection logic for a pattern only one existing city uses.

- Q: What scale and direction should the new aggregate compatibility score use?
  → A: 0–100, higher = more compatible / less integration effort.
- Q: How should categorical facts (rail integration mechanism, blocked sub-reason) convert
  into the formula's numeric inputs?
  → A: A fixed, published penalty/credit lookup table per category — not a derived
  multi-factor blend.
- Q: Should a BLOCKED report (no live feed ever measured) still get a numeric score, or
  show only a qualitative effort tier?
  → A: Compute a real number even for BLOCKED reports, built from whatever was measurable
  (static data, desk-checked rail alignment) plus a fixed penalty for the blocking
  classification — every report gets a number, not just successful ones.
- Q: How many effort tiers should the score bucket into?
  → A: 4 tiers — Drop-in / Minor Config / Adapter Needed / Not Viable — mirroring the
  config-only vs. bespoke-code fork this app's onboarding already makes.
- Q: How should the two bus-side measured axes (required-fields health, route-ID
  alignment %) combine?
  → A: Required fields gate the score (a feed with no usable live positions caps low
  regardless of alignment); when required fields pass, alignment % scales the bus
  contribution linearly.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Weekly hands-free discovery of a new candidate city (Priority: P1)

As the maintainer of TransitJazz, I want a scheduled job to autonomously pick a
not-yet-evaluated North American or European transit authority, check whether its public
real-time feeds are usable by the app, and hand me a pull request with the findings — so
that the pipeline of candidate cities keeps growing every week without me spending time on
manual feed research.

**Why this priority**: This is the entire feature. Without an autonomous, unattended run
that reliably produces a reviewable report, there is no product — the whole point is
removing the maintainer from the discovery loop.

**Independent Test**: Trigger the job with no arguments and no human present. Confirm that,
some time later, a pull request exists against `main` that adds exactly one new
compatibility report file, and that `main` itself is untouched.

**Acceptance Scenarios**:

1. **Given** a transit authority in the candidate pool that has never been evaluated and
   publishes a standard, keyless, vehicle-position real-time feed, **When** the job runs,
   **Then** it fetches and decodes that feed, computes compatibility, and delivers a pull
   request containing one new report file marking the authority (fully or partially)
   compatible, with real measured numbers (not placeholders).
2. **Given** the job has already produced reports for several authorities in past runs,
   **When** it runs again, **Then** it selects an authority that has not yet been evaluated
   (by city and by authority, not merely by report filename) rather than re-evaluating one
   already covered.
3. **Given** no human is available to answer questions or supply credentials, **When** the
   job encounters any ambiguity (e.g., a city with multiple competing transit operators),
   **Then** it resolves the ambiguity itself using a stated tie-break rule and proceeds to
   completion without pausing for input.

---

### User Story 2 - Documented negative result when a feed isn't usable (Priority: P1)

As the maintainer, I want the job to still produce a clear, honest report when an
authority's feeds turn out to be unusable (no real-time feed, key-gated, or missing vehicle
positions) — so that a dead end is recorded and never re-attempted, instead of the run
silently doing nothing or fabricating numbers.

**Why this priority**: Feed availability is unpredictable and failure is the single most
common outcome for a hands-free discovery job (per the design's own risk assessment). If
failures aren't turned into durable, honest records, the job either stalls, silently skips
cities (defeating the "keeps growing every week" goal), or — worse — reports fabricated
compatibility numbers that mislead a future onboarding decision.

**Independent Test**: Point the job at an authority known to have no public real-time feed
(or only a key-gated one). Confirm it still completes, still opens a pull request, and the
report clearly states the specific reason the authority is blocked, with any unmeasured
figures marked as such rather than filled in with a guess.

**Acceptance Scenarios**:

1. **Given** a chosen authority publishes no real-time vehicle-position feed at all,
   **When** the job runs, **Then** it produces a report documenting the authority as blocked
   for that specific reason, still reports what could be determined from its static
   schedule data, and still opens a pull request.
2. **Given** a chosen authority's real-time feed requires a registration key the job does
   not already possess, **When** the job runs, **Then** it does not attempt to register for
   or fabricate a key, and instead reports the authority as blocked for that reason.
3. **Given** a value could not be measured because the feed was unreachable, **When** the
   report is written, **Then** that value is marked as not assessed rather than given an
   invented number.

---

### User Story 3 - Reports never bypass human review (Priority: P1)

As the maintainer, I want every report the job produces to land only as a pull request that
I must review and merge myself — never as a direct change to the main codebase, and never as
a trigger that starts onboarding a city into the live app — so that an autonomous job can
never affect production behavior or make an authority-selection mistake permanent without my
sign-off.

**Why this priority**: This is a hard safety boundary called out repeatedly in the source
design as non-negotiable. An autonomous, credential-free, unsupervised job touching the
production branch or triggering irreversible onboarding would be an unacceptable risk,
regardless of how good stages 1–5 are.

**Independent Test**: Run the job repeatedly (including a run that picks the "wrong"
authority for an ambiguous city) and confirm that in every case the only durable output is
an open pull request with a single new file — `main` never receives a direct commit, no
application configuration or code file is ever modified, and no other automated process
begins onboarding the city.

**Acceptance Scenarios**:

1. **Given** the job completes a run, **When** its output is inspected, **Then** the only
   change proposed is a new pull request adding exactly one report file, and no commit has
   been made directly to `main`.
2. **Given** the job picked an authority that later turns out to be the wrong one for that
   city, **When** this is discovered, **Then** the mistake is contained to an unmerged pull
   request that a human can simply decline, with no other system state affected.
3. **Given** a report recommends an authority as fully compatible, **When** the run ends,
   **Then** the job has not begun any onboarding activity (no city-registration, map, or
   configuration changes) — onboarding remains a separate, human-initiated action.

---

### User Story 4 - One number tells me the effort required (Priority: P1)

As the maintainer, I want every report to open with a single aggregate compatibility score
and a plain-language effort tier — so that I can triage a week's worth of candidate reports
in seconds, without reading every section, and get a reliable, at-a-glance sense of whether
an authority is a near-drop-in or a multi-week bespoke integration.

**Why this priority**: This is the report's headline number — the single most
consequential piece of information a reviewer sees before deciding whether to invest time
reading further. An inconsistent or unreproducible score would actively mislead
prioritization decisions, so it must be described precisely enough that any two runs
evaluating the same measured facts always produce the same score.

**Independent Test**: Take the measured facts from two already-published reports covering
different outcomes (a clean compatible case and a blocked case) and manually recompute
the score from the published formula. Confirm the recomputed score matches what's printed
at the top of each report exactly, and confirm that a reader can map the score to an
effort tier without reading any other section of the document.

**Acceptance Scenarios**:

1. **Given** a completed evaluation of any outcome (compatible, partially compatible, or
   blocked), **When** the report is written, **Then** it opens with a numeric score (0–100,
   higher meaning more compatible / less integration effort) and one of four named effort
   tiers, both appearing before any other report content.
2. **Given** the same set of measured inputs (required-fields health, route-ID alignment
   percentage, rail integration mechanism and its alignment, and — for a blocked case — the
   blocking classification), **When** the score is computed by two independent runs,
   **Then** both runs produce the identical numeric score — the formula has no
   run-to-run variance, randomness, or subjective judgment call.
3. **Given** a blocked report where no live feed was ever measured, **When** the score is
   computed, **Then** it still produces a real number (never a placeholder or omitted
   field), built only from what was actually measurable plus the fixed penalty for the
   blocking classification — not a guess at what the live feed might have shown.
4. **Given** a report's score falls in a particular numeric range, **When** a reader checks
   only the effort tier label, **Then** the tier reliably indicates the class of future
   work implied (no code changes needed / minor configuration such as a key or an existing
   transform / a new bespoke adapter required / not realistically viable right now) without
   needing to read the detailed sections.

---

### Edge Cases

- What happens when both the curated candidate list is exhausted and open-ended discovery
  finds no new, not-yet-evaluated authority? The run must end cleanly with a short note that
  nothing was found, producing no report file and no pull request.
- What happens when a chosen city has multiple plausible transit operators and none clearly
  dominates? The job applies its stated tie-break rule, proceeds with its best pick, and
  notes the ambiguity in the report rather than stalling or asking for clarification.
- What happens when a real-time feed is reachable and decodes, but happens to report zero
  active vehicles at the moment of the check (e.g., off-peak hours)? The report notes the
  time-of-day caveat rather than immediately declaring the authority permanently
  incompatible.
- What happens when a feed decodes but position data all comes back empty/zero due to a
  format quirk rather than a genuinely empty feed? The job must recognize this signal and
  investigate further before writing a compatibility number, rather than recording a false
  "no data" result for an otherwise-good feed.
- What happens when the job cannot publish its findings (e.g., it cannot push a branch or
  open a pull request due to a connectivity or permissions problem)? The work already done
  must not be lost; the job ends with a clear statement of what succeeded, what failed, and
  where the unpublished result can be found, and it must never fall back to committing
  directly to `main`.
- What happens if the same transit authority is reachable under more than one name or
  abbreviation? The job must recognize it as already evaluated and not produce a duplicate
  report.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST be runnable with zero arguments and complete an entire
  evaluation cycle without requesting or waiting on any human input.
- **FR-002**: The system MUST select, on each run, exactly one city/transit-authority pair
  that has not been evaluated in a prior run, checking for duplicates by city and by
  authority identity (not solely by an output filename or slug).
- **FR-003**: The system MUST draw its candidate first from a maintained, ranked list of
  candidate cities, and only fall back to open-ended discovery once that list is exhausted
  of not-yet-evaluated entries.
- **FR-004**: The system MUST resolve a city to a single primary transit authority using a
  stated, deterministic tie-break rule when more than one plausible operator exists, without
  asking a human to choose.
- **FR-005**: The system MUST attempt to discover the chosen authority's public real-time
  vehicle-position feed, public static schedule data, and (when applicable) a rail-specific
  real-time feed.
- **FR-006**: The system MUST distinguish a genuine vehicle-position real-time feed from
  other real-time feed types (e.g., trip-update or alert-only feeds) that cannot supply live
  vehicle locations, and MUST NOT count the latter as satisfying the requirement for a
  usable feed.
- **FR-007**: The system MUST NOT attempt to register for, guess, or fabricate any
  credential or access key; any feed reachable only behind a credential the job does not
  already possess MUST be treated as unusable ("blocked"). Because the underlying platform
  can consume a query-parameter or header-based key without any code change once one is
  available, a blocked report for this specific reason MUST be labeled distinctly from a
  blocked report where no usable feed format exists at all (see FR-012a).
- **FR-008**: The system MUST evaluate compatibility using only measurements taken from an
  actual, successful fetch and decode of real feed data — every reported number MUST
  originate from that measurement, never from an assumption or placeholder.
- **FR-008a**: Before concluding that a route-ID mismatch requires new code, the system
  MUST check whether the mismatch would be resolved by one of the platform's existing,
  generic, config-only identifier transforms (case normalization, a trailing-marker-to-
  suffix rewrite, and leading-zero stripping). If one of these transforms would close the
  mismatch, the verdict MUST reflect a config-only fix rather than a code-change
  requirement.
- **FR-009**: When a value cannot be measured (feed unreachable, blocked, or not
  applicable), the system MUST record that field as explicitly "not assessed" or
  "not applicable" rather than omitting it or inventing a value.
- **FR-010**: The system MUST produce a written compatibility report for every run that
  selects a city, covering both the successful-evaluation case and the blocked/negative
  case, in a consistent, comparable format across runs, with the aggregate compatibility
  score and effort tier (FR-012c/FR-012d) as the first content a reader encounters.
- **FR-011**: Each report MUST state, for real-time vehicle tracking and (independently) for
  rail service, one of: compatible, partially compatible, incompatible, or not applicable —
  and MUST NOT combine the two into a single verdict. When rail is anything other than not
  applicable, the report MUST further state which of the platform's two known rail
  integration mechanisms would apply — a config-only route-identifier remap, or a
  bespoke, agency-specific adapter for a non-standard live-position feed — since these
  represent materially different amounts of future onboarding effort.
- **FR-012**: A report of the negative/blocked case MUST state the specific reason the
  authority could not be evaluated further, and MUST still include whatever could be
  determined from data that was reachable (e.g., static schedule health).
- **FR-012a**: A blocked report MUST classify its blocking reason into one of: (a)
  key-gated — a usable real-time feed format exists but requires a credential not already
  available, resolvable with configuration alone once a credential is obtained; or (b) no
  usable feed exists — no real-time feed of a consumable format is published at all,
  requiring new integration code beyond configuration. These two cases MUST NOT be
  reported identically, since they imply different follow-up effort.
- **FR-012b**: When a vehicle's route cannot be matched during evaluation, the report MUST
  note that the platform's runtime behavior for this case is to render the vehicle under
  an explicit "unknown" category rather than silently defaulting it into an existing
  category (e.g., treating it as a bus) — this is a deliberate data-quality signal in the
  platform, not a defect, and the report's alignment-gap discussion MUST describe this
  actual consequence rather than only citing a raw skip percentage.
- **FR-012c**: Every report, regardless of outcome, MUST open with a single **aggregate
  compatibility score** on a 0–100 scale (higher = more compatible / less integration
  effort) and a corresponding **effort tier**, both appearing before any other report
  section. The score MUST be produced by a fixed, published, deterministic formula — never
  a subjective judgment call — such that two runs presented with the same measured facts
  always compute the identical score. The formula is:
  - **Required-fields gate (bus)**: if the platform's required fields (route identifier +
    live position) are not usably present in the feed, the bus contribution is capped at
    10 of its possible 70 points, regardless of route-ID alignment — a feed without usable
    live positions cannot drive the platform no matter how well its route IDs align. If
    required fields are usably present, the bus contribution is `70 × (effective alignment
    percentage ÷ 100)`, where "effective alignment percentage" credits any mismatch that
    one of the platform's existing config-only identifier transforms would resolve (per
    FR-008a) as matched.
  - **Rail contribution (0–20 points)**: 20 if rail is not applicable to this authority (no
    penalty for a bus-only agency); 20 if rail is present and would integrate via a
    config-only mechanism with a clean/verified alignment; 12 if rail is present and would
    integrate via a config-only mechanism but alignment is partial or unverified; 5 if rail
    would require a bespoke, agency-specific adapter regardless of alignment quality.
  - **Credential contribution (0–10 points)**: 10 if the evaluated feed(s) are keyless or
    already authorized; 0 if any required feed is key-gated (FR-012a), since a credential
    still must be obtained before any config-only fix applies.
  - **Blocked-outcome ceiling**: for a blocked report, the bus contribution is always 0 (no
    live feed was ever measured, so nothing is credited there), the rail contribution uses
    only what a static-data-plus-published-line-code desk check can determine (by the same
    0/5/12/20 scale above, or 0 if not even that is determinable), and the total is then
    capped at 40 if the blocking classification is key-gated, or capped at 15 if the
    blocking classification is no-usable-feed-exists — reflecting that a missing live bus
    feed dominates the score regardless of how clean the static/rail desk-check looks.
  - The three contributions (bus, rail, credential) sum to the aggregate score before any
    blocked-outcome ceiling is applied; the ceiling, when applicable, is a hard maximum,
    not an additional deduction.
- **FR-012d**: The aggregate score MUST map to exactly one of four named effort tiers,
  with no ambiguous or overlapping boundaries: **Drop-in** (90–100, config-only, no code
  changes anticipated), **Minor Config** (70–89, resolvable via configuration alone — e.g.
  obtaining a key and/or applying an existing identifier transform — no new code), **Adapter
  Needed** (40–69, requires new integration code such as a bespoke city implementation or
  rail adapter), and **Not Viable** (0–39, no usable feed format exists or required fields
  fundamentally fail, making integration impractical without substantial new work).
- **FR-013**: The system MUST deliver each run's report as a proposed change for human
  review (a pull request) rather than applying it directly, and MUST modify no file other
  than the single new report for that run.
- **FR-014**: The system MUST NOT make any change directly to the project's main/production
  branch under any circumstance, including failure or retry scenarios.
- **FR-015**: The system MUST NOT perform, trigger, or begin any onboarding of a city into
  the live application (no changes to application configuration, code, map data, or
  city-registration) — evaluation and onboarding are separate, and onboarding remains a
  distinct, human-initiated action.
- **FR-016**: If the run finds no unevaluated candidate available (both the curated list and
  open-ended discovery are exhausted), the system MUST end without producing a report file
  or a pull request, and MUST clearly indicate that no candidate was found.
- **FR-017**: If the system cannot publish its completed work as a pull request (e.g., due
  to a connectivity or access problem), it MUST preserve the completed work in a recoverable
  form and clearly report what succeeded, what failed, and where the result can be found,
  without falling back to a direct change to the main branch.
- **FR-018**: The system MUST be schedulable to run automatically on a recurring cadence
  without requiring the maintainer's local machine to be running at the scheduled time.

### Key Entities

- **Candidate pool entry**: A ranked, curated city/authority pair considered for evaluation,
  including the region, whether it is expected to run rail service, and (when already
  known) links to its public feeds. Consumed top-to-bottom before falling back to open
  discovery.
- **Evaluated-city record**: The durable set of cities/authorities already evaluated in a
  past run (derived from prior reports), used to prevent re-evaluating or duplicating work
  on the same authority under a different name.
- **Compatibility report**: The single output artifact of a run — one document per
  evaluated authority, opening with the aggregate compatibility score and effort tier
  (FR-012c/FR-012d), then stating feed health, live vehicle-tracking compatibility, rail
  compatibility (independently, naming which integration mechanism — config-only remap or
  bespoke adapter — would apply), the specific reasoning behind each verdict, and, in the
  blocked case, the reason evaluation could not proceed further (distinguishing key-gated
  from no-usable-feed-format, per FR-012a).
- **Aggregate compatibility score**: A derived, deterministic 0–100 figure computed from
  three weighted contributions — required-fields-gated bus alignment (0–70), rail
  integration mechanism and alignment (0–20), and credential availability (0–10) — subject
  to a fixed ceiling on blocked outcomes, per FR-012c. Maps to exactly one of four effort
  tiers (FR-012d): Drop-in, Minor Config, Adapter Needed, Not Viable.
- **Scheduled run**: A recurring, unattended invocation of the discovery process on a fixed
  cadence, independent of any particular machine being powered on.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An unattended, scheduled run completes end-to-end — from city selection
  through report delivery — with zero human interactions required, in at least 95% of
  weekly runs.
- **SC-002**: Every run that selects a candidate produces exactly one durable report,
  regardless of whether the outcome is a usable feed or a blocked authority — runs never
  silently produce nothing when a candidate was available.
- **SC-003**: Across any sequence of runs, no city/authority is ever evaluated and reported
  on more than once.
- **SC-004**: 100% of numeric or percentage figures appearing in a delivered report
  correspond to an actual measurement taken during that run; none are placeholders or
  guesses.
- **SC-005**: 100% of delivered reports arrive as a reviewable pull request against the
  main branch, with zero direct commits to the main branch across all runs.
- **SC-006**: Zero runs, across any outcome (successful, blocked, or failed-to-publish),
  result in a change to application code, configuration, or the start of city onboarding.
- **SC-007**: The maintainer can review a new candidate authority's viability, end to end,
  by reading a single delivered pull request, without needing to personally repeat any feed
  research.
- **SC-008**: 100% of delivered reports open with a numeric aggregate compatibility score
  and an effort tier, appearing before any other report content, with zero reports omitting
  either.
- **SC-009**: Recomputing the aggregate score by hand from a report's own published measured
  inputs (per the FR-012c formula) reproduces the exact score printed at the top of that
  report, every time — the formula has zero run-to-run variance for identical inputs.
- **SC-010**: The maintainer can determine which of the four effort tiers a report falls
  into by reading only the top of the document, without needing to read the detailed
  feed-health, alignment, or rail sections to understand the rough scope of future
  integration work.

## Assumptions

- The maintainer already has an established, house-style format for compatibility reports
  (existing reports for previously evaluated authorities) that the new reports should match,
  so reviewers can compare runs at a glance; this spec does not restate that format's exact
  layout, only that consistency is required.
- "North American or European city" bounds the candidate scope for this feature; expansion
  to other regions is out of scope unless a future change says otherwise.
- A weekly cadence is an acceptable default recurrence for the scheduled run, chosen to
  outlast the curated candidate pool before leaning on open-ended discovery; this can be
  adjusted later without being a scope change.
- The existing feed-fetching, decoding, and compatibility-evaluation logic used elsewhere in
  the project is assumed reusable and correct; this feature is about orchestrating an
  unattended run around that logic, not re-deriving how compatibility is measured.
- "Primary transit authority" tie-break resolution is expected to occasionally be wrong for
  a genuinely ambiguous city; this is an accepted risk mitigated entirely by the
  pull-request review gate (a human can decline a mis-picked authority), not by requiring
  perfect automated judgment.
- Git hosting and pull-request tooling the project already uses for other changes are
  assumed available to this feature; no new hosting integration is in scope.
- A rare third rail-integration shape exists in the live platform today (one authority
  splits a single real-time feed into a subway-synthesis path and a separate bus path,
  entirely through bespoke code) but is used by only one already-evaluated authority.
  Building formal detection/classification for this shape is out of scope for this
  feature; if a future candidate appears to need it, the report may note it as an open
  item, but the skill is not required to recognize or classify it as its own category.
