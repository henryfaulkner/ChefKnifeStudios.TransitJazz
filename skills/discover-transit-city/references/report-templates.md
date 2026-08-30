# Report Templates — COMPATIBLE and BLOCKED

Pick exactly one template for this run's outcome; read `aggregate-score-formula.md` first
— both templates require it. Never blend the two templates, and never draft any report
content before the score is computed (it is the first content in the rendered report).

---

# Report Template — COMPATIBLE / PARTIALLY COMPATIBLE outcome

**Use this template when**: stage 3 found a real, keyless (or already-authorized) GTFS-RT
vehicle-position feed, it was successfully fetched, and stage 4's combined decode script
ran to completion (`rt.*` populated, no unresolved `rt._diag_note`). If the feed could not
be fetched or decoded at all, STOP — use the BLOCKED template below instead. Do not use
this template and then fill its numeric fields with guesses; every `<...>` placeholder
below MUST be replaced with a real measured value or the run has picked the wrong template.

**Compute the aggregate score before drafting anything else.** Read
`aggregate-score-formula.md` and follow it exactly — it is the single source of
truth for the scoring math shared by both templates. The score and effort tier are the
FIRST content in the rendered report (FR-012c/FR-012d), above even the H1's context —
placed there deliberately so a reviewer triaging many reports never has to scroll.

**How to use this file**: copy the Markdown between the `BEGIN TEMPLATE` and `END TEMPLATE`
markers into `docs/city-compat/{slug}.md`, then replace every `<angle-bracket>` placeholder.
Do not add, remove, or reorder sections except where a placeholder's own instructions say
a section may be omitted (Rail only). Table row shapes are fixed — do not add or drop
table rows within a section beyond what's shown.

---

BEGIN TEMPLATE

```markdown
# GTFS Compatibility Report — <AUTHORITY OFFICIAL NAME> (<City>, <Region>)

> ## <aggregate_score>/100 — <Drop-in / Minor Config / Adapter Needed / Not Viable>
> Bus: <bus_points>/70 · Rail: <rail_points>/20 · Credential: <credential_points>/10
> <One clause plain-language summary of what the tier means for this specific authority —
>  e.g. "Config-only: apply the existing stripLeadingZeros transform and add a CityConfig
>  entry, no new code." Computed per `aggregate-score-formula.md`; every
>  component above is a real measurement or fixed categorical lookup, never a guess.>

**Evaluated:** <YYYY-MM-DD>

## Feed health

| | |
|---|---|
| GTFS-RT URL | `<gtfs_rt_vehicle_positions_url>` |
| Static GTFS URL | `<static_gtfs_zip_url>` |
| RT feed size | <rt.total_bytes> bytes  •  Header ts: `<rt.header_timestamp or "0 (normal — see note)">` |
| Static routes | <static.route_count> routes / <static.routes_with_shape> with shapes / <static.routes_without_shape> without |

<!-- Optional 1-3 sentence feed-source note: only include if something about the URL/portal
     needs a caveat (e.g. non-obvious gateway, CKAN resource ID rotation risk, sibling
     endpoints that look similar but are NOT vehicle-positions). Omit this note entirely if
     there's nothing notable — do not manufacture a note to fill space. -->

## Vehicle positions (GTFS-RT)

| | |
|---|---|
| Total / vehicle entities | <rt.total_entities> / <rt.vehicle_entities> |
| With `route_id` | **<rt.vehicles_with_route_id> (<rt_with_route_pct>%)** |
| Without `route_id` | **<rt.vehicles_without_route_id> (<rt_without_route_pct>%)** ← out-of-service / deadheading; skipped as `skippedNoRouteId` |
| lat/lon present | **<rt.lat_lon_pct>%** |
| speed present | <rt.speed_pct>% (optional — degrades gracefully) |
| bearing present | <rt.bearing_pct>% |
| vehicle.timestamp | <rt.timestamp_pct>% |

<One to two sentences: what kind of feed this is (surface-only / includes rail / bus+tram
mix), and the practical headline — e.g. how many live, route-attributed vehicles remain
after dropping route-less entities. Every number here must appear in the table above —
do not introduce a new figure only in prose.>

## Route ID alignment (<buses / buses + streetcars / etc. — match what stage 4 actually aligned>)

| | |
|---|---|
| RT distinct route IDs | <alignment.rt_distinct> |
| Static index keys (`route_short_name ?? route_id`) | <alignment.static_keys> |
| **Matched (as-is)** | **<alignment.matched> (<alignment.match_pct>%)** |
| Unmatched RT IDs | <alignment.unmatched_rt_ids, comma-separated, or "none"> |
| Static-only keys | <alignment.static_only_total> (<1-4 word reason if known, e.g. "off-peak/inactive routes"> — full sample: <alignment.static_only_sample>) |
| Fixable via existing normalizer? | <"No transform needed — verbatim match." / "Yes — `<uppercase / plusToSbs / stripLeadingZeros>` closes the gap, config-only, no code change." / "No — mismatch isn't one of the three existing transform shapes; would need new code."> |

<1-3 sentences: is there a transform needed (e.g. strip a prefix, zero-pad), or is it a
verbatim match? Before calling any residual mismatch "needs new code," check it against
the platform's three existing config-only route-ID transforms — case normalization, a
trailing-marker-to-suffix rewrite (e.g. a `+` suffix becoming `-SBS`), and leading-zero
stripping. If one of these closes the gap, the fixable-via-existing-normalizer row above
must say so and the verdict below must NOT downgrade to "needs new code." If any RT IDs
remain unmatched after checking, say whether they look like a real gap or an internal/
special service the public static feed wouldn't have.>

**Unmatched-route runtime behavior:** any vehicle whose route can't be resolved against
the index above is not silently folded into an existing category (e.g. treated as a bus)
— the platform renders it under an explicit "unknown" category, a deliberate data-quality
signal rather than a defect. <If alignment is below 100%, one clause noting how many/what
share of vehicles this affects in practice.>

## Rail (heavy rail / `route_type=1`)

<!-- OMIT THIS ENTIRE SECTION if static.rail_route_count == 0. Do not write "Rail: N/A" in
     place of the section — just remove the section header and everything below it. -->

| | |
|---|---|
| Static rail routes | <static.rail_route_count> — keys <static.rail_index_keys, comma-separated> |
| Rail realtime API | <rail_realtime_url or "Not provided — <authority> publishes no public live rail vehicle-position feed"> |
| Live trains available | <rail.distinct_trains or 0> |
| Live-position check | <PASS (one coord per train) / FAIL — N trains with multiple coords / N/A — no feed> |
| LINE ↔ static match | <rail_match_pct%> or "N/A" if no rail feed was fetchable |
| Integration mechanism this would need | <"Config-only route-ID remap — rail vehicles arrive on the same GTFS-RT feed as buses with a different route_id scheme that a plain remap dictionary resolves; no new code." / "Bespoke adapter — rail positions come from a separate, agency-specific non-GTFS-RT feed (e.g. JSON) and would need new code to normalize into the platform's feed shape, mirroring the one bespoke rail adapter that exists today." / "N/A — no rail feed to integrate."> |

<1-3 sentences: if no live rail feed exists, say so plainly and note that rail geometry
still loads from static (route_type=1) even without live positions — this is N/A, not
a defect. If a rail feed was fetched, state whether trains snap cleanly (100% match) or
need a line-code transform, exactly like the bus alignment section above. Naming the
integration mechanism matters: a config-only remap is materially less onboarding effort
than a bespoke adapter, and the report should make that distinction explicit rather than
leaving "Rail: PARTIALLY COMPATIBLE" to imply the same effort either way.>

<!-- If the agency has a non-rail category worth flagging (e.g. streetcars classified as
     route_type=0 → Bus in this app's loader), add ONE short subsection here mirroring the
     "Streetcars are route_type=0 (tram), not rail" note in ttc.md. Omit if not applicable
     — do not force this subsection for agencies with a clean bus/rail split. -->

## Verdict

- **Buses<+ streetcars, if applicable>: <COMPATIBLE / PARTIALLY COMPATIBLE>** — <one-clause justification citing the alignment %>
- **Rail: <COMPATIBLE / PARTIALLY COMPATIBLE / N/A>** — <one-clause justification, or omit this line entirely if the Rail section above was omitted>

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **<PASS / FAIL>** — <cite the lat/lon % and any per-vehicle timestamp %> |
| Route ID alignment | **<PASS / PARTIAL (<pct>% match) / FAIL>** — <one clause> |
| Rail line alignment | **<PASS / PARTIAL / FAIL / N/A>** — <one clause, or omit row entirely if no rail section exists> |

**Bottom line:** <2-4 sentences summarizing what works, any caveats (cosmetic vs.
functional), and whether a simple transform would close any remaining gap. No new numbers
here that don't already appear above.>

## Adding <authority> as a data source

- **Static GTFS zip:** `<url>`<if the URL has quirks (spaces, rotating resource IDs), one clause noting it, mirroring ttc.md's CKAN caveat — omit if none>
- **GTFS-RT vehicle positions:** `<url>`<note any sibling endpoints that exist but are NOT used (trip-updates, alerts), if discovered during stage 3 — omit if none surfaced>
- **Rail realtime API:** <url, or "n/a — <authority> has no public live rail position feed">
- **Auth:** <"None for either feed." / describe exactly what's required — this MUST match what stage 3 actually found, never assume "none" without having checked. If a key is required, note it's a config-only `ApiKeyEnvVar`/`ApiKeyQueryParam` entry, not new code.>
- **Route ID transform needed (buses):** <"none — RT route_id matches static route_short_name verbatim." / "config-only — apply the existing `<uppercase / plusToSbs / stripLeadingZeros>` normalizer via CityConfig.RouteIdNormalization, no code change." / describe a genuinely new transform if none of the three existing ones fit>
- **Rail line transform needed:** <"none" / "config-only — a CityConfig.RailRouteIdMap entry remaps <RT id> → <static key>, no code change" / "n/a — no rail" / describe if a bespoke adapter is needed instead>
- **Config entry (generic city path):** this is a config-only city — a `CityConfig` entry (`Cities:` array in both the Worker's and WebAPI's `appsettings.json`, byte-identical) with `GtfsRtUrls`, `StaticZipUrls`, and (only if needed) `ApiKeyEnvVar`/`ApiKeyQueryParam`, `RouteIdNormalization`, `RailRouteIdMap`. <Omit if this authority instead needs a bespoke `ITransitCity` implementation — say so explicitly and name what the bespoke class would need to do (e.g. merge a separate rail JSON feed like the one existing bespoke rail adapter does).>
- **Optional follow-up:** <anything worth flagging for a future onboarding pass that isn't blocking, e.g. tram-specific voicing — omit if nothing applies>
```

END TEMPLATE

---

## Field-source reference (for the writing agent, not part of the rendered doc)

| Placeholder | Comes from |
|---|---|
| `<aggregate_score>`, `<bus_points>`, `<rail_points>`, `<credential_points>`, effort tier | `aggregate-score-formula.md` — compute this FIRST, before drafting the rest of the report, since the score block is the report's first content |
| `rt.*` | `mj-gtfs` combined decode script's `rt` JSON object |
| `static.*` | same script's `static` JSON object |
| `alignment.*` | same script's `alignment` JSON object |
| `rail.*` | `mj-gtfs`'s separate rail-realtime decode script (only run if a rail URL was found) |
| `rail_match_pct` | computed the same way as bus `alignment.match_pct`, but against `static.rail_index_keys` and the rail feed's `LINE` values — not emitted by the combined script; compute by hand from the two sets |
| Fixable-via-existing-normalizer check | manually test `alignment.unmatched_rt_ids` against the platform's three known transforms (uppercase, a trailing `+`→`-SBS` rewrite, leading-zero stripping) before concluding new code is needed — this is a desk-check, not something `mj-gtfs` computes for you |
| Rail integration mechanism | desk-check based on what stage 3 found: if rail vehicles ride the same GTFS-RT feed as buses under a different ID scheme, that's the config-only remap case; if rail positions come from a wholly separate, agency-specific feed format, that's the bespoke-adapter case |
| `<...>` anything else | stage 1–3 findings (city/authority name, URLs, region, discovered auth requirements) |

Any placeholder you cannot fill from an actual tool-call output is a signal you picked the
wrong template — stop and re-evaluate whether the BLOCKED template fits better.

---

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
at 15 — see `aggregate-score-formula.md`).

**How to use this file**: copy the Markdown between the `BEGIN TEMPLATE` and `END TEMPLATE`
markers into `docs/city-compat/{slug}.md`, then replace every `<angle-bracket>`
placeholder. Unlike the COMPATIBLE template, several fields here have a **fixed literal
value** (`UNASSESSED`, `N/A`) rather than a measurement slot — use exactly that literal
token, never a guessed number, when the placeholder says so.

**Compute the aggregate score before drafting anything else.** Read
`aggregate-score-formula.md`'s "Blocked-outcome ceiling" section and follow it
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
>  `aggregate-score-formula.md`; the rail component (if non-zero) comes from a
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
| `<aggregate_score>`, `<rail_points>`, effort tier, ceiling clause | `aggregate-score-formula.md`'s "Blocked-outcome ceiling" section — compute AFTER deciding the blocking classification (below), since the classification sets the ceiling |
| Blocking classification (KEY-GATED / NO-USABLE-FEED) | Decide this FIRST, before drafting anything else — it is a binary desk-check: does a consumable feed format exist behind a credential (KEY-GATED), or does no consumable format exist regardless of credentials (NO-USABLE-FEED)? Every downstream field in this template follows from this choice, including the score ceiling. |
| Blocking reason (Feed health) | The feed-discovery playbook's failure→verdict classifier — pick the row that matches what was actually found and the classification chosen above, quote it near-verbatim |
| `static.*` | `mj-gtfs`'s static GTFS parse script, if the zip was reachable |
| Rail line codes (desk-check section) | Published developer documentation found during stage 3 WebSearch — only if concretely found, never inferred |
| `UNASSESSED` / `N/A` cells | Fixed literal tokens — never replaced with a number. If you find yourself wanting to write a percentage in one of these cells, you have live data and should be using the COMPATIBLE template instead (or its PARTIAL case) |
| "Open items" list | Whatever concrete unblockers stage 3's investigation surfaced — not boilerplate. For KEY-GATED, this list is almost always "obtain a developer API key"; for NO-USABLE-FEED, it should describe what a bespoke adapter would need to normalize. |
