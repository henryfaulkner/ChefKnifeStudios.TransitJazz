# Report Template — BLOCKED outcome

**Use this template when**: stage 3 could not obtain a usable GTFS-RT vehicle-positions
feed for any reason — none published, trip-updates/alerts only, key-gated with no key
already in hand, or reachable-but-decodes-to-zero-vehicle-entities with no retry budget
left. **A BLOCKED report is a successful run, not a failure** — write it fully and still
open the PR (FR-010, FR-012, D2). Do not leave a run silent just because the headline
outcome is negative.

**Every BLOCKED report MUST classify into exactly one of two sub-reasons (FR-012a)** —
this is not optional narrative color, it changes the effort signal a reviewer takes away:

| Sub-reason | Meaning | Future effort implied |
|---|---|---|
| **KEY-GATED** | A real-time feed exists in a consumable format (standard GTFS-RT protobuf), but reaching it requires a registered API key this run does not already have in the environment. The platform's generic city path already supports passing a key via a query parameter or header — nothing about this is structurally incompatible. | **Config-only** once a key is obtained — no new code, just an `ApiKeyEnvVar`/`ApiKeyQueryParam` config entry. |
| **NO-USABLE-FEED** | No real-time feed of a consumable format exists at all — nothing published, or only trip-updates/alerts (no vehicle positions), or a proprietary non-GTFS-RT API (e.g. XML/JSON) with no protobuf equivalent. | **New integration code required** (a bespoke adapter), independent of and in addition to any key acquisition. |

Pick exactly one sub-reason before drafting the rest of the report — it determines how the
"Adding as a data source" and "Bottom line" sections should be framed below, AND it sets
the hard ceiling on the aggregate score below (KEY-GATED caps at 40, NO-USABLE-FEED caps
at 15 — see `contracts/aggregate-score-formula.md`).

**How to use this file**: copy the Markdown between the `BEGIN TEMPLATE` and `END TEMPLATE`
markers into `docs/city-compat/{slug}.md`, then replace every `<angle-bracket>`
placeholder. Unlike the COMPATIBLE template, several fields here have a **fixed literal
value** (`UNASSESSED`, `N/A`) rather than a measurement slot — use exactly that literal
token, never a guessed number, when the placeholder says so.

**Compute the aggregate score before drafting anything else.** Read
`contracts/aggregate-score-formula.md`'s "Blocked-outcome ceiling" section and follow it
exactly — even a BLOCKED report gets a real number (FR-012c), built only from what was
measurable (the rail desk-check, if any) plus the fixed classification ceiling. The score
and effort tier are the FIRST content in the rendered report, above the H1's context.

---

BEGIN TEMPLATE

```markdown
# GTFS Compatibility Report — <AUTHORITY OFFICIAL NAME> (<City>, <Region>)

> ## <aggregate_score>/100 — <Adapter Needed / Not Viable>
> Bus: 0/70 (blocked — no live feed measured) · Rail: <rail_points>/20 · Credential: 0/10 (blocked)
> Ceiling applied: <"KEY-GATED, capped at 40" / "NO-USABLE-FEED, capped at 15">
> <One clause plain-language summary — e.g. "No usable feed format exists; would need a
>  net-new bespoke adapter regardless of any key." Computed per
>  `contracts/aggregate-score-formula.md`; the rail component (if non-zero) comes from a
>  real static/published-line-code desk-check, never a guess.>

**Evaluated:** <YYYY-MM-DD>

## Feed health

| | |
|---|---|
| **Blocking classification** | **<KEY-GATED / NO-USABLE-FEED>** — see the sub-reason table this template opens with |
| Static GTFS URL | `<static_gtfs_zip_url>` — <"verified live, HTTP 200, <size> zipped / ~<size> unzipped" if fetched, or "not verified" if even static couldn't be reached> |
| GTFS-RT vehicle positions (buses) | **<Exact blocking reason — pick the one that matches the classification above:>** |
| | KEY-GATED: "Exists as standard GTFS-RT protobuf but requires a registered API key not already available in the environment; key acquisition is out of scope for this run. The platform's generic city path already supports a config-only key once obtained." |
| | NO-USABLE-FEED: "Does not exist. No `.pb` protobuf feed is published for <authority> buses." |
| | NO-USABLE-FEED: "Exists but is trip-updates/alerts only — no vehicle-positions endpoint found." |
| | NO-USABLE-FEED: "Only a proprietary non-GTFS-RT API (<format, e.g. XML/JSON>) is published — no protobuf equivalent exists." |
| | (time-of-day caveat, either classification) "Reachable but decoded to 0 vehicle entities at time of check — <note time-of-day / retry budget context>." |
| Rail realtime (trains) | <same reasoning as above, or "N/A — <authority> has no route_type=1 routes in static" if there's no rail to even ask about> |

<1-4 sentences of narrative context: what was searched (developer portal, Mobility
Database, targeted WebSearch), what was found instead (e.g. a proprietary key-gated legacy
API, sibling trip-updates/alerts endpoints), and — if this reveals something structurally
different from a typical "ID mismatch" compatibility problem — say so plainly, mirroring
cta.md's framing ("this is a structurally different problem... there is no protobuf feed
for the worker's decoder to read at all"). Include one representative curl/request +
response snippet ONLY if it concretely demonstrates the blocking reason (e.g. a key-gate
error body) — omit if you have nothing concrete to show.>

## Static GTFS (verified by direct parse)

<!-- Include this section only if the static zip WAS reachable and parsed. If static
     itself also failed, replace this section's body with a single line:
     "Not assessed — static GTFS zip was not reachable (<reason>)." and skip to Verdict. -->

| | |
|---|---|
| Routes | <static.route_count> total, <static.routes_with_shape> have shapes |
| `route_type=3` (bus) | <bus_route_count> routes — <1 clause on ID format, e.g. "route_id and route_short_name match plain rider-facing strings"> |
| `route_type=1` (rail) | <rail_route_count> routes — <comma-separated static.rail_index_keys, or "none"> |

<Only if relevant: 1-2 sentences on any static-data quirk that affects the join key, e.g.
an empty-string route_short_name that the loader already normalizes to null before falling
back to route_id — cite the actual behavior only if you've confirmed it, otherwise omit.>

## Rail line-key alignment (only if determinable from static + a published rail API's line codes, even without a live feed)

<!-- Include ONLY if you found published, non-GTFS-RT documentation of a proprietary rail
     API's line/route parameter values (e.g. a developer-docs page listing valid `rt=`
     codes) that can be compared against static.rail_index_keys WITHOUT needing a live
     fetch. If no such documentation exists, OMIT this entire section — do not force it. -->

<authority>'s <rail API name>'s route parameter uses exactly these codes: <comma-separated
codes>. <State plainly whether these are identical, verbatim, to the static route_type=1
keys above, or would need a transform — this is a desk-check against published docs, not a
live measurement, so say "would be" not "is".>

## Vehicle positions / route ID alignment (buses)

**Not assessable without a live feed.** <1-2 sentences restating the specific blocking
reason from Feed health above — do not repeat the whole paragraph, just enough to explain
why the numbers below are UNASSESSED.> Field completeness (route_id %, lat/lon %,
speed/bearing %) cannot be measured until <what would need to change — e.g. "a key is
obtained and a live sample is pulled">.

## Verdict

- **Buses: INCOMPATIBLE (<KEY-GATED / NO-USABLE-FEED>)** — <one clause citing the specific blocking reason and, if KEY-GATED, noting this is a config-only fix once a key exists>
- **Rail: <INCOMPATIBLE (KEY-GATED / NO-USABLE-FEED) / N/A>** — <one clause, or omit this line if rail is N/A because no route_type=1 routes exist at all>

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **UNASSESSED** — <one clause: no feed reachable without X> |
| Route ID alignment (buses) | **N/A** — nothing to align against |
| Rail line alignment | **<UNASSESSED / "Would PASS (<pct>%, zero transform) — verified from static + published line codes" / N/A>** |
| Blocking classification | **<KEY-GATED / NO-USABLE-FEED>** |

**Bottom line:** <2-4 sentences: what IS clean (e.g. static data, prospective line-key
match) vs. what's blocking (no protobuf feed, key-gating), and what class of work would be
needed to unblock it. If KEY-GATED, be explicit that this is a config-only fix once a key
is obtained — no code change. If NO-USABLE-FEED, be explicit that a bespoke adapter is
needed regardless of any key, and that this is materially more effort than a config-only
onboarding.>

## Adding <authority> as a data source

- **Static GTFS zip:** `<url>` — <"no auth required, drop-in as a config-only CityConfig entry." / describe what's needed>
- **Bus realtime:** `<url or API name>` — <KEY-GATED: "registered API key needed; once obtained, this is a config-only CityConfig entry (ApiKeyEnvVar/ApiKeyQueryParam) — no new code." / NO-USABLE-FEED: "no GTFS-RT equivalent; would need a net-new `ITransitCity` implementation normalizing this API's response shape into the platform's feed format, mirroring the one bespoke city implementation that already does this for a different agency.">
- **Rail realtime:** <url or API name, or "n/a — no rail to onboard"> — <same KEY-GATED/NO-USABLE-FEED treatment as bus realtime; if a live feed exists on the same GTFS-RT stream under different route-ID values, note that a config-only `RailRouteIdMap` entry would suffice instead of a bespoke adapter>
- **Auth:** <describe every credential that would need to be provisioned, and note per the repo's existing precedent that any key must be stored via env/secrets, never committed>
- **Config entry vs. new code:** <KEY-GATED and otherwise standard: "config-only — a CityConfig entry once a key exists, no new `ITransitCity` implementation needed." / NO-USABLE-FEED: "requires a new bespoke `ITransitCity` implementation; there is no existing config-only path for this feed shape.">
- **Effort scope:** <one clause: "config-only once a key is obtained" (KEY-GATED) vs. "new adapter code independent of any key" (NO-USABLE-FEED) vs. "both — a key AND new adapter code" if both problems stack>

## Open items for a follow-up pass

- <Bulleted list of concrete next steps a human with more access/time would take — e.g.
  "Obtain a <authority> developer API key for <X>." / "Pull a live sample once access
  exists; measure route_id/lat-lon/speed/bearing completeness the same way other agencies
  were measured." Keep this list to what's actually actionable — don't pad it.>
```

END TEMPLATE

---

## Field-source reference (for the writing agent, not part of the rendered doc)

| Placeholder | Comes from |
|---|---|
| `<aggregate_score>`, `<rail_points>`, effort tier, ceiling clause | `contracts/aggregate-score-formula.md`'s "Blocked-outcome ceiling" section — compute AFTER deciding the blocking classification (below), since the classification sets the ceiling |
| Blocking classification (KEY-GATED / NO-USABLE-FEED) | Decide this FIRST, before drafting anything else — it is a binary desk-check: does a consumable feed format exist behind a credential (KEY-GATED), or does no consumable format exist regardless of credentials (NO-USABLE-FEED)? Every downstream field in this template follows from this choice, including the score ceiling. |
| Blocking reason (Feed health) | The feed-discovery playbook's failure→verdict classifier — pick the row that matches what was actually found and the classification chosen above, quote it near-verbatim |
| `static.*` | `mj-gtfs`'s static GTFS parse script, if the zip was reachable |
| Rail line codes (desk-check section) | Published developer documentation found during stage 3 WebSearch — only if concretely found, never inferred |
| `UNASSESSED` / `N/A` cells | Fixed literal tokens — never replaced with a number. If you find yourself wanting to write a percentage in one of these cells, you have live data and should be using `report-template-compatible.md` instead (or its PARTIAL case) |
| "Open items" list | Whatever concrete unblockers stage 3's investigation surfaced — not boilerplate. For KEY-GATED, this list is almost always "obtain a developer API key"; for NO-USABLE-FEED, it should describe what a bespoke adapter would need to normalize. |
