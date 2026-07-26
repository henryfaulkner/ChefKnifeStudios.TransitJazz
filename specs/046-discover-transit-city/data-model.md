# Data Model: Discover Transit City

This feature has no database and no runtime objects in the usual sense — its "data model"
is the shape of the flat files it reads and writes, plus the JSON blob that flows from
`mj-gtfs`'s combined decode script into the report templates. Documented here for
traceability against the spec's Key Entities section.

## Ground-truth reference

What "compatible" concretely means is not defined by this feature or by the design doc —
it is defined by the actual behavior of
`src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker`, the running component a
candidate feed would eventually plug into. The `gtfs-compatibility` function this feature
delegates to already encodes the load-bearing rule (route_id + lat/lon required, alignment
against `RouteJoinKey`); this feature's own contribution is grounding a few report fields
in the worker's *actual* runtime mechanics, confirmed by reading the source directly
(2026-07-25) rather than assuming from the design doc or skill docs alone:

| Ground-truth fact (source file) | What it changes about this feature |
|---|---|
| `ITransitCity` is the extension point; the generic, config-only implementation is `Cities/GtfsRtCity.cs`, driven entirely by a `CityConfig` record (`Cities/CityConfig.cs`) — most new cities need **zero new C# code**, only a `Cities:` array entry in both `appsettings.json` files. | Report templates' "Adding as a data source" sections now describe the actual config shape (`CityConfig`'s fields) instead of a stale, pre-multi-city reference to a single hardcoded `Worker.cs` URL field. |
| `CityConfig.ApiKeyEnvVar`/`ApiKeyQueryParam` already provide a generic, config-only mechanism for a query-param or header API key — a key requirement is not itself a code-level incompatibility. | BLOCKED reports MUST classify into KEY-GATED (config-only once a key exists) vs. NO-USABLE-FEED (no consumable feed format at all, needs new adapter code) — see FR-012a and both report templates. |
| `RouteIdNormalizer.cs` ships three generic, config-driven transforms (`uppercase`, `plusToSbs`, `stripLeadingZeros`) applied via `CityConfig.RouteIdNormalization` — already used by NYMTA in production, zero new code required to reuse them for a new city. | Before a route-ID mismatch is called "needs new code," the compatible-outcome template requires checking it against these three transforms first (FR-008a). |
| `CityConfig.RailRouteIdMap` is a plain per-feed dictionary remap (config-only) — structurally distinct from `RailRealtime/RailRealtimeAdapter.cs`, a bespoke class parsing an agency-specific non-GTFS-RT JSON feed (real code, MARTA-specific today). | The compatible-outcome template's Rail section and the BLOCKED template's rail-realtime line now name which of these two mechanisms a candidate would need (FR-011). |
| `Worker.cs`'s `ResolveCategory` deliberately falls back to the literal string `"unknown"` for an unresolved route join key — never silently reusing an existing category like Bus — per its own inline comment ("an unmatched route is a visible data-quality signal, not silently absorbed into the bus count"). | The compatible-outcome template's route-ID alignment section now states this actual client-visible consequence of a partial match, not just a raw skip percentage (FR-012b). |
| NYMTA (`Cities/NymtaCity.cs` + `Program.cs`'s special-case branch) is the **only** city using a split `GtfsRtUrls` (subway synthesis) / `BusGtfsRtUrls` (plain bus feed) config shape — a bespoke-only pattern, not a generic mechanism. | Explicitly out of scope: stage 3 does not build detection/classification logic for this third shape. If a future candidate looks like it needs it, the report notes it as an open item only (see spec Assumptions). |

None of this changes the feature's boundary (still evaluation-only, still delegates
fetch/decode to `mj-gtfs`) — it only makes the report content precisely match what the
live platform would actually require to onboard a candidate, rather than the more generic
framing the original design doc used.

## Candidate pool entry

**File**: `.claude/skills/discover-transit-city/candidates.md` (one Markdown table)

| Field | Type | Required | Notes |
|---|---|---|---|
| Rank | integer | yes | Walk order, ascending. Lower rank = tried first. |
| City | string | yes | Human city name, used for dedup and report header. |
| Authority (official name) | string | yes | The primary transit operator (design doc §5 seed list already names these). |
| Region | enum: `NA` \| `EU` | yes | Bounds candidate scope per spec Assumptions. |
| Known static zip URL | string (URL) or blank | no | Pre-filled shortcut; blank means stage 3 must discover it. |
| Known GTFS-RT vehicle-positions URL | string (URL) or blank | no | Same as above. Must be verified as vehicle-positions specifically, not trip-updates/alerts (FR-006). |
| Rail? | enum: `Yes` \| `No` \| `Unknown` | yes | Whether the agency is expected to run heavy rail; `Unknown` defers to stage 4's static-zip `route_type=1` detection. |
| Notes | string | no | Free text — e.g. "known key-gated," "CKAN URL rotates." |

**Validation rules**:
- `Rank` values must be unique and dense-ish (gaps are fine; duplicates are not — ambiguous
  walk order).
- `City` + `Authority` pair must not already appear in the evaluated-city record (see
  below) — this is the FR-002 dedup check, applied at read time by the orchestrating skill,
  not stored as a flag in this file.
- Seed content excludes the already-evaluated set at authoring time: `{marta, mbta, wmata,
  nymta, ttc, cta}` (per design doc §5) — the file itself is never required to strike a row
  once evaluated; dedup is computed live against `docs/city-compat/*.md` each run, so the
  candidates file can stay a static seed list without needing an update after every run.

## Evaluated-city record

**Storage**: Derived, not a separate file — it is the **set of existing
`docs/city-compat/*.md` files**, read fresh each run.

| Field | Type | Source |
|---|---|---|
| Slug | string | filename stem, e.g. `ttc` from `ttc.md` |
| City | string | parsed from the report's H1 (`# GTFS Compatibility Report — {AUTHORITY} ({City, Region})`) |
| Authority | string | parsed from the same H1 |

**Validation rules**:
- Dedup MUST be computed by **City + Authority**, read from each report's H1, not merely by
  filename/slug — this directly satisfies FR-002 and the spec's "same authority reachable
  under more than one name" edge case (e.g. never re-add "NYC MTA" as a new slug when
  `nymta.md` already covers the same authority).
- This record is read-only from this feature's perspective within a single run; the *only*
  write this feature ever makes to it is the one new file created in stage 5 of the run
  that just completed.

## Compatibility report (the run's sole output artifact)

**File**: `docs/city-compat/{slug}.md`, one of exactly two shapes, chosen once per run
before any content is drafted:

### Shape A — COMPATIBLE / PARTIAL (see `contracts/report-template-compatible.md`)

| Section | Content origin |
|---|---|
| **Aggregate score + effort tier** (FIRST content, above the H1's context) | `contracts/aggregate-score-formula.md`, computed from `rt.lat_lon_pct` (required-fields gate), `alignment.match_pct` + normalizer-fixability (bus), rail mechanism/alignment, and credential availability — see FR-012c/FR-012d |
| H1 + Evaluated date | Stage 1 (city/authority) + run date |
| Feed health | URLs from stage 3; sizes/counts from `mj-gtfs` fetch |
| Vehicle positions (GTFS-RT) | `rt.*` fields from the combined decode+align JSON |
| Route ID alignment | `alignment.*` fields from the same JSON, PLUS a desk-check of any unmatched IDs against the three existing `RouteIdNormalizer` transforms (FR-008a) and a note on the platform's "unknown category" fallback for any residual mismatch (FR-012b) |
| Rail (omitted entirely if `static.rail_route_count == 0`) | `static.rail_*` + rail-specific decode, only if a rail feed was fetched, PLUS which of the two real integration mechanisms — config-only `RailRouteIdMap` remap or a bespoke `RailRealtimeAdapter`-style class — would apply (FR-011) |
| Verdict | Interpretation-table lookup (from `gtfs-compatibility.md`) applied to the above, independently for buses and rail |
| Adding {authority} as a data source | URLs + auth + transform notes, mechanically derived from what stage 3/4 found, framed against the real `CityConfig`/`ITransitCity` extension shape (config-only entry vs. new bespoke class) |

### Shape B — BLOCKED (see `contracts/report-template-blocked.md`)

| Section | Content origin |
|---|---|
| **Aggregate score + effort tier** (FIRST content, above the H1's context) | `contracts/aggregate-score-formula.md`'s "Blocked-outcome ceiling": bus/credential are always 0 (never measured); rail component only if a static/published-line-code desk-check was possible; hard-capped at 40 (KEY-GATED) or 15 (NO-USABLE-FEED) |
| H1 + Evaluated date | Same as Shape A |
| **Blocking classification** | **Required, decided first**: `KEY-GATED` (a consumable feed format exists behind a credential not already available — config-only fix once obtained) or `NO-USABLE-FEED` (no consumable feed format exists at all — needs new adapter code regardless of credentials) (FR-012a) |
| Top-of-doc blocked statement | The specific stage-3 failure-mode classification (from the feed-discovery playbook's failure→verdict table), phrased consistently with the blocking classification above |
| Static GTFS (if reachable) | Same `static.*` fields as Shape A, when the static zip specifically *was* fetchable even though realtime wasn't |
| Rail line-key alignment (if determinable from static + published line codes) | Cross-check against known line codes even without a live feed, mirroring `cta.md`'s "would PASS" finding |
| Vehicle positions / route ID alignment | Hard-coded `UNASSESSED` narrative — never a percentage |
| Verdict | `INCOMPATIBLE (KEY-GATED)` / `INCOMPATIBLE (NO-USABLE-FEED)` / `UNASSESSED` cells only — no invented pass rate |
| Adding {authority} as a data source | Describes prospective work framed by the blocking classification: a config-only `CityConfig` entry once a key exists (KEY-GATED) vs. a new bespoke `ITransitCity` implementation (NO-USABLE-FEED) |
| Open items for a follow-up pass | Bulleted list of what a future pass with credentials/access would need to do |

**Validation rules (bind both shapes)**:
- Every numeric/percentage field traces to a real `mj-gtfs` decode of a real download
  (FR-008); a field with no measurement MUST render as the literal token `UNASSESSED` or
  `N/A` (FR-009) — never omitted, never guessed.
- Exactly one report file is produced per run that selects a candidate (SC-002); zero
  files are produced on the "no candidate found" edge case (FR-016).
- Every report, both shapes, opens with the aggregate score + effort tier as its first
  content (FR-012c/FR-012d, SC-008); recomputing the formula in `contracts/
  aggregate-score-formula.md` from the report's own measured inputs MUST reproduce that
  same number exactly (SC-009) — the formula is fixed and categorical-lookup-driven
  specifically so this holds without exception.

## Scheduled run

Not a stored entity — a recurring, stateless invocation. Its only "state" carryover
between runs is the evaluated-city record (derived from committed report files) and the
static candidate pool file; the run itself holds no memory beyond one execution.

| Attribute | Value |
|---|---|
| Trigger | `/schedule` cloud routine |
| Cadence | Weekly |
| Invocation prompt | `/discover-transit-city` (zero arguments) |
| Success postcondition | Either exactly one new `docs/city-compat/{slug}.md` delivered via PR, or a clean no-op with a one-line note (no candidate found) |
| Failure postcondition | Local branch + commit preserved, PR/push failure clearly reported; `main` untouched in all cases |

## Template provenance (planning-to-implementation link)

The report templates and the scoring formula they share are authored once, here, as
planning contracts:
- `contracts/report-template-compatible.md`
- `contracts/report-template-blocked.md`
- `contracts/aggregate-score-formula.md` — the single source of truth for the aggregate
  score math both templates reference; never duplicated inline in either template

At implementation time (`/speckit-implement`), all three become the literal body of
`.claude/skills/discover-transit-city/references/report-templates.md` (concatenated, with a
short "pick one template, but always read the formula first" preamble) — no
transformation, no re-derivation. This is the same contract → applied-artifact
relationship `043-toronto-ttc-transit/contracts/city-config.md` had with the actual
`appsettings.json` edits it specified.
