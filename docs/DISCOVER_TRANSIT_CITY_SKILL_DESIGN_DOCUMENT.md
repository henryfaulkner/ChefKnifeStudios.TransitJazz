# Design Document — `discover-transit-city` Skill

**Status:** Design (not yet built)
**Author:** design pass, 2026-07-25
**Audience:** a fresh agent session tasked with *building* this skill. This document is
written to be self-contained — you should be able to build the skill from this file plus
the four referenced skills and one exemplar doc, without re-deriving the pipeline.

---

## 1. What this skill is

`discover-transit-city` is a **hands-free, CRON-driven orchestrator** that, on each run,
autonomously:

1. **Selects** a North American or European city not already evaluated.
2. **Finds** that city's primary transit authority.
3. **Discovers** the authority's public GTFS-RT / static-GTFS / rail-realtime feed URLs.
4. **Evaluates** TransitJazz compatibility by fetching + decoding those feeds.
5. **Writes** a compatibility report to `docs/city-compat/{slug}.md`.
6. **Delivers** the report on a new git branch as a pull request — never touching `main`,
   never editing application code, never onboarding the city.

It is meant to be invoked with **zero arguments** by a scheduled cloud routine
(`/schedule`), and to complete **without any human interaction**. This is the defining
constraint and it shapes every decision below: **the skill must never ask the user a
question, never wait for input, and must degrade to a written negative report rather than
stalling or guessing.**

### 1.1 Relationship to existing skills — read these first

This skill is an **orchestrator that delegates**. It contains genuinely new logic only for
stages 1–3 (select / find authority / discover feeds). Stages 4–6 reuse machinery that
already exists. Before building, read:

| Skill / file | Path | What you reuse from it |
|---|---|---|
| `mj-gtfs` | `.claude/skills/mj-gtfs/SKILL.md` | The **fetch + decode engine**. Its PowerShell + pure-Python scripts download and decode the GTFS-RT protobuf, static zip, and rail JSON, and its "combined decode + alignment fast path" emits one JSON blob with RT decode + static parse + route-ID alignment. **Do not reimplement any of this.** |
| `gtfs-compatibility` function | `.claude/skills/mj-data-explorer/functions/gtfs-compatibility.md` | The **evaluation logic + report template**. Defines what "compatible" means for the worker (route_id + lat/lon required; route-ID alignment against `routeShortName ?? routeId`; rail as a third independent axis), the interpretation table, and the report format. Stage 4 *is* this function, minus its "ask the user for URLs" step (stage 3 supplies them instead). |
| `add-transit-city` | `.claude/skills/add-transit-city/SKILL.md` | The **style template** for how an orchestrator skill is written in this repo (imperative checklist, ordered stages, delegates to sub-skills). Mirror its tone. Also the authority on the "compat check happens *before* any onboarding, and onboarding is human-triggered" boundary this skill must respect. |
| `mj-data-explorer` | `.claude/skills/mj-data-explorer/SKILL.md` | **Anti-pattern reference.** This is the *conversational* front door — "ask open questions, one step at a time." `discover-transit-city` is the opposite: imperative, autonomous, no questions. Do **not** route through this skill; it would stall waiting for input. |

Exemplar compat docs (the output target shape):
`docs/city-compat/ttc.md` (a clean COMPATIBLE example),
`docs/city-compat/cta.md` (**the canonical BLOCKED/negative-report example — no GTFS-RT
feed exists, keys required; copy its structure for the no-feed case**),
`docs/city-compat/wmata.md`, `docs/city-compat/mbta.md`, `docs/city-compat/nymta.md`.

### 1.2 Why not just call `/mj-data-explorer`?

The original request said "use `/mj-data-explorer` to evaluate compatibility," but that
skill is a conversational router whose UX contract is *ask the user, infer intent, one step
at a time.* That is fundamentally incompatible with a hands-free CRON job — it would block
on the opening free-text question. The compat *logic* it routes to
(`functions/gtfs-compatibility.md`) is what we actually want, and that in turn drives
`mj-gtfs`. So this skill bypasses the conversational shell and drives the compatibility
function + `mj-gtfs` directly. Telemetry (the `telemetry-query-bridge` MCP tool that
`mj-data-explorer` also fronts) is **irrelevant here**: a brand-new candidate city has
emitted zero telemetry, so there is nothing to query. Compatibility is purely a
feed-fetch-and-decode question.

---

## 2. Design decisions (locked)

These were decided by the requesting user and are **not open for reinterpretation** by the
builder:

| # | Decision | Value | Consequence for the build |
|---|---|---|---|
| D1 | **City selection strategy** | **Hybrid** | Curated ranked list first; when exhausted, fall back to open `WebSearch` discovery. Requires a bundled `candidates.md`. |
| D2 | **No usable feed found** | **Write a negative report** | On key-gated / trip-updates-only / no-vehicle-positions feeds, still write `docs/city-compat/{slug}.md` documenting *why* it's blocked (mirror `cta.md`). Do not silently skip. |
| D3 | **Output boundary** | **Doc on a branch + PR** | Write the doc, commit only that one file to a new branch, open a PR. Never commit to `main`. Never run the `add-transit-city` onboarding flow. Never edit app code. |
| D4 | **Scheduler** | **Cloud routine via `/schedule`** | The skill itself is scheduler-agnostic, but the deliverable includes creating a weekly `/schedule` routine whose prompt is `/discover-transit-city`. |

Two standing repo constraints also bind this skill:

- **Never auto-commit to `main`.** (Repo memory `feedback_never_auto_commit` + `AGENTS.md`.)
  The PR flow is the *only* sanctioned git action, and even that must land on a *new
  branch*, never `main`. Committing the single doc file to a feature branch is acceptable
  because it is explicitly the user-chosen deliverable (D3) — but the branch must never be
  merged by the skill.
- **Root namespace is `ChefKnifeStudios.TransitJazz.*`** even though the repo folder is
  `TransitJazz`. Irrelevant to this skill (it writes docs, not code) but noted so the
  builder isn't confused by paths.

---

## 3. Skill file layout

Create under `.claude/skills/discover-transit-city/`:

```
discover-transit-city/
├── SKILL.md                       # orchestrator: the 6 stages, imperative checklist
├── candidates.md                  # curated ranked NA/EU candidate pool (D1 first arm)
└── references/
    ├── feed-discovery-playbook.md # STAGE 3 detail: how to find feeds, failure modes
    └── report-templates.md        # the COMPATIBLE and BLOCKED doc templates + verdict rules
```

Keep `SKILL.md` lean — it is the always-loaded router. Push the heavy detail (the
feed-discovery failure taxonomy, the full report templates) into `references/` so they load
only when the relevant stage runs. This mirrors the progressive-disclosure pattern
`mj-data-explorer` uses (SKILL.md routes; `functions/` and `references/` hold the depth).

### 3.1 SKILL.md frontmatter

```markdown
---
name: discover-transit-city
description: Hands-free CRON orchestrator that autonomously picks an unevaluated North American or European city, finds its primary transit authority, discovers its GTFS-RT/static/rail feed URLs, evaluates TransitJazz compatibility via mj-gtfs, writes docs/city-compat/{slug}.md, and opens a PR. Use when the user says "discover a transit city", "run the city discovery job", "find a new compatible agency", or when invoked with no arguments by a scheduled routine.
---
```

The `description` must make clear this is autonomous and CRON-oriented, so the router picks
it for the scheduled invocation and *not* for interactive "add this specific city" requests
(those go to `add-transit-city`).

---

## 4. The six stages (this is the body of SKILL.md)

Write SKILL.md as an ordered, imperative checklist — like `add-transit-city`, not like
`mj-data-explorer`. Each stage below gives the intent, the exact actions, and the
**failure/branch behavior** (critical, because there is no human to recover).

### STAGE 0 — Preflight (always)

- State up front, in the skill body, the **hard invariants** so the running agent re-reads
  them every invocation:
  - "You will not ask the user anything. If a decision is ambiguous, apply the rule stated
    here and proceed; if truly blocked, write a BLOCKED report and open the PR anyway."
  - "You will write exactly **one** file: `docs/city-compat/{slug}.md`. You will not edit
    any other file, any app code, any appsettings, or any other skill."
  - "You will commit only that one file, to a **new** branch, and open a PR. You will never
    commit to `main` and never merge."
- Confirm working directory is the repo root
  (`C:\Projects\ChefKnifeStudios.TransitJazz`).

### STAGE 1 — Select a city (hybrid, D1)

**Goal:** choose exactly one city that has *not* been evaluated.

1. **Enumerate the done-set.** `Glob docs/city-compat/*.md`. Each filename stem is an
   already-evaluated agency slug (`ttc`, `cta`, `wmata`, `mbta`, `nymta`, …). Read the
   `# GTFS Compatibility Report — X (City)` H1 of each to also capture the **city name**,
   because dedup must be by *city/authority*, not just slug (avoid re-evaluating "NYC MTA"
   as "nymta" then again as "mta-nyc").
2. **Curated arm (first).** Read `candidates.md`. Walk the ranked list top-to-bottom; pick
   the first entry whose city **and** authority are not in the done-set. Stop at the first
   match — that is this run's target.
3. **Open-discovery arm (fallback, only if curated list is fully exhausted).**
   `WebSearch` for a large-network NA or EU transit authority not in the done-set (e.g.
   "largest public transit agencies North America Europe GTFS realtime"). Prefer agencies
   known to publish **keyless, standard GTFS-RT protobuf** (see playbook). Dedup against the
   done-set by city and authority. Pick one.
4. Record the chosen **city**, **authority (official name)**, and **slug** (lowercase, short,
   matching the existing convention: `ttc`, `wmata`, …). The slug becomes the output
   filename and the report H1.

**Branch behavior:** if both arms fail to produce an unevaluated city (list exhausted AND
web discovery finds nothing new), this run has nothing to do — write nothing, open no PR,
and end with a one-line note "no unevaluated candidate found." (This is the one case where
no file is written.)

### STAGE 2 — Find the primary transit authority

**Goal:** resolve the city to the *right* operator. This is a **high-risk step** for a
hands-free job because most metros have multiple operators (regional rail vs. city bus vs.
metro vs. suburban districts).

1. If the curated `candidates.md` entry already names the authority (it should — see §5),
   trust it and skip the search.
2. Otherwise `WebSearch "primary public transit authority {city}"` and apply the **tie-break
   rule** (state it verbatim in SKILL.md): *"Pick the operator running the largest bus
   and/or metro network serving the city's urban core. Prefer the agency whose GTFS the city
   is popularly identified with. When a regional umbrella and a city operator both exist,
   choose the one that publishes a unified GTFS-RT vehicle-positions feed."*
3. Record the authority's **official name** (for the report header) and its **developer /
   open-data portal URL** if surfaced (feeds it into stage 3).

**Branch behavior:** if the authority is genuinely ambiguous and no single operator
dominates, pick the largest by ridership per the search results and **note the ambiguity in
the report's feed-health section** — do not stall.

### STAGE 3 — Discover feed URLs

**Goal:** obtain up to three URLs `mj-gtfs` can consume:
(a) GTFS-RT **vehicle-positions** protobuf (buses), (b) static GTFS **zip**, (c) rail
realtime API (only if the agency runs heavy rail). **This is where a hands-free job most
often fails**, so this stage has the richest failure taxonomy — see
`references/feed-discovery-playbook.md`.

1. Search the agency's developer portal / open-data site / the Mobility Database
   (`mobilitydatabase.org`, successor to TransitFeeds) for the three URLs.
2. **Verify the GTFS-RT URL is actually vehicle positions, not trip-updates or alerts.**
   Many agencies publish `/tripupdates` and `/alerts` but no `/vehiclepositions`. A feed
   without vehicle positions is **BLOCKED** (the worker needs live lat/lon).
3. **Detect key-gating.** If the feed 200s only with a registered API key the job cannot
   obtain, that is **BLOCKED** (mirror `cta.md`). Do not attempt to register for a key.
4. **Static zip** is almost always keyless and public; note its size.
5. **Rail:** only pursue a rail realtime URL if the static zip has `route_type=1` routes
   (detected in stage 4) *and* the agency publishes a separate live train API. Absence of a
   rail feed is `N/A`, not BLOCKED — the agency can still be bus-compatible.

**Branch behavior — the BLOCKED path (D2):** if after a reasonable search no usable
GTFS-RT vehicle-positions feed exists (none published, trip-updates only, or key-gated),
**skip stage 4** and go straight to stage 5 to write a **negative report** using the BLOCKED
template. The report must state the specific reason and cite what *was* found (static zip
health, any proprietary APIs, whether rail line-keys would align) — exactly as `cta.md`
does. A BLOCKED report is a successful run, not a failure.

### STAGE 4 — Evaluate (delegate to mj-gtfs + gtfs-compatibility)

**Goal:** produce the compatibility numbers. **Do not reimplement any fetch/decode logic —
read `mj-gtfs` and run its scripts.**

1. Read `.claude/skills/mj-gtfs/SKILL.md`.
2. **Parallel fetch** (single parallel tool call): download the GTFS-RT protobuf to
   `$env:TEMP\gtfs-rt.pb`, download + extract the static zip to `$env:TEMP\gtfs-{slug}\`
   (use the **agency-slug directory**, never the shared `gtfs-static` name — the skill warns
   about silent partial-write collisions), and if a rail URL exists, fetch it too.
3. **Combined decode + align** (single tool call): run `mj-gtfs`'s "combined decode +
   alignment fast path" script. It emits one JSON blob: `rt.*` (RT decode + field
   completeness), `static.*` (route counts, `rail_route_count`, `rail_index_keys`,
   `index_keys`), and `alignment.*` (`match_pct`, `unmatched_rt_ids`, `static_only_sample`).
   - Set the script's `agency` variable to the slug.
   - **If `rt._diag_note` is present** (lat/lon decoded as 0%), the position field number
     differs for this feed — follow `mj-gtfs`'s raw-field-inspection path to find the right
     field, then re-run. Do **not** write a report with `lat_lon_pct = 0` from a decode bug;
     that would falsely label a good feed INCOMPATIBLE.
4. **Rail (if fetched):** decode the rail JSON via `mj-gtfs`'s rail section (different
   format — JSON, one row per train×station). Confirm the live-position check PASSes (one
   coord per `TRAIN_ID`) and cross-check `LINE` values against `static.rail_index_keys`.
5. Apply the **interpretation table** from `gtfs-compatibility.md` (§ "Interpreting partial
   compatibility") to turn the numbers into per-axis verdicts:
   `COMPATIBLE / PARTIALLY COMPATIBLE / INCOMPATIBLE / N/A` for buses and rail independently.

### STAGE 5 — Write the report

**Goal:** write `docs/city-compat/{slug}.md` in the exact house style.

- Use `references/report-templates.md`. Pick the **COMPATIBLE/PARTIAL template** (from stage
  4 numbers) or the **BLOCKED template** (from a stage-3 dead end).
- Match the existing docs' Markdown shape: an H1 `# GTFS Compatibility Report — {AUTHORITY}
  ({City, Region})`, a bold `**Evaluated:** {YYYY-MM-DD}` line, then `## Feed health`,
  `## Vehicle positions (GTFS-RT)`, `## Route ID alignment`, `## Rail`, `## Verdict` (with
  the per-check table), and `## Adding {authority} as a data source`. Study `ttc.md` for the
  positive shape and `cta.md` for the negative shape and follow them closely — reviewers
  expect these docs to look uniform.
- Fill every number from the stage-4 JSON. **Never invent a percentage** — if a value wasn't
  measured (e.g. blocked before fetch), write `UNASSESSED` / `N/A`, exactly as `cta.md` does.

### STAGE 6 — Deliver as a PR (D3)

**Goal:** surface the report for human review without touching `main`.

Run these git actions (and **only** these). Use the `gh` CLI for the PR.

1. `git checkout -b compat/{slug}` off the current `main` (the branch name convention:
   `compat/` prefix + slug).
2. `git add docs/city-compat/{slug}.md` — **stage only that one file.** If `git status`
   shows any other modified/created file, something went wrong upstream; do not add it.
3. `git commit` the single file. Commit message: `compat: evaluate {AUTHORITY} ({City})`.
   Follow the repo's commit trailer convention (see any recent commit / `CLAUDE.md`).
4. `git push -u origin compat/{slug}`.
5. `gh pr create` targeting `main`, title `Compat report: {AUTHORITY} ({City}) — {verdict}`,
   body = a short summary: chosen city + authority, the per-axis verdict, the headline
   alignment %, and (if BLOCKED) the blocking reason. Include the sentence "Auto-generated
   by the discover-transit-city scheduled routine; review before merging. This does **not**
   onboard the city."

**Branch behavior:** if `git push` or `gh pr create` fails (auth, network), leave the branch
committed locally and end with a clear note of what failed and the branch name, so a human
can push it. Never fall back to committing on `main`.

---

## 5. `candidates.md` — the curated pool (D1 first arm)

A ranked Markdown table the skill walks top-to-bottom. **Rank by likelihood of a clean,
keyless, standard-GTFS-RT-protobuf result** (so early runs produce good PRs and build
confidence), and pre-fill any *known* feed URLs to save the agent a search in stages 2–3.

Required columns: `Rank | City | Authority (official name) | Region | Known static zip URL |
Known GTFS-RT vehicle-positions URL | Rail? | Notes`. Leave URL cells blank where unknown —
stage 3 will discover them; a filled cell is a shortcut, not a requirement.

**Seed the list** (builder: verify each URL at build time; treat these as starting research,
not gospel — feed URLs rot):

- **North America:** SEPTA (Philadelphia), TriMet (Portland), SFMTA / Muni (San Francisco),
  RTD (Denver), King County Metro (Seattle), Metro Transit (Minneapolis–St. Paul), MTS (San
  Diego), TransLink (Vancouver), STM (Montréal), OC Transpo (Ottawa), MARTA is **already
  done**, so are WMATA/MBTA/NYMTA/TTC/CTA — exclude them.
- **Europe:** TfL (London), RATP / Île-de-France Mobilités (Paris), VBB (Berlin-Brandenburg),
  MVV (Munich), ATM (Milan), EMT (Madrid), STIB/MIVB (Brussels), RET (Rotterdam), HSL
  (Helsinki), Ruter (Oslo), Transport for Ireland (Dublin).

> **Builder note:** several EU agencies gate GTFS-RT behind free registration (TfL, IDFM) —
> those will likely land as BLOCKED on the first automated pass, which is fine and expected
> per D2. Rank the known-keyless NA agencies (SEPTA, TriMet, RTD) at the top so the earliest
> scheduled runs yield clean COMPATIBLE PRs.

Keep the done-set exclusions accurate: at authoring time the evaluated set is
`{marta, mbta, wmata, nymta, ttc, cta}` (marta is the app's home agency; the other five have
docs in `docs/city-compat/`).

---

## 6. `references/feed-discovery-playbook.md` — the load-bearing new content

Stages 4–6 are near-free (delegated or mechanical); **stage 3 is the whole ballgame** for a
hands-free job, so invest the most authoring effort here. This file must teach the agent to
*find* feeds and, crucially, to *correctly classify a dead end* rather than fabricate a
result. Contents:

1. **Where to look**, in order: the agency's own developer/open-data portal → the Mobility
   Database (`mobilitydatabase.org`) → a targeted `WebSearch` for
   `"{agency} GTFS realtime vehicle positions"`. Prefer the agency's canonical URL over a
   third-party mirror.
2. **The three feed types and how to tell them apart.** A GTFS-RT endpoint may be
   `vehicle-positions`, `trip-updates`, or `service-alerts`. **Only vehicle-positions drives
   the worker.** Endpoint paths often name the type (`/vehiclepositions`, `/vehicles` vs.
   `/tripupdates`, `/alerts`). If unsure, `mj-gtfs`'s decode reports vehicle-entity count —
   zero vehicle entities means it's not a positions feed.
3. **Failure-mode → verdict table** (the classifier). Each row: symptom → verdict → what to
   write. Cover at minimum:
   - No GTFS-RT feed published at all → **BLOCKED (no realtime feed)**; write the static
     health + note it's static-only. (This is `cta.md`.)
   - GTFS-RT exists but trip-updates/alerts only, no vehicle positions → **BLOCKED (no
     vehicle positions)**.
   - Feed 200s only with a registered API key → **BLOCKED (key-gated)**; do not register.
   - Feed reachable but 0 vehicle entities at this time of day → note the time-of-day
     caveat; if a retry pattern is cheap, note it, else BLOCKED-provisional.
   - Feed reachable, decodes, but 0% route_id → **INCOMPATIBLE (likely fixable transform)**
     per the gtfs-compatibility table; still write a full report.
   - Static zip 404 / moved (CKAN resource IDs rotate — see the TTC note in `ttc.md`) →
     retry the portal's current link; if unrecoverable, BLOCKED with the stale-URL reason.
4. **The "do not fabricate" rule, stated bluntly:** every number in the report must come from
   an actual `mj-gtfs` decode of a real download. If a feed couldn't be fetched, the
   corresponding fields are `UNASSESSED`, never a guessed percentage. `cta.md` is the model:
   it says "Not assessable without a live feed" rather than inventing coverage stats.
5. **Auth boundary:** the job may pass a key it *already has in the environment* (rare) but
   must **never** create accounts, solve registration flows, or fabricate keys. Key-gated =
   BLOCKED, full stop.

---

## 7. `references/report-templates.md`

Two ready-to-fill templates plus the verdict rules. Derive both from the live exemplars so
they stay in the house style:

- **COMPATIBLE / PARTIAL template** — structurally identical to `ttc.md` /`wmata.md`
  /`mbta.md`: `Feed health` table, `Vehicle positions (GTFS-RT)` table, `Route ID alignment`
  table, optional `Rail` section (omit if no `route_type=1`), `Verdict` prose + the per-check
  results table (`Required fields | Route ID alignment | Rail line alignment`), and
  `Adding {authority} as a data source`.
- **BLOCKED template** — structurally identical to `cta.md`: state plainly at the top that no
  usable GTFS-RT feed exists and why; still report the **static** health (it's usually fetch-
  able and valuable); still assess **rail line-key alignment from static + published line
  codes** if determinable (cta.md does this and finds a 100% would-be match); a `Verdict` that
  marks the unfetchable axes `UNASSESSED` / `INCOMPATIBLE (as GTFS-RT)`; and an
  `Adding … as a data source` section describing the adapter work + keys that *would* be
  required. End with an `## Open items for a follow-up pass` list (obtain key, pull live
  sample, measure completeness) — again mirroring `cta.md`.
- **Verdict rules:** copy the interpretation table verbatim from
  `gtfs-compatibility.md` (§ "Interpreting partial compatibility") — buses and rail are
  scored on **independent** axes; an agency can be bus-COMPATIBLE and rail-N/A (that's TTC),
  or both-INCOMPATIBLE-as-GTFS-RT (that's CTA).

---

## 8. The `/schedule` routine (deliverable D4)

Separate from the skill files: create a weekly cloud routine so the whole thing runs
hands-free.

- **Cadence:** weekly (e.g. Monday 06:00 local). *Not* daily — the curated pool is ~20
  cities; daily would exhaust it in three weeks and then lean entirely on open discovery.
  Weekly gives ~5 months of curated runs.
- **Prompt:** exactly `/discover-transit-city`.
- **Guardrails to bake into the routine prompt** (belt-and-suspenders with STAGE 0):
  "Write exactly one file under docs/city-compat/. Commit only that file to a new
  `compat/{slug}` branch. Open a PR to main. Never commit to main, never merge, never edit
  application code, never run the add-transit-city onboarding flow."
- **Why cloud, not local `/loop` or OS Task Scheduler:** the job must fire even when the
  user's laptop is asleep; a local loop dies with the machine (repo runs on Windows 11, see
  `CLAUDE.md`). The cloud routine is the only reliably hands-free option.

---

## 9. Risk register / explicit non-goals

**Risks the builder should surface in SKILL.md so the running agent respects them:**

- **Wrong-authority pick (stage 2)** — mitigated by the tie-break rule and by the PR gate: a
  wrong pick is a bad PR a human closes, not a broken `main`. That bounded blast radius is
  *why* D3 stops at a PR.
- **Feed-discovery failure (stage 3)** — the dominant failure mode; mitigated by the BLOCKED
  path (D2) so failures become documented negative reports instead of crashes or fabricated
  data. Budget the most build effort into the playbook.
- **Decode field-number drift** — `mj-gtfs` position fields vary by publisher; a false
  `lat_lon_pct = 0` would mislabel a good feed. Stage 4 must honor `rt._diag_note` and
  re-decode before writing. Never write a verdict off a suspected decode bug.
- **Silent duplicate cities** — dedup by *city + authority*, not just slug (stage 1),
  because the same authority can be named several ways.

**Non-goals (do NOT build these):**

- No onboarding. The skill never touches `CityNames.cs`, `appsettings.json`, `CityFab.razor`,
  map origins, or overlay text. That is `add-transit-city`'s job and stays human-triggered.
- No merging. The skill opens a PR and stops; a human reviews and merges.
- No telemetry queries. Irrelevant for an unonboarded city (§1.2).
- No `main` commits. Ever.
- No interactive prompts. The skill must run to completion unattended or write a BLOCKED
  report; it must never wait for user input.

---

## 10. Build checklist (what "done" looks like)

1. `.claude/skills/discover-transit-city/SKILL.md` — frontmatter (§3.1) + the six-stage
   imperative checklist (§4) + the STAGE-0 invariants + the §9 risk callouts. Lean; depth
   lives in `references/`.
2. `.claude/skills/discover-transit-city/candidates.md` — ranked table (§5), keyless-NA
   agencies at the top, done-set excluded, URLs pre-filled where known/verified.
3. `.claude/skills/discover-transit-city/references/feed-discovery-playbook.md` — the
   find-feeds guidance + failure→verdict classifier + the do-not-fabricate rule (§6).
4. `.claude/skills/discover-transit-city/references/report-templates.md` — COMPATIBLE and
   BLOCKED templates + verdict rules, derived from `ttc.md` / `cta.md` /
   `gtfs-compatibility.md` (§7).
5. **Manual dry-run before scheduling** — invoke `/discover-transit-city` once by hand and
   confirm the full chain: it picks SEPTA (or the top unevaluated candidate), finds feeds,
   decodes via `mj-gtfs`, writes `docs/city-compat/septa.md` in-style, and opens a PR touching
   only that file. **Do this before creating the routine** — it is the only way to prove
   stage 3 finds feeds without a human, and it costs one run to learn.
6. Create the weekly `/schedule` routine (§8) with the guardrail prompt.

**Acceptance:** a fresh scheduled run, with no human present, produces either (a) a PR adding
exactly one `docs/city-compat/{slug}.md` with real decoded numbers and a per-axis verdict, or
(b) a PR adding exactly one BLOCKED report explaining why the agency isn't reachable — and in
no case a commit to `main`, an edit to app code, or a stall waiting for input.
```
