---
name: discover-transit-city
description: Hands-free CRON orchestrator that autonomously picks an unevaluated North American or European city, finds its primary transit authority, discovers its GTFS-RT/static/rail feed URLs, evaluates TransitJazz compatibility via mj-gtfs, writes docs/city-compat/{slug}.md, and opens a PR. Use when the user says "discover a transit city", "run the city discovery job", "find a new compatible agency", or when invoked with no arguments by a scheduled routine.
---

# Discover Transit City

A hands-free, CRON-driven orchestrator. Each invocation autonomously selects one
not-yet-evaluated city, finds its transit authority, discovers its feed URLs, evaluates
compatibility, writes exactly one report, and opens a PR — with **zero human
interaction** at any point. This is the defining constraint and it shapes every stage
below: never ask the user a question, never wait for input, and degrade to a written
BLOCKED report rather than stalling or guessing.

This skill is an **orchestrator that delegates**. It contains genuinely new logic only
for STAGE 1–3 (select / find authority / discover feeds). STAGE 4–6 reuse existing
machinery (`mj-gtfs`, `gtfs-compatibility`, `git`/`gh`) verbatim — never reimplemented
here.

## STAGE 0 — Preflight (always)

Re-read these hard invariants at the start of every invocation:

- **You will not ask the user anything.** If a decision is ambiguous, apply the rule
  stated in the relevant stage below and proceed. If truly blocked, write a BLOCKED
  report and open the PR anyway — that is a successful run, not a failure.
- **You will write exactly one file**: `docs/city-compat/{slug}.md`. You will not edit
  any other file — no other docs, no application code, no `appsettings.json`, no
  `candidates.md`, no other skill.
- **You will commit only that one file, to a new branch**, and open a PR. You will
  **never** commit to `main` and **never** merge your own PR.
- **No onboarding side effects.** Never invoke `add-transit-city`, never edit
  `CityNames.cs`, either `appsettings.json`'s `Cities:` array, `CityFab.razor`, the
  `_cityCenter` map origin dictionary, or any `.resx` overlay content. Onboarding stays
  separate and human-triggered.

Confirm the working directory is the repo root before proceeding.

## STAGE 1 — Select a city

**Goal:** choose exactly one city that has not been evaluated **and does not already
have an open, unmerged evaluation sitting in a PR.** A merged report and an open PR
both mean "already spoken for" — re-evaluating either wastes a run and, worse, can
produce a second conflicting branch/PR for the same authority. This is why the
done-set below has two sources, not one.

1. **Enumerate the done-set — merged reports.** `Glob docs/city-compat/*.md`. For each
   file, read its `# GTFS Compatibility Report — {AUTHORITY} ({City, Region})` H1 to
   capture the **city + authority** pair.
2. **Enumerate the done-set — open PRs.** Run
   `gh pr list --state open --json number,headRefName,title,body --limit 200`. This
   skill only ever opens branches named `compat/{slug}` (STAGE 6), so:
   - Any PR whose `headRefName` matches `^compat/` is one of this skill's own prior
     runs, regardless of who or what triggered it.
   - Parse the **city + authority** pair from that PR's `body` (it contains the same
     rendered report, so the same `# GTFS Compatibility Report — {AUTHORITY} ({City,
     Region})` H1 is present) rather than from the branch slug or title alone — the
     title is a paraphrase, the H1 is the ground truth already used for the merged-report
     side of the done-set.
   - If a PR's body can't be parsed (edited manually, truncated, etc.), fall back to the
     `compat/{slug}` branch name itself as a weaker key — better to skip a possible
     re-run than to duplicate one.
   - If `gh pr list` fails (auth/network), do not treat that as license to skip this
     check silently — note the failure and proceed using only the merged-report done-set
     for this run, but say so in the run's terminal output so a human knows the open-PR
     check was degraded, not clean.
3. **Union the two sources** into one done-set, deduped by city + authority (the same
   authority can be reachable under more than one name) — this is the set STAGE 1's
   selection arms below check against. An open PR counts as "done" even though nothing
   has merged: the goal is compiling compatible cities without a human having to
   intervene on a merge cadence, not re-discovering the same city daily while its PR
   waits for review.
4. **Curated arm (first).** Read `candidates.md`. Walk the ranked table top-to-bottom;
   pick the first row whose (City, Authority) pair is not in the done-set. Stop there —
   that row is this run's target.
5. **Open-discovery arm (fallback, only if the curated list is fully exhausted — every
   row already done).** `WebSearch` for a large-network NA or EU transit authority not
   in the done-set (e.g. "largest public transit agencies North America Europe GTFS
   realtime"). Prefer agencies likely to publish keyless, standard GTFS-RT protobuf (see
   `references/feed-discovery-playbook.md`). Dedup against the done-set by city and
   authority.
6. Record the chosen **city**, **authority (official name)**, and **slug** (lowercase,
   short, matching the existing convention: `ttc`, `wmata`, `septa`, …). The slug becomes
   the output filename and the report H1's implicit key.

**Slug collision check:** before finalizing the target, also confirm the chosen slug
does not collide with an existing open `compat/{slug}` branch name for a *different*
authority than intended (a stale/renamed candidate). If it collides, this is the same
city — treat as already in the done-set and pick the next candidate instead.

**Branch behavior:** if both arms produce nothing new (curated list exhausted AND
open discovery finds nothing new — checked against the merged+open-PR union), this run
has nothing to do — write **no file**, open **no PR**, and end with a one-line note:
"No unevaluated candidate found (N merged, M open PR(s))." This is the one case where no
report is written.

## STAGE 2 — Find the primary transit authority

**Goal:** resolve the city to the *right* operator. Most metros have multiple operators
(regional rail vs. city bus vs. metro vs. suburban districts) — this is a high-risk step
for a hands-free job.

1. If the `candidates.md` row already named the authority, trust it and skip the search.
2. Otherwise `WebSearch "primary public transit authority {city}"` and apply this
   tie-break rule verbatim: **prefer the operator running the largest bus and/or metro
   network serving the city's urban core; prefer the agency whose GTFS the city is
   popularly identified with; when a regional umbrella and a city operator both exist,
   prefer the one that publishes a unified GTFS-RT vehicle-positions feed.**
3. Record the authority's official name (for the report header) and its developer /
   open-data portal URL if surfaced (feeds STAGE 3).

**Branch behavior:** if the authority is genuinely ambiguous and no single operator
dominates, pick the largest by ridership per the search results and note the ambiguity
in the eventual report's feed-health section — never stall.

## STAGE 3 — Discover feed URLs

**Goal:** obtain up to three URLs `mj-gtfs` can consume: (a) GTFS-RT
**vehicle-positions** protobuf (buses), (b) static GTFS zip, (c) rail realtime API (only
if the agency runs heavy rail). This is where a hands-free job most often fails — read
`references/feed-discovery-playbook.md` for the full search order, feed-type classifier,
and failure→verdict table before acting in this stage.

1. Search order: the agency's own developer/open-data portal → the Mobility Database
   (`mobilitydatabase.org`) → a targeted `WebSearch`.
2. **Verify** any GTFS-RT candidate URL is specifically vehicle-positions, not
   trip-updates/alerts. Path naming is a hint; **zero vehicle entities on decode is the
   authoritative signal**, not the URL.
3. **Detect key-gating.** A feed that 200s only with a registered API key not already
   available in the environment is BLOCKED — classify it **KEY-GATED** (a config-only
   gap: `CityConfig.ApiKeyEnvVar`/`ApiKeyQueryParam` already support a key generically
   once one exists). Do not attempt to register for a key, solve a CAPTCHA, or fabricate
   one.
4. **Static zip**: fetch regardless — usually keyless and public. Note its size.
5. **Rail**: only pursue a rail realtime URL if the static zip has `route_type=1`
   routes (confirmed in STAGE 4) and a separate rail realtime API is plausible. Absence
   of a rail feed is `N/A`, not BLOCKED — the agency can still be bus-compatible.

**Branch behavior — the BLOCKED path (D2):** if no usable vehicle-positions feed is
found by any means, classify the specific failure mode into exactly **KEY-GATED** or
**NO-USABLE-FEED** (no consumable format published at all — nothing, trip-updates/alerts
only, or a proprietary non-GTFS-RT API) per the playbook's failure→verdict table, and go
straight to STAGE 5 with the BLOCKED template — **skip STAGE 4 entirely**. This
classification is required, not optional narrative — it determines both the "Adding as a
data source" framing and the aggregate score's hard ceiling in STAGE 5.

## STAGE 4 — Evaluate (delegate to mj-gtfs + gtfs-compatibility)

**Input:** the URLs from STAGE 3 (only reached if at least the vehicle-positions feed
was found). **Never reimplement fetch/decode logic** — this stage is entirely delegated.

1. Read `.claude/skills/mj-gtfs/SKILL.md` first.
2. **Parallel fetch**, in one tool dispatch: GTFS-RT protobuf → `$env:TEMP\gtfs-rt.pb`;
   static zip → an **agency-slug'd** directory `$env:TEMP\gtfs-{slug}\` (never the
   shared `gtfs-static` name — collision risk across agencies); rail JSON if applicable.
3. Run `mj-gtfs`'s **combined decode + alignment fast path** script once, in a single
   tool call, with `agency` set to the slug. It emits `rt.*` / `static.*` / `alignment.*`
   in one JSON blob.
4. **If `rt._diag_note` is present** (lat/lon decoded as 0%), the position field number
   differs for this feed — follow `mj-gtfs`'s raw-field-inspection path, find the
   correct field, and re-decode before proceeding. **Never write a report off a
   suspected decode bug** — this would falsely label a good feed INCOMPATIBLE.
5. If rail was fetched, decode via `mj-gtfs`'s rail section; confirm the live-position
   check PASSes (one coordinate per train); cross-check `LINE` values against
   `static.rail_index_keys`.
6. Apply the interpretation table from
   `.claude/skills/mj-data-explorer/functions/gtfs-compatibility.md`'s "Interpreting
   partial compatibility" section to produce independent bus and rail verdicts.
7. **Ground-truth checks before finalizing verdicts** — these come from a direct read of
   the platform's actual source
   (`src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker`), not just the
   generic skill docs. If these facts look to have drifted since this skill was
   authored, re-verify against the cited files directly:
   - Before calling any residual `alignment.unmatched_rt_ids` entry "needs new code,"
     test it against the platform's three existing, generic, config-only transforms in
     `Cities/RouteIdNormalizer.cs`: `uppercase` (case normalization), `plusToSbs` (a
     trailing `+` suffix rewritten to `-SBS`), and `stripLeadingZeros`. If one closes the
     gap, the verdict is a config-only fix (`CityConfig.RouteIdNormalization`), not a
     code-change requirement.
   - If rail is anything other than N/A, determine which of the platform's two real rail
     mechanisms applies (see `Cities/CityConfig.cs` and
     `RailRealtime/RailRealtimeAdapter.cs`): a config-only `CityConfig.RailRouteIdMap`
     remap (rail rides the same GTFS-RT feed as buses under different route-ID values —
     zero new code) vs. a bespoke adapter class mirroring `RailRealtimeAdapter.cs` (rail
     positions come from a wholly separate, agency-specific non-GTFS-RT feed format —
     real new code). Name the applicable mechanism explicitly in the report.
   - For any vehicle whose route doesn't resolve, note the platform's actual runtime
     behavior: `Worker.cs`'s `ResolveCategory` deliberately falls back to the literal
     string `"unknown"` for an unmatched route join key — it never silently reuses an
     existing category like Bus. State this real consequence in the report rather than
     only citing a raw skip percentage.
   - Recall (from `data-model.md`'s Ground-truth reference, still true as of this
     skill's authoring): most new cities need **zero new C# code** — the generic
     `Cities/GtfsRtCity.cs` implementation of `ITransitCity`, driven entirely by a
     `CityConfig` record, covers them. A bespoke `ITransitCity` implementation is only
     needed for a feed shape genuinely outside that config surface (e.g. a
     non-GTFS-RT rail API to merge, or a split multi-feed shape).

## STAGE 5 — Write the report

**Input:** either the STAGE 4 JSON + verdicts (COMPATIBLE/PARTIAL path) or the STAGE 3
blocking classification (BLOCKED path). **Output:** exactly one file,
`docs/city-compat/{slug}.md`. Use `references/report-templates.md` for the full
templates — this stage summarizes the required steps.

1. **Compute the aggregate score FIRST**, before drafting any other content. Read
   `references/aggregate-score-formula.md` and follow it exactly:
   - The required-fields-gated, alignment-scaled **bus contribution** (0–70).
   - The rail **mechanism/alignment lookup** (0–20, fixed categorical values only).
   - The **credential lookup** (0–10).
   - Sum them. **On the BLOCKED path only**, apply the hard ceiling: `MIN(raw_score,
     40)` if KEY-GATED, `MIN(raw_score, 15)` if NO-USABLE-FEED.
   - Map the final score to one of the four effort tiers (Drop-in / Minor Config /
     Adapter Needed / Not Viable) per the formula's range table.
   - This score block is the **first content** in the rendered report, above even the
     H1's surrounding context — a reviewer triaging many reports should never have to
     scroll to find it.
2. **Pick exactly one template** — `references/report-templates.md`'s COMPATIBLE/PARTIAL
   template for a successful STAGE 4 evaluation, or its BLOCKED template for a STAGE 3
   dead end. **Never blend the two.** Reaching STAGE 5 via a STAGE 3 dead end is a
   **successful run, not a failure** — the BLOCKED template must still be filled
   completely (including whatever static-only data was reachable) and still result in an
   opened PR.
3. Fill every placeholder from real measured data or the fixed `UNASSESSED`/`N/A`
   tokens — **never invent a number**. The same rule binds every input to the score
   formula: each component must be a real measurement or a categorical fact from STAGE
   3, never a guess. If you find yourself wanting to type a number you didn't get from a
   tool-call output, stop — either you're about to fabricate, or you picked the wrong
   template.
4. This is the run's **only** file write. If any other file appears modified afterward,
   that's a bug in this run — investigate before proceeding to STAGE 6.

## STAGE 6 — Deliver as a PR

**Input:** the single written report file. **Output:** an open PR against `main`
(success path), or a preserved local branch + clear failure note (degraded path). Run
**only** these git/gh actions, in this exact order:

1. **Precondition check**: `git status` must show only the one new report file as
   untracked. If anything else appears, **STOP** — do not proceed with `git add`; this
   indicates either a bug in an earlier stage or unrelated in-progress work that must
   not be swept into this run's commit.
2. `git checkout -b compat/{slug}` off the current `main`.
3. `git add docs/city-compat/{slug}.md` — stage **only** that file.
4. `git commit -m "compat: evaluate {AUTHORITY} ({City})"`.
5. `git push -u origin compat/{slug}`.
6. `gh pr create` targeting `main`; title `Compat report: {AUTHORITY} ({City}) —
   {verdict}`; body includes the per-axis verdict, headline alignment %, the blocking
   reason if BLOCKED, and — verbatim or near-verbatim — the sentence: "Auto-generated by
   the discover-transit-city scheduled routine; review before merging. This does **not**
   onboard the city."

**Branch behavior (degraded path):**

| Failure point | Required behavior |
|---|---|
| `git push` fails (network/auth) | Branch + commit remain local. End the run reporting the branch name, that the commit succeeded, and that push failed with the specific reason. Do not retry indefinitely, do not fall back to `main`. |
| `gh pr create` fails (auth/network/API) | Same as above, but note the push succeeded and only PR creation failed — a human can open the PR manually from the pushed branch. |
| Both succeed | Report the PR URL as the run's terminal output. |

**Never**, under any branch behavior above, fall back to committing on `main`.

## Risk register / non-goals

**Risks this skill is designed around:**

- **Wrong-authority pick (STAGE 2)** — bounded by the tie-break rule and by the PR gate:
  a wrong pick becomes a bad PR a human closes, not a broken `main`.
- **Feed-discovery failure (STAGE 3)** — the dominant failure mode; mitigated by the
  BLOCKED path (D2) so failures become documented negative reports, never crashes or
  fabricated data.
- **Decode field-number drift** — `mj-gtfs` position field numbers vary by publisher; a
  false `lat_lon_pct = 0` would mislabel a good feed. STAGE 4 must honor
  `rt._diag_note` and re-decode before writing anything.
- **Silent duplicate cities** — dedup by city + authority (STAGE 1), not just slug,
  because the same authority can be named several ways.
- **Re-evaluating a city stuck in review** — an unmerged PR from a prior run is still
  "spoken for." STAGE 1 unions merged `docs/city-compat/*.md` reports with open
  `compat/*` PRs (via `gh pr list`) before selecting, so a city sitting in review does
  not get silently re-discovered and re-PR'd on every scheduled run. This is the
  mechanism that lets the job compile a growing backlog of candidate PRs unattended,
  without a human needing to merge (or even look at) each one before the next run.

**Explicit non-goals (do NOT do these, ever):**

- No onboarding. This skill never touches `CityNames.cs`, either `appsettings.json`'s
  `Cities:` array, `CityFab.razor`, the `_cityCenter` map origin dictionary, or any
  `.resx` overlay content. That is `add-transit-city`'s job and stays human-triggered.
- No merging. This skill opens a PR and stops; a human reviews and merges.
- No telemetry queries. Irrelevant for an unonboarded city — it has emitted zero
  telemetry.
- No `main` commits, ever, under any branch behavior.
- No interactive prompts. This skill runs to completion unattended or writes a BLOCKED
  report; it never waits for user input.
