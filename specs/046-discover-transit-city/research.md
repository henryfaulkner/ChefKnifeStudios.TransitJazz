# Phase 0 Research: Discover Transit City

The feature spec and the source design document (`docs/DISCOVER_TRANSIT_CITY_SKILL_DESIGN_DOCUMENT.md`)
already lock all decisions that would otherwise need research (D1–D4, the six stages, the
risk register). This phase therefore consolidates *how* those locked decisions map onto
concrete, already-existing repo machinery, and resolves the one open question the design
doc explicitly left to the builder: **the exact shape of the two report templates**, which
is the focus this planning pass was asked to nail down.

## Decision: Ground every "what compatible means" claim in `TransitDataWorker`'s actual source, not just skill docs

**Rationale**: A follow-up clarification pass (2026-07-25, informed by a direct read of
`src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker`) found the original plan
was accurate at the level the `mj-gtfs`/`gtfs-compatibility` skill docs describe, but
under-specified relative to what the worker's C# source actually does today. Five concrete
gaps were closed (see spec Clarifications, 2026-07-25 session, and `data-model.md`'s new
"Ground-truth reference" table):

1. The worker's generic city extension point (`Cities/GtfsRtCity.cs` + `CityConfig`) means
   most onboarding is genuinely config-only — the templates previously referenced a stale,
   pre-multi-city `Worker.cs _gtfsRtUrl` field that no longer exists in the source.
2. A key requirement is not itself an incompatibility — `CityConfig.ApiKeyEnvVar`/
   `ApiKeyQueryParam` already handle it generically. BLOCKED reports now split KEY-GATED
   (config-only once a key exists) from NO-USABLE-FEED (needs new adapter code) — these
   were previously conflated into one undifferentiated "blocked" outcome.
3. `RouteIdNormalizer.cs`'s three generic transforms mean a route-ID mismatch is often
   config-only, not a code change — the compatible-outcome template now requires checking
   against these three before calling a mismatch "needs new code."
4. Rail has two genuinely different integration mechanisms in the real code
   (`CityConfig.RailRouteIdMap`, config-only, vs. `RailRealtime/RailRealtimeAdapter.cs`, a
   bespoke class) — previously the templates had one undifferentiated "Rail" verdict.
5. `Worker.cs`'s `ResolveCategory` explicitly falls back to `"unknown"`, never silently
   reusing an existing category — a deliberate, source-commented data-quality signal the
   original templates didn't mention, describing only a raw skip percentage.

A sixth candidate gap — NYMTA's bespoke `GtfsRtUrls`/`BusGtfsRtUrls` split — was
deliberately left **out of scope**: only one existing city uses this third rail-mechanism
shape, and building formal detection for a pattern seen exactly once is speculative
complexity this feature doesn't need yet (captured as a spec Assumption instead of a
requirement).

**Alternatives considered**: Leaving the plan as originally written, trusting the
`gtfs-compatibility` function's existing abstraction level — rejected because the user
explicitly asked to align context and plan with the ground-truth worker source, and
because these five gaps produce materially different, actionable report content (e.g. a
KEY-GATED report correctly signals "no code needed, just a key," where the prior
undifferentiated BLOCKED report did not).

## Decision: Delegate all feed fetch/decode to `mj-gtfs`; write zero new fetch/decode code

**Rationale**: `mj-gtfs` (`.claude/skills/mj-gtfs/SKILL.md`) already implements verified,
working PowerShell + inline-Python fetch and decode for all three feed types this feature
needs (GTFS-RT protobuf, static GTFS zip, agency-specific rail JSON), including a
"combined decode + alignment fast path" that emits exactly the JSON shape
(`rt.*`/`static.*`/`alignment.*`) the report templates consume. Reimplementing any part of
this would duplicate maintenance burden and risk re-introducing bugs `mj-gtfs` has already
fixed (e.g. the field-number drift self-diagnosis via `rt._diag_note`).

**Alternatives considered**: Writing a standalone fetch/decode script scoped to this skill
— rejected because it duplicates `mj-gtfs` for no benefit and would drift from it over time
(two copies of protobuf field-number tribal knowledge is worse than one).

## Decision: Delegate compatibility interpretation to the `gtfs-compatibility` function

**Rationale**: `.claude/skills/mj-data-explorer/functions/gtfs-compatibility.md` already
defines what "compatible" means for the worker (route_id + lat/lon required, alignment
against `RouteJoinKey` = `route_short_name ?? route_id`, rail as an independent axis) and
the exact interpretation table (100% match → COMPATIBLE, 0% → INCOMPATIBLE-likely-fixable,
etc.). This feature's stage 4 is that function's logic minus its interactive "ask the user
for URLs" step, since stage 3 supplies URLs instead. No new interpretation rules are
needed.

**Alternatives considered**: None — the interpretation table is already the house standard
and both exemplar reports (`ttc.md`, `cta.md`) already conform to it.

## Decision: Two rigid fill-in templates, not one flexible template

This is the point the user's `/speckit-plan` invocation explicitly called out: **create
specific templates for the evaluation agent to fill out for its final output doc.**

**Rationale**: Reading the five existing `docs/city-compat/*.md` reports side by side
(`ttc.md`, `cta.md`, `wmata.md`, `mbta.md`, `nymta.md`), two genuinely different shapes
exist depending on outcome:
- **COMPATIBLE/PARTIAL shape** (`ttc.md`): Feed health → Vehicle positions (GTFS-RT) →
  Route ID alignment → Rail (omitted if no rail) → Verdict (prose + table) → Adding X as a
  data source. Every field is a *measured* number from a successful decode.
- **BLOCKED shape** (`cta.md`): a top-of-doc statement of *why* nothing could be fetched →
  whatever static-only data *was* reachable → a Verdict table with `UNASSESSED`/`N/A`
  cells instead of percentages → the same "Adding X" section but describing prospective
  adapter work instead of a drop-in config change → an additional "Open items for a
  follow-up pass" section that the COMPATIBLE shape does not have.

A single loose template covering both cases (with lots of "if applicable" hedging) would
reproduce exactly the failure mode the design doc warns about hardest: an unattended agent
inventing a plausible-sounding number to fill a field that doesn't cleanly apply to its
case. **Two separate, rigid, copy-and-fill templates** — one per outcome, chosen once at
the start of stage 5 — remove that ambiguity: every blank in the COMPATIBLE template is a
required measurement (if it can't be filled from real decode output, that's a signal the
BLOCKED template should have been used instead), and every blank in the BLOCKED template
either has an explicit `UNASSESSED`/`N/A` default or asks only for what *is* knowable
without a live feed (static-only data, prospective adapter shape).

**Alternatives considered**:
- *One template with conditional sections* — rejected: conditional Markdown is exactly
  what invites an agent to leave a section half-filled or invent a plausible value for an
  inapplicable field.
- *Free-form report writing "in the style of" the exemplars* — this is what the design doc
  describes as the fallback for a human, but for a zero-supervision agent, "match this
  style" is far weaker than "fill in this blank" — it doesn't mechanically prevent a
  fabricated percentage the way a hard template with only two legal terminal values
  (`UNASSESSED` or `N/A`) for unmeasurable fields does.
- *A single JSON/YAML report schema, rendered to Markdown by a script* — rejected as
  disproportionate: this repo's existing report convention is hand-shaped Markdown for
  human readability (these are PR-review artifacts, read by the maintainer, not
  machine-parsed), and introducing a rendering step is unneeded machinery for a
  once-a-week, one-file-per-run artifact.

## Decision: Templates live in `contracts/` (this spec) and are copied verbatim into the skill's `references/`

**Rationale**: Per this repo's `/speckit-plan` convention (`043-toronto-ttc-transit` did
the analogous thing with `contracts/city-config.md`), a contract file documents the exact
shape something must take; implementation applies it. Here, "applying" a template contract
means the skill's `references/report-templates.md` contains that same content, since the
templates *are* the runtime artifact a report-writing agent copies from. Keeping the
authoritative copy in `specs/046-discover-transit-city/contracts/` and treating the skill's
copy as the "applied" version keeps this feature's planning trail consistent with how
prior features used `contracts/`.

## Decision: No new git/PR tooling — reuse `git` + `gh` CLI exactly as documented

**Rationale**: The design doc's stage 6 (§4 STAGE 6) already specifies the exact git
sequence (`checkout -b compat/{slug}` → `add` the one file → `commit` → `push -u` → `gh pr
create`). This repo already uses `gh` conventionally (per the global CLAUDE.md git/PR
instructions). No new tooling or auth setup is required; the only new discipline is the
"stage only that one file, abort if `git status` shows anything else" guard, which is a
process rule for the skill's SKILL.md, not a research question.

**Alternatives considered**: None — this is already fully specified by the design doc and
matches standard repo practice.

## Decision: `/schedule` cloud routine, weekly cadence, prompt = `/discover-transit-city`

**Rationale**: Per design doc §8 and locked decision D4. A local `/loop` or OS Task
Scheduler would die with the Windows machine going to sleep (this repo's `CLAUDE.md` notes
the dev environment is Windows 11); a cloud routine is the only mechanism that reliably
fires unattended. Weekly (not daily) cadence is chosen because the curated pool is ~20
candidates — daily would exhaust the curated arm in three weeks and prematurely lean on
open-ended `WebSearch` discovery, which is the riskier, lower-confidence path (per the
design doc's own ranking rationale).

**Alternatives considered**: Daily cadence — rejected, burns the curated pool too fast.
Local scheduling — rejected, not reliably unattended on this repo's dev machine.

## Remaining NEEDS CLARIFICATION

None. All Technical Context fields were resolvable from existing repo conventions and the
locked design-doc decisions; no clarification markers remain.
