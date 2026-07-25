# Report Template — COMPATIBLE / PARTIALLY COMPATIBLE outcome

**Use this template when**: stage 3 found a real, keyless (or already-authorized) GTFS-RT
vehicle-position feed, it was successfully fetched, and stage 4's combined decode script
ran to completion (`rt.*` populated, no unresolved `rt._diag_note`). If the feed could not
be fetched or decoded at all, STOP — use `report-template-blocked.md` instead. Do not use
this template and then fill its numeric fields with guesses; every `<...>` placeholder
below MUST be replaced with a real measured value or the run has picked the wrong template.

**Compute the aggregate score before drafting anything else.** Read
`contracts/aggregate-score-formula.md` and follow it exactly — it is the single source of
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
>  entry, no new code." Computed per `contracts/aggregate-score-formula.md`; every
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
- **Config entry (generic city path):** this is a config-only city — a `CityConfig` entry (`Cities:` array in both the Worker's and WebAPI's `appsettings.json`, byte-identical) with `GtfsRtUrls`, `StaticZipUrls`, `EmitsTelemetry: true`, and (only if needed) `ApiKeyEnvVar`/`ApiKeyQueryParam`, `RouteIdNormalization`, `RailRouteIdMap`. <Omit if this authority instead needs a bespoke `ITransitCity` implementation — say so explicitly and name what the bespoke class would need to do (e.g. merge a separate rail JSON feed like the one existing bespoke rail adapter does).>
- **Optional follow-up:** <anything worth flagging for a future onboarding pass that isn't blocking, e.g. tram-specific voicing — omit if nothing applies>
```

END TEMPLATE

---

## Field-source reference (for the writing agent, not part of the rendered doc)

| Placeholder | Comes from |
|---|---|
| `<aggregate_score>`, `<bus_points>`, `<rail_points>`, `<credential_points>`, effort tier | `contracts/aggregate-score-formula.md` — compute this FIRST, before drafting the rest of the report, since the score block is the report's first content |
| `rt.*` | `mj-gtfs` combined decode script's `rt` JSON object |
| `static.*` | same script's `static` JSON object |
| `alignment.*` | same script's `alignment` JSON object |
| `rail.*` | `mj-gtfs`'s separate rail-realtime decode script (only run if a rail URL was found) |
| `rail_match_pct` | computed the same way as bus `alignment.match_pct`, but against `static.rail_index_keys` and the rail feed's `LINE` values — not emitted by the combined script; compute by hand from the two sets |
| Fixable-via-existing-normalizer check | manually test `alignment.unmatched_rt_ids` against the platform's three known transforms (uppercase, a trailing `+`→`-SBS` rewrite, leading-zero stripping) before concluding new code is needed — this is a desk-check, not something `mj-gtfs` computes for you |
| Rail integration mechanism | desk-check based on what stage 3 found: if rail vehicles ride the same GTFS-RT feed as buses under a different ID scheme, that's the config-only remap case; if rail positions come from a wholly separate, agency-specific feed format, that's the bespoke-adapter case |
| `<...>` anything else | stage 1–3 findings (city/authority name, URLs, region, discovered auth requirements) |

Any placeholder you cannot fill from an actual tool-call output is a signal you picked the
wrong template — stop and re-evaluate whether `report-template-blocked.md` fits better.
