# Contract: Aggregate Compatibility Score Formula

**Purpose**: A single, deterministic 0–100 score (plus a derived effort tier) that opens
every `docs/city-compat/{slug}.md` report — the one number a reviewer needs to triage a
week's worth of candidate reports without reading the rest of the document (FR-012c,
FR-012d, User Story 4). Both report templates (`report-template-compatible.md`,
`report-template-blocked.md`) reference this file rather than duplicating the math — if
the formula ever changes, it changes here once.

**Non-negotiable property**: the formula MUST be fully reproducible. Given the same
measured inputs, it MUST always produce the same score — no subjective judgment, no
randomness, no "use your best guess." Every input into the formula is either a real
measurement from `mj-gtfs`/stage-4, or a categorical fact from stage 3, never an invented
value.

## The three contributions (sum to the pre-ceiling score)

### 1. Bus contribution — 0 to 70 points (required-fields-gated, alignment-scaled)

```
IF required fields (route identifier + live position) are NOT usably present
  (i.e. rt.lat_lon_pct is not high enough that vehicles can actually be snapped —
   treat "usably present" as rt.lat_lon_pct >= 90, mirroring the gtfs-compatibility.md
   PASS/FAIL line for this exact check):
    bus_points = MIN(10, 70 × (rt.lat_lon_pct ÷ 100))
    # capped at 10 regardless of how high lat_lon_pct climbs above the gate threshold on
    # a technicality — the gate failing means the check itself failed, not "almost passed"
ELSE (required fields usably present):
    effective_alignment_pct = alignment.match_pct
    IF an unmatched RT id would be resolved by one of the three existing
       RouteIdNormalizer transforms (uppercase / plusToSbs / stripLeadingZeros):
        effective_alignment_pct = recompute alignment.match_pct crediting that id as matched
    bus_points = 70 × (effective_alignment_pct ÷ 100)
```

Round `bus_points` to one decimal place. This is the largest share of the score (70 of
100) because live vehicle positions + route resolution are the worker's hard,
non-negotiable dependency (`gtfs-compatibility.md`'s "what compatible means" section) —
everything else is comparatively minor.

### 2. Rail contribution — fixed lookup, 0/5/12/20 points

| Rail situation | Points |
|---|---|
| No `route_type=1` routes in static — rail not applicable to this authority | **20** (no penalty for a bus-only agency) |
| Rail present; integrates via a config-only `RailRouteIdMap` remap; alignment verified clean (LINE↔static match ≥ 90%, live-position check PASS) | **20** |
| Rail present; integrates via a config-only `RailRouteIdMap` remap; alignment partial, unverified, or live-position check not run/FAILs | **12** |
| Rail present; would require a bespoke `RailRealtimeAdapter`-style adapter (positions come from a separate, agency-specific non-GTFS-RT feed), regardless of how clean the line-key desk-check looks | **5** |

Exactly one row applies — never interpolate between rows, never average two rows.

### 3. Credential contribution — fixed lookup, 0/10 points

| Credential situation | Points |
|---|---|
| All evaluated feeds (bus, and rail if present) are keyless or already-authorized | **10** |
| Any evaluated feed is key-gated (a credential is required and not already available) | **0** |

A key-gated feed scores 0 here even if every other axis is clean — a credential must
still be obtained before any config-only fix applies (this is exactly the KEY-GATED
distinction FR-012a introduced).

## Pre-ceiling total

```
raw_score = bus_points + rail_points + credential_points
```

For a COMPATIBLE/PARTIAL report, `raw_score` **is** the final aggregate score — no ceiling
applies (there is no blocking classification to cap it).

## Blocked-outcome ceiling (BLOCKED reports only)

A blocked report never measured a live bus feed, so:

```
bus_points = 0   # always — nothing was measured, nothing is credited
rail_points = <apply the same 0/5/12/20 lookup above, using ONLY what a static-data +
               published-line-code desk check could determine (per report-template-
               blocked.md's "Rail line-key alignment" section); if not even a desk check
               was possible, rail_points = 0>
credential_points = 0   # a blocked report has no verified-keyless feed to credit

raw_score = rail_points   # bus and credential contribute nothing

IF blocking classification == KEY-GATED:
    aggregate_score = MIN(raw_score, 40)
ELSE IF blocking classification == NO-USABLE-FEED:
    aggregate_score = MIN(raw_score, 15)
```

The ceiling reflects that a missing live bus feed dominates the score regardless of how
clean the static/rail desk-check looks — even a hypothetically perfect rail desk-check
(20 points) cannot lift a KEY-GATED report above 40 or a NO-USABLE-FEED report above 15.
This is a **cap**, not an additional subtraction — `aggregate_score = MIN(raw_score, cap)`.

## Effort tier mapping (applies to the final `aggregate_score`, both outcome types)

| Score range | Tier | Meaning |
|---|---|---|
| 90–100 | **Drop-in** | Config-only, no code changes anticipated |
| 70–89 | **Minor Config** | Resolvable via configuration alone (e.g. obtaining a key and/or applying an existing identifier transform) — no new code |
| 40–69 | **Adapter Needed** | Requires new integration code (a bespoke city implementation or rail adapter) |
| 0–39 | **Not Viable** | No usable feed format exists, or required fields fundamentally fail — integration impractical without substantial new work |

Boundaries are inclusive on both ends of each range; there is no gap or overlap between
tiers (89 and 90 are adjacent tiers, never both, never neither).

## Worked examples (for calibration — not fixtures the skill reads at runtime)

**TTC-shaped case** (buses COMPATIBLE 99.4%, rail N/A, keyless):
`bus = 70 × 0.994 = 69.6` + `rail = 20` (N/A) + `credential = 10` (keyless) = **99.6 →
Drop-in**. Matches the real `ttc.md` verdict qualitatively (clean, keyless, near-perfect
alignment).

**CTA-shaped case** (buses NO-USABLE-FEED, rail line-keys would desk-check to a clean
100% match but require a bespoke adapter):
`bus = 0` (blocked) + `rail = 5` (bespoke adapter needed, even though the desk-check
alignment is clean — the mechanism, not the alignment quality, sets this row) +
`credential = 0` (blocked) = `raw = 5`; classification is NO-USABLE-FEED → `MIN(5, 15) =
5 → Not Viable`. Matches the real `cta.md` bottom line ("two new protocol adapters...
not a config swap").

**A hypothetical WMATA-shaped case** (buses COMPATIBLE ~95%, rail present via a
config-only `RailRouteIdMap` remap with clean alignment, keyless):
`bus = 70 × 0.95 = 66.5` + `rail = 20` (config-only, clean) + `credential = 10` (keyless)
= **96.5 → Drop-in**.

## Field-source reference

| Formula input | Comes from |
|---|---|
| `rt.lat_lon_pct` | `mj-gtfs` combined decode script's `rt` object — same field already in Vehicle positions section |
| `alignment.match_pct`, unmatched-ID normalizer check | `mj-gtfs` combined decode script's `alignment` object, cross-checked against `RouteIdNormalizer`'s three transforms per FR-008a |
| Rail situation row | Stage 4's rail decode + line-key cross-check (COMPATIBLE path) or stage 3's static-only desk-check (BLOCKED path) |
| Credential situation | Stage 3's key-gating detection |
| Blocking classification | Stage 3's KEY-GATED / NO-USABLE-FEED determination (FR-012a) |

Any input you cannot source from an actual measurement or a stage-3 categorical finding
is a sign you're about to fabricate part of the score — stop and re-verify before writing
a number.
