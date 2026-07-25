# GTFS Compatibility Report — King County Metro (Seattle, Washington)

> ## 100.0/100 — Drop-in
> Bus: 70.0/70 · Rail: 20/20 · Credential: 10/10
> Config-only: RT `route_id` is an internal numeric ID (`100001`) that doesn't match
> static's rider-facing `route_short_name` (`1`) via any string transform, but a single
> 103-entry `RailRouteIdMap` (a generic whole-feed remap, despite its rail-sounding name)
> resolves all 103 distinct RT ids to a valid static key at 100% — no new code. Computed
> per `aggregate-score-formula.md`, with one explicit deviation noted below.

**Evaluated:** 2026-07-25

> **Scoring note (read before trusting the 70/70 bus figure at face value):** the
> aggregate-score formula's bus-alignment credit clause names only the three
> `RouteIdNormalizer` string transforms (`uppercase` / `plusToSbs` / `stripLeadingZeros`).
> KCM's mismatch is not one of those — it's resolved by `CityConfig.RailRouteIdMap`, a
> mechanism the formula's own **rail** lookup table explicitly gives full credit for
> ("integrates via a config-only `RailRouteIdMap` remap; alignment verified clean" → 20
> pts). `RailRouteIdMap`'s implementation (`GtfsRtCity.ApplyRailRouteIdMap`) is generic —
> it remaps every vehicle entity's `RouteId` unconditionally, before normalization runs,
> regardless of whether the route is rail or bus. This report credits `bus_points` using
> the post-remap `effective_alignment_pct` (100%) by direct analogy to the rail row, since
> it is the same platform code path and the same config-only nature. A literal reading of
> the formula's bus clause (crediting only the three named transforms) would instead score
> `bus_points = 0` → aggregate ≈ 30 → **Not Viable**, which materially undersells a
> verified, zero-new-code drop-in. **Follow-up recommended:** extend
> `aggregate-score-formula.md`'s bus-alignment clause to explicitly credit
> `RailRouteIdMap`-style whole-feed remaps, not just the three normalizer transforms.

## Feed health

| | |
|---|---|
| GTFS-RT URL | `https://s3.amazonaws.com/kcm-alerts-realtime-prod/vehiclepositions.pb` |
| Static GTFS URL | `https://metro.kingcounty.gov/GTFS/google_transit.zip` |
| RT feed size | 38,016 bytes  •  Header ts: `0` (normal — per-vehicle timestamps present at 98.0%) |
| Static routes | 157 routes / 157 with shapes / 0 without |

> **Feed source note:** King County Metro's own GTFS-RT is published as a direct, keyless
> protobuf object on S3 (`s3.amazonaws.com/kcm-alerts-realtime-prod/*`), separate from a
> second, key-gated access path via **OneBusAway** (Sound Transit's regional real-time
> aggregator, which requires a requested API key for its trip-updates/vehicle-positions
> endpoints). This report uses the direct S3 path, which is keyless and decodes cleanly —
> the OneBusAway key requirement does not apply here. Sibling S3 endpoints exist but are
> NOT vehicle-positions: `.../tripupdates.pb` (trip updates), `.../alerts.pb` (service
> alerts).

## Vehicle positions (GTFS-RT)

| | |
|---|---|
| Total / vehicle entities | 391 / 391 |
| With `route_id` | **391 (100%)** |
| Without `route_id` | **0 (0%)** |
| lat/lon present | **97.7%** |
| speed present | 3.1% (optional — degrades gracefully; sparsely populated on this feed) |
| bearing present | 5.1% (optional — degrades gracefully; sparsely populated on this feed) |
| vehicle.timestamp | 98.0% |

This is a **surface-only** feed: buses plus King County Metro's two streetcar lines
(South Lake Union, First Hill) — no heavy rail, since Sound Transit (a separate agency)
operates Link light rail. **All 391 live vehicles are route-attributed**, zero
`skippedNoRouteId` loss; 97.7% carry a usable live position.

## Route ID alignment (buses + streetcars)

| | |
|---|---|
| RT distinct route IDs | 103 |
| Static index keys (`route_short_name ?? route_id`) | 157 |
| **Matched (as-is)** | **0 (0.0%)** — see remap note below |
| Unmatched RT IDs | all 103 as-is (e.g. `100001`, `100002`, `100003`, …) |
| Static-only keys | 157 (every static key, since RT sends the internal `route_id` column, not `route_short_name`) |
| Fixable via existing normalizer? | No — none of `uppercase`/`plusToSbs`/`stripLeadingZeros` transform `100001` into `1`; this is a different identifier space (internal numeric ID vs. rider-facing short name), not a formatting quirk. **Fixable via `CityConfig.RailRouteIdMap` instead** — a 103-entry dictionary keyed by the RT `route_id` values above, resolving to their static `route_short_name` (verified below). |

**RT sends the GTFS `route_id` column verbatim; static's index key is `route_short_name`.**
Cross-referencing every one of the 103 distinct RT ids against `routes.txt`'s `route_id`
column (not its `route_short_name` column) finds a **100% match** — every single RT id
corresponds to exactly one static route, and every corresponding `route_short_name` is
already a valid static index key (e.g. `100001`→`1`, `100002`→`10`, `100340`→`South Lake
Union Streetcar`). Nothing is unresolved after the remap; there is no residual gap to
report as a runtime `"unknown"` category.

**Unmatched-route runtime behavior:** as-is (without the remap), 100% of live vehicles
would render under the platform's explicit `"unknown"` category — this makes the
`RailRouteIdMap` config entry load-bearing for this city, not optional polish.

## Rail (`route_type=1`)

<!-- static.rail_route_count == 0 — King County Metro runs zero route_type=1 routes. Link
     light rail is a separate agency (Sound Transit) and out of scope for this report. -->

King County Metro publishes **zero `route_type=1` routes** — Seattle's Link light rail
(the region's heavy-rail service) is operated by **Sound Transit**, a distinct multi-county
agency with its own GTFS/GTFS-RT (Metro is contracted to operate Link's trains and Sound
Transit Express buses, but does not publish their realtime feed under its own GTFS). Rail
is genuinely **N/A** for this authority, not a gap — a future Sound Transit evaluation
would be the correct place to assess Link light rail.

### Streetcars are `route_type=0` (tram), not rail

KCM's route_type breakdown is **101 bus (`3`) + 2 streetcar (`0`, South Lake Union & First
Hill) + 2 ferry (`4`, not present in this RT feed) = 105 routes with live traffic** of 157
total static routes. Mirroring TTC's precedent, the worker's loader classifies only
`route_type=1` as `TransitMode.Rail`; **`route_type=0` streetcars are treated as Bus.**
Both KCM streetcar lines are live in this RT snapshot and resolve cleanly under the same
103-entry remap — they'd ride the bus instrument palette, a sonic choice to revisit later,
not a compatibility defect.

## Verdict

- **Buses + streetcars: COMPATIBLE (config-only remap required)** — 100% resolvable via a
  103-entry `RailRouteIdMap`; 0% as-is because RT sends `route_id`, not `route_short_name`
- **Rail: N/A** — King County Metro has no `route_type=1` routes; Link light rail belongs
  to the separate Sound Transit agency

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **PASS** — 97.7% lat/lon, 100% route_id, 98.0% per-vehicle timestamp |
| Route ID alignment | **PASS (100% once remapped)** — 0% as-is; fully resolved by a verified 103-entry `RailRouteIdMap`, zero residual gap |
| Rail line alignment | **N/A** — no `route_type=1` routes published by this authority |

**Bottom line:** King County Metro is a strong, keyless candidate blocked from a
verbatim-match report only by a naming quirk: its GTFS-RT feed identifies vehicles by the
internal `route_id` column instead of the rider-facing `route_short_name` static uses as
its index key. That gap is not cosmetic-free (100% of vehicles would land in
`"unknown"` without it) but it is **entirely config-only** — a single 103-entry
`RailRouteIdMap` dictionary, built directly from this evaluation's own `route_id →
route_short_name` cross-reference, closes it completely with zero new code. Both feeds are
keyless via KCM's direct S3 endpoints (the OneBusAway key-gated path is a red herring —
don't use it).

## Adding King County Metro as a data source

- **Static GTFS zip:** `https://metro.kingcounty.gov/GTFS/google_transit.zip`
- **GTFS-RT vehicle positions:** `https://s3.amazonaws.com/kcm-alerts-realtime-prod/vehiclepositions.pb`
  - Sibling feeds (unused by this worker): `.../tripupdates.pb` (trip updates), `.../alerts.pb` (service alerts). A separate, key-gated OneBusAway path also exists for KCM data but is unnecessary — the direct S3 feeds above are keyless and already verified live.
- **Rail realtime API:** n/a — King County Metro has no `route_type=1` routes; Link light rail is a separate Sound Transit feed, out of scope here.
- **Auth:** None for either feed — both are public, keyless S3 objects.
- **Route ID transform needed (buses + streetcars):** config-only — a `CityConfig.RailRouteIdMap` entry with 103 entries (`100001`→`1`, `100002`→`10`, … `100340`→`South Lake Union Streetcar`, `102638`→`First Hill Streetcar`), generated directly from this evaluation's `routes.txt` cross-reference. None of the three `RouteIdNormalization` string transforms apply.
- **Config entry (generic city path):** this is a config-only city — a `CityConfig` entry (`Cities:` array in both the Worker's and WebAPI's `appsettings.json`, byte-identical) with `GtfsRtUrls`, `StaticZipUrls`, `EmitsTelemetry: true`, and the 103-entry `RailRouteIdMap` above (reused generically for buses, not for an actual rail merge). No `ApiKeyEnvVar`/`ApiKeyQueryParam` needed — both feeds are keyless.
- **Optional follow-up:** (1) extend `aggregate-score-formula.md`'s bus-alignment crediting clause to explicitly cover `RailRouteIdMap`-style whole-feed remaps, not just the three named normalizer transforms — this report's score would be ambiguous without the scoring-note callout above. (2) consider a distinct `TransitMode` for `route_type=0` streetcars instead of folding them into Bus, same open item TTC's report already flagged. (3) speed/bearing are sparsely populated (3.1%/5.1%) — both degrade gracefully and are non-blocking, but worth knowing before tuning any speed-dependent audio behavior.
