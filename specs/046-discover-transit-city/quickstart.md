# Quickstart: Discover Transit City

This is the **manual dry-run** the design doc requires before ever creating the
`/schedule` routine (design doc §10 step 5: "it is the only way to prove stage 3 finds
feeds without a human, and it costs one run to learn"). Do this once, by hand, after the
skill files are implemented and before wiring up the weekly cloud routine.

## Prerequisites

- `.claude/skills/discover-transit-city/` exists with `SKILL.md`, `candidates.md`,
  `references/feed-discovery-playbook.md`, `references/report-templates.md`.
- `.claude/skills/mj-gtfs/SKILL.md` is unchanged and working (this feature never modifies
  it).
- `git` and `gh` are authenticated in the environment the dry-run runs in.
- Repo working tree is clean (`git status` shows nothing pending) before starting, so the
  STAGE 6 "only one file changed" check is unambiguous.

## Run it

Invoke `/discover-transit-city` with no arguments, exactly as the scheduled routine would.

## Expected outcome (one of two)

### A. A candidate was found and evaluated

Confirm, in order:

1. **Stage 1 picked the right candidate.** Per `candidates.md`'s ranking (keyless NA
   agencies first), the first run should pick **SEPTA** (Philadelphia) — the design doc's
   own prediction (§10 step 5). If it picked something else, check whether SEPTA was
   already in the done-set (it shouldn't be) or whether the candidates file's rank ordering
   drifted.
2. **Exactly one new file exists**: `docs/city-compat/septa.md` (or whichever city was
   actually chosen). Run `git status` — it must show only this one new file.
3. **The report used the correct template** for its outcome — open the file and check it
   against `contracts/report-template-compatible.md` or `-blocked.md` field-for-field. No
   placeholder tokens (literal `<...>`) should remain in the committed file. No numeric
   field should look suspicious (e.g. round percentages like exactly 50.0% on a small
   sample are a smell — verify against the raw decode JSON if anything looks too clean).
   If BLOCKED, confirm it states exactly one of **KEY-GATED** or **NO-USABLE-FEED** — not
   left ambiguous, not both, not neither. If COMPATIBLE/PARTIAL and any route ID went
   unmatched, confirm the report checked it against the platform's three real
   `RouteIdNormalizer` transforms (`uppercase`/`plusToSbs`/`stripLeadingZeros`) before
   calling it a code-change requirement, and confirm the "unknown category" runtime
   behavior is named rather than just a raw skip percentage. If rail is present, confirm
   the report names which real mechanism applies (`RailRouteIdMap` config-only remap vs. a
   bespoke `RailRealtimeAdapter`-style class).
3a. **The aggregate score is present, first, and reproducible.** Confirm the score block
   (`<score>/100 — <tier>`, plus the bus/rail/credential breakdown) is the FIRST content in
   the file, above the H1's surrounding context. Manually recompute the score by hand from
   `contracts/aggregate-score-formula.md` using the report's own published measured values
   (`rt.lat_lon_pct`, `alignment.match_pct`, rail mechanism, credential situation, and — if
   BLOCKED — the classification ceiling). The recomputed number MUST exactly match what's
   printed in the report (SC-009). If it doesn't, the formula was applied incorrectly —
   this is a blocking defect, not a cosmetic one, since the whole point of the score is
   trustworthy comparability across reports. Confirm the score falls within the effort
   tier's stated range with no off-by-one boundary errors (e.g. a 90 must read Drop-in,
   not Minor Config).
4. **A PR exists**, targeting `main`, titled `Compat report: SEPTA (Philadelphia) —
   <verdict>`, body containing the required "does not onboard the city" sentence.
5. **`main` has zero new commits.** `git log main` before and after the run must be
   identical.
6. Walk the `pr-delivery-contract.md` verification checklist in full.

### B. No candidate was found (unlikely on a first run, but verify the path exists)

- Confirm no file was written and no PR was opened.
- Confirm the run ended with a clear one-line note rather than an error or a stall.
- (This path is realistically only reachable once the curated pool is exhausted — on a
  first dry-run, seeing this outcome instead of (A) likely means the done-set detection is
  miscounting existing `docs/city-compat/*.md` reports as already covering every candidate
  row. Investigate before proceeding.)

## If the dry-run reveals a problem

Do not create the `/schedule` routine until the dry-run is clean. Common issues to check
first, per the design doc's risk register:

- **Wrong template used or fields invented**: re-read the chosen contract template; the
  agent likely didn't stop at a stage-3 dead end and instead pushed through to the
  COMPATIBLE template with placeholder numbers. Fix by tightening `SKILL.md`'s STAGE 3→5
  branch instruction.
- **`rt.lat_lon_pct = 0` false negative**: check whether `rt._diag_note` was present and
  whether the run followed the raw-field-inspection re-decode path before writing the
  report — this is the single riskiest silent-failure mode called out in the spec's edge
  cases.
- **More than one file changed**: stop, inspect what else changed, and treat it as a
  blocking bug — this violates the FR-013 invariant and must be fixed before scheduling.
- **Duplicate/near-duplicate city picked**: check the done-set parsing logic reads each
  existing report's H1 (city + authority) rather than only comparing filenames.

## After a clean dry-run

Create the weekly `/schedule` routine:
- Prompt: exactly `/discover-transit-city`.
- Cadence: weekly.
- Include the belt-and-suspenders guardrail text from the design doc §8 in the routine
  prompt (restating: write exactly one file, commit only that file to a new
  `compat/{slug}` branch, open a PR to `main`, never commit to `main`, never merge, never
  edit application code, never run `add-transit-city`'s onboarding flow).
