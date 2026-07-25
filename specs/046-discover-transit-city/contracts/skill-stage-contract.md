# Contract: Six-Stage Orchestration

This documents the input/output contract for each of the skill's six stages, so
`SKILL.md` can be written as a lean imperative checklist (per the design doc's instruction
to push depth into `references/`) while this file is the traceable source of truth for
what each stage must consume and produce. Mirrors design doc §4.

## STAGE 0 — Preflight

**Input**: none (always runs first).
**Output**: none (asserts invariants; does not produce artifacts).
**Contract**:
- Re-states the hard invariants inline (never ask the user anything; write exactly one
  file; commit only that file to a new branch; never touch `main`; never merge).
- Confirms working directory is the repo root.
- On failure: N/A — this stage cannot fail, it only asserts context.

## STAGE 1 — Select a city

**Input**: `.claude/skills/discover-transit-city/candidates.md` (curated pool);
`docs/city-compat/*.md` (evaluated-city record, read live).
**Output**: exactly one `(city, authority, slug)` tuple, OR an explicit "no candidate
found" signal.
**Contract**:
1. Enumerate the done-set by reading every existing report's H1 (city + authority, not
   just filename).
2. Walk `candidates.md` top-to-bottom; first row whose (city, authority) isn't in the
   done-set wins. Stop there.
3. If the curated list is fully exhausted (every row already done), fall back to
   `WebSearch` for a large-network NA/EU authority not in the done-set, preferring
   agencies likely to publish keyless standard GTFS-RT (per the feed-discovery playbook).
4. **Branch behavior**: if both arms produce nothing new, output the "no candidate found"
   signal and the run ends here — no file, no PR (FR-016, SC-002's exception case).

## STAGE 2 — Find the primary transit authority

**Input**: the `city` from stage 1 (and `authority` if the candidates row already named
one).
**Output**: a confirmed authority official name + optional developer/open-data portal URL.
**Contract**:
1. If the candidates row already named the authority, trust it — skip search.
2. Otherwise `WebSearch "primary public transit authority {city}"` and apply the stated
   tie-break rule verbatim (design doc §4 STAGE 2 step 2): prefer the operator with the
   largest urban-core bus/metro network; prefer the one the city's GTFS is popularly
   identified with; prefer the one publishing a unified GTFS-RT vehicle-positions feed
   when a regional umbrella and a city operator both exist.
3. **Branch behavior**: if genuinely ambiguous, pick the largest by ridership per search
   results and note the ambiguity in the eventual report — never stall (FR-004).

## STAGE 3 — Discover feed URLs

**Input**: confirmed `authority` from stage 2.
**Output**: up to three URLs (GTFS-RT vehicle-positions, static GTFS zip, rail realtime),
OR a classified blocking reason (see `references/feed-discovery-playbook.md`'s
failure→verdict table).
**Contract**:
1. Search order: agency's own developer/open-data portal → Mobility Database
   (`mobilitydatabase.org`) → targeted `WebSearch`.
2. Verify any GTFS-RT candidate URL is specifically **vehicle-positions**, not
   trip-updates/alerts (FR-006) — path naming is a hint; zero vehicle entities on decode
   is the authoritative signal.
3. Detect key-gating — a feed that 200s only with a key not already available is BLOCKED,
   full stop, no registration attempted (FR-007). Classify it specifically as **KEY-GATED**
   (FR-012a) if the feed is otherwise a standard, consumable GTFS-RT protobuf — this is a
   config-only gap (the platform's `CityConfig.ApiKeyEnvVar`/`ApiKeyQueryParam` already
   support a key generically once one exists), distinct from a feed that isn't a usable
   format at all.
4. Static zip: fetch regardless (usually keyless); note size.
5. Rail: only pursue a rail URL if a static route_type=1 exists AND a separate rail
   realtime API is plausible; absence of a rail feed is `N/A`, not BLOCKED, by itself.
6. **Branch behavior**: if no usable vehicle-positions feed is found by any means, classify
   the specific failure mode into **KEY-GATED** or **NO-USABLE-FEED** (no consumable format
   published at all — nothing, trip-updates/alerts only, or a proprietary non-GTFS-RT API)
   and go straight to STAGE 5 with the BLOCKED template — skip STAGE 4 entirely (D2). This
   classification is REQUIRED, not optional narrative — it determines which "Adding as a
   data source" framing the BLOCKED template uses.

## STAGE 4 — Evaluate (delegate to `mj-gtfs` + `gtfs-compatibility`)

**Input**: the URLs from stage 3 (only reached if at least the vehicle-positions feed was
found).
**Output**: the combined decode+align JSON (`rt.*`/`static.*`/`alignment.*`), optionally
plus rail decode output, mapped to per-axis verdicts via the interpretation table.
**Contract**:
1. Read `mj-gtfs`'s SKILL.md; never reimplement fetch/decode.
2. Parallel fetch: GTFS-RT protobuf → `$env:TEMP\gtfs-rt.pb`; static zip → agency-slug'd
   `$env:TEMP\gtfs-{slug}\` (never the shared `gtfs-static` name); rail JSON if applicable
   — all in one parallel tool dispatch.
3. Run the combined decode+align script once both fetches complete, in a single tool call,
   with `agency` set to the slug.
4. If `rt._diag_note` is present (lat/lon decoded as 0%), follow the raw-field-inspection
   path and re-run before proceeding — never write a report off a suspected decode bug
   (this is the "false INCOMPATIBLE" risk called out in the spec's edge cases).
5. If rail was fetched, decode via `mj-gtfs`'s rail section; confirm the live-position
   PASS check; cross-check `LINE` against `static.rail_index_keys`.
6. Apply the interpretation table from `gtfs-compatibility.md` to produce independent
   bus/rail verdicts (FR-011).
7. **Ground-truth checks before finalizing verdicts** (FR-008a, FR-011, FR-012b — added
   after reading `TransitDataWorker`'s actual source, not merely the skill docs):
   - Before calling any residual `alignment.unmatched_rt_ids` entry "needs new code," test
     it against the platform's three existing, generic `RouteIdNormalizer` transforms
     (`uppercase`, a trailing-marker-to-suffix rewrite, leading-zero stripping — see
     `Cities/RouteIdNormalizer.cs`). If one closes the gap, the verdict is a config-only
     fix, not a code-change requirement.
   - If rail is anything other than N/A, determine which of the platform's two real rail
     mechanisms applies: a config-only `CityConfig.RailRouteIdMap` remap (rail rides the
     same GTFS-RT feed as buses under different route-ID values) vs. a bespoke adapter
     class mirroring `RailRealtime/RailRealtimeAdapter.cs` (rail positions come from a
     wholly separate, agency-specific feed format). Name it explicitly in the report.
   - For any vehicle whose route doesn't resolve, note that the platform's actual runtime
     behavior (`Worker.cs`'s `ResolveCategory`) tags it an explicit "unknown" category
     rather than silently defaulting it into an existing one (e.g. Bus) — state this real
     consequence rather than only citing a raw skip percentage.

## STAGE 5 — Write the report

**Input**: either the stage-4 JSON+verdicts (COMPATIBLE/PARTIAL path) or the stage-3
blocking classification (BLOCKED path).
**Output**: exactly one file, `docs/city-compat/{slug}.md`.
**Contract**:
1. Pick exactly one template — `contracts/report-template-compatible.md` (as applied into
   `references/report-templates.md`) for a successful stage-4 evaluation, or
   `contracts/report-template-blocked.md` for a stage-3 dead end. **Never blend the two.**
2. **Compute the aggregate score FIRST**, per `contracts/aggregate-score-formula.md`
   (FR-012c/FR-012d): the required-fields-gated bus contribution (0–70), the rail
   mechanism/alignment lookup (0–20), and the credential lookup (0–10), summed and — for
   the BLOCKED path only — capped at 40 (KEY-GATED) or 15 (NO-USABLE-FEED). Map the result
   to one of the four effort tiers. This score is the first content in the rendered
   report, above the H1's context.
3. Fill every placeholder from real measured data or the fixed `UNASSESSED`/`N/A` tokens —
   never invent a number (FR-008/FR-009). The same rule binds the score: every input into
   the formula must be a real measurement or a categorical fact from stage 3, never a guess.
4. This is the run's only file write. If any other file appears modified afterward,
   that's a bug in this run, not an intended side effect.

## STAGE 6 — Deliver as a PR

**Input**: the single written report file.
**Output**: an open PR against `main` (success path), or a preserved local branch +
clear failure note (degraded path).
**Contract** (exact git/gh sequence, design doc §4 STAGE 6):
1. `git checkout -b compat/{slug}` off current `main`.
2. `git add docs/city-compat/{slug}.md` — stage only that file; if `git status` shows any
   other modified/created file, stop and investigate rather than adding it.
3. `git commit` — message `compat: evaluate {AUTHORITY} ({City})`, following repo commit
   trailer convention.
4. `git push -u origin compat/{slug}`.
5. `gh pr create` targeting `main`; title `Compat report: {AUTHORITY} ({City}) — {verdict}`;
   body includes the per-axis verdict, headline alignment %, blocking reason if BLOCKED,
   and the sentence: "Auto-generated by the discover-transit-city scheduled routine;
   review before merging. This does **not** onboard the city."
6. **Branch behavior**: if push or PR creation fails, leave the branch committed locally
   and end with a clear statement of what failed and the branch name — never fall back to
   committing on `main` (FR-017).
