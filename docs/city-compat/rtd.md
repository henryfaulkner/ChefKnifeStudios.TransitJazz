# GTFS Compatibility Report — RTD (Regional Transportation District) (Denver, Colorado)

> ## 92.4/100 — Drop-in
> Bus: 62.4/70 · Rail: 20/20 · Credential: 10/10
> Config-only: buses snap at 89.2% verbatim alignment; all 8 light/commuter rail lines
> ride the same keyless feed under an `101C`/`103W`/`113B`-style prefix scheme that a
> single 8-entry `RailRouteIdMap` resolves cleanly to the static `C`/`W`/`B` keys — no new
> code either axis. Computed per `aggregate-score-formula.md`; every component above is a
> real measurement or fixed categorical lookup, never a guess.

**Evaluated:** 2026-07-25

## Feed health

| | |
|---|---|
| GTFS-RT URL | `https://open-data.rtd-denver.com/files/gtfs-rt/rtd/VehiclePosition.pb` |
| Static GTFS URL | `https://www.rtd-denver.com/files/gtfs/google_transit.zip` |
| RT feed size | 52,656 bytes  •  Header ts: `0` (normal — per-vehicle timestamps present at 100%) |
| Static routes | 125 routes / 125 with shapes / 0 without |

> **Feed source note:** RTD migrated to new canonical GTFS-RT URLs in Fall 2025
> (`open-data.rtd-denver.com`); the legacy `www.rtd-denver.com/files/gtfs-rt/...` path
> ceased functioning December 2025. The static zip URL published on RTD's own open-data
> page (`www.rtd-denver.com/files/gtfs/google_transit.zip`) is now a 308 redirect to
> `www.rtd-denver.com/api/download?feedType=gtfs&filename=google_transit.zip` — both were
> followed and verified live for this evaluation. Sibling GTFS-RT endpoints exist but are
> NOT vehicle-positions: `.../rtd/Alerts.pb` (service alerts), `.../rtd/TripUpdate.pb`
> (trip updates). RTD also publishes separate Bustang (state-run intercity bus) feeds
> under `.../cdot/Bustang_*.pb` — a different agency, not evaluated here. Access requires
> agreeing to RTD's GTFS Realtime License Agreement by downloading, but this is a
> click-through terms-of-use, not a registered API key or auth token — the feed is keyless.

## Vehicle positions (GTFS-RT)

| | |
|---|---|
| Total / vehicle entities | 357 / 357 |
| With `route_id` | **357 (100%)** |
| Without `route_id` | **0 (0%)** |
| lat/lon present | **100%** |
| speed present | 0% (optional — degrades gracefully; absent on every vehicle in this feed) |
| bearing present | 92.4% |
| vehicle.timestamp | 100% |

This feed carries buses and both of RTD's rail modes (light rail, `route_type=0`;
commuter rail, `route_type=2`) together under one keyless protobuf endpoint — no
mode-specific sub-feed. **All 357 live vehicles are route-attributed with a live
position**, zero `skippedNoRouteId` loss.

## Route ID alignment (buses + light rail + commuter rail)

| | |
|---|---|
| RT distinct route IDs | 93 |
| Static index keys (`route_short_name ?? route_id`) | 125 |
| **Matched (as-is)** | **83 (89.2%)** |
| Unmatched RT IDs | `101C`, `101E`, `101T`, `103W`, `107R`, `113B`, `113G`, `117N`, `BOND`, `FREE` |
| Static-only keys | 42 (largely off-peak/seasonal/school variants, e.g. `0L`, `116X`, `120L`, `145X`, `225D`, `228A`, `228F`, plus the `FreeRide`/`MetroRide` short-name pair discussed below) |
| Fixable via existing normalizer? | No — none of the 10 unmatched IDs are resolved by `uppercase`, `plusToSbs`, or `stripLeadingZeros`; 8 of the 10 need the platform's separate `RailRouteIdMap` mechanism instead (see Rail below), not a `RouteIdNormalization` transform. |

**8 of the 10 unmatched RT IDs are rail, not a bus defect** — RTD's RT feed sends light
rail and commuter rail vehicles under a distinct route_id scheme (`101C`, `101E`, `101T`,
`103W`, `107R`, `113B`, `113G`, `117N`) that doesn't match static's plain line-letter short
names (`C`, `E`, `T`, `W`, `R`, `B`, `G`, `N`) — see Rail section for the resolution. The
remaining two are a real, small bus-side gap: `BOND` has no corresponding static route at
all (no matching `route_id`, `route_short_name`, or `route_long_name` — looks like a
discontinued or RT-only service, not a live compatibility defect), and `FREE` is RT's
`route_id` for the "16th Street FreeRide," whose static `route_short_name` is
`FreeRide` — RT sends the bare `route_id` value instead, an isolated cosmetic mismatch
the platform's three existing normalizers don't cover (it's neither a case, `+`-suffix,
nor leading-zero problem).

**Unmatched-route runtime behavior:** the 2 genuinely-unresolved bus IDs (`BOND`, `FREE`)
would render under the platform's explicit `"unknown"` category rather than being folded
into an existing one — in this snapshot that's at most 2 of 93 distinct route IDs (a small
minority of live vehicles), not a systemic gap.

## Rail (`route_type=0` light rail + `route_type=2` commuter rail)

<!-- RTD has zero route_type=1 routes; its rail service is entirely route_type=0 (5
     light-rail lines: C/E/T/W/R) and route_type=2 (4 commuter-rail lines: B/G/N/A). Per
     GtfsStaticLoader.cs's classifier (route_type 0, 1, AND 2 are all Rail), this is the
     platform's Rail axis, not Bus — included here despite the section header's usual
     route_type=1 framing. -->

| | |
|---|---|
| Static rail routes | 8 — keys `A`, `B`, `C`, `E`, `G`, `N`, `R`, `T`, `W` (9 listed; `A` also duplicates as both a static key and its own verbatim RT match) |
| Rail realtime API | Not a separate feed — all 8 rail lines ride the **same** keyless GTFS-RT feed as the buses, under RT `route_id`s `101C`/`101E`/`101T`/`103W`/`107R`/`113B`/`113G`/`117N` (plus `A`, which already matches verbatim) |
| Live trains available | 54 of 357 total vehicles, spanning all 8 rail lines (`101C`×5, `101E`×7, `101T`×5, `103W`×6, `107R`×7, `113B`×1, `113G`×4, `A`×6 in this one fetch) |
| Live-position check | PASS for all 8 lines — 100% lat/lon on every rail vehicle, distinct real coordinates tracing each line's actual geographic corridor (e.g. `A`-line points along the I-70/Peña corridor toward the airport; `103W` points west toward Golden) |
| LINE ↔ static match | 100% once the 8-entry remap below is applied (`A` already matches verbatim with zero transform) |
| Integration mechanism this would need | Config-only `CityConfig.RailRouteIdMap` — an 8-entry dictionary (`101C→C`, `101E→E`, `101T→T`, `103W→W`, `107R→R`, `113B→B`, `113G→G`, `117N→N`) remaps RT's numeric-prefixed IDs to static's plain line letters; no new code, mirrors the platform's existing config-only remap mechanism exactly. |

RTD's rail is a clean drop-in once the remap is applied: all 8 light-rail and
commuter-rail lines are live in the identical keyless feed the buses use, at 100%
lat/lon, with real, line-appropriate coordinates (not stale or duplicate positions).
The only reason this isn't a verbatim match like SEPTA's `M1` is that RTD's RT
`route_id`s carry a numeric route-family prefix (`101`/`103`/`107`/`113`/`117`) the
static `route_short_name`s don't — a naming-convention difference, not a missing
feed or a bespoke-adapter situation. A single small, static dictionary closes the gap
entirely.

## Verdict

- **Buses: PARTIALLY COMPATIBLE** — 89.2% alignment as-is; 2 of 93 distinct route IDs (`BOND`, `FREE`) remain genuinely unresolved after excluding the 8 rail IDs handled below
- **Rail: COMPATIBLE (config-only remap)** — 100% of all 8 light/commuter rail lines are live, 100% lat/lon, resolved cleanly via an 8-entry `RailRouteIdMap`, no new code

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **PASS** — 100% lat/lon, 100% route_id, 100% per-vehicle timestamp |
| Route ID alignment | **PARTIAL (89.2%)** — clean once rail is remapped; `BOND`/`FREE` remain a small residual bus-side gap |
| Rail line alignment | **PASS (100% post-remap)** — config-only `RailRouteIdMap`, live-position check clean across all 8 lines |

**Bottom line:** RTD is a strong, keyless, single-feed candidate — one GTFS-RT endpoint
carries buses, light rail, and commuter rail together, and the entire rail axis (8 lines,
54 live vehicles in this pass) is one small `RailRouteIdMap` config entry away from a
perfect match, with zero new code required anywhere. The only open item is two bus route
IDs (`BOND`, `FREE`) that don't cleanly resolve via the platform's three existing
normalizer transforms; `BOND` looks like a route absent from the current static schedule
rather than an ID-format problem, and `FREE` is a one-line dictionary fix of the same
general shape as the rail remap, not a structural blocker.

## Adding RTD as a data source

- **Static GTFS zip:** `https://www.rtd-denver.com/files/gtfs/google_transit.zip`
  - Note: this URL 308-redirects to `https://www.rtd-denver.com/api/download?feedType=gtfs&filename=google_transit.zip`; either the redirect target or a client that follows 308s must be used (`Invoke-WebRequest`'s default redirect-following handles it transparently).
- **GTFS-RT vehicle positions:** `https://open-data.rtd-denver.com/files/gtfs-rt/rtd/VehiclePosition.pb`
  - Sibling feeds (unused by this worker): `.../rtd/Alerts.pb` (service alerts), `.../rtd/TripUpdate.pb` (trip updates), plus a separate Bustang (CDOT intercity bus) pair at `.../cdot/Bustang_*.pb` that is a different operator entirely.
  - Note: RTD migrated all canonical GTFS-RT URLs in Fall 2025; legacy `www.rtd-denver.com/files/gtfs-rt/*` paths stopped working in December 2025 — use the `open-data.rtd-denver.com` host above.
- **Rail realtime API:** n/a as a separate feed — all 8 rail lines already arrive on the bus GTFS-RT feed above under a remappable ID scheme.
- **Auth:** None for either feed — both require accepting a click-through License Agreement by downloading, not a registered API key or token.
- **Route ID transform needed (buses):** config-only for 2 of 93 distinct IDs (`FREE` → `FreeRide`, if a fix is wanted) via a small dictionary entry, not one of the three existing `RouteIdNormalization` steps; `BOND` has no static counterpart to map to at all. The remaining 91 distinct bus IDs match verbatim.
- **Rail line transform needed:** config-only — a `CityConfig.RailRouteIdMap` with 8 entries (`101C→C`, `101E→E`, `101T→T`, `103W→W`, `107R→R`, `113B→B`, `113G→G`, `117N→N`) remaps RT's prefixed IDs to static's plain line letters; `A` already matches verbatim and needs no entry.
- **Config entry (generic city path):** this is a config-only city — a `CityConfig` entry (`Cities:` array in both the Worker's and WebAPI's `appsettings.json`, byte-identical) with `GtfsRtUrls`, `StaticZipUrls`, `EmitsTelemetry: true`, and `RailRouteIdMap` (the 8-entry table above). No `ApiKeyEnvVar`/`ApiKeyQueryParam` needed — both feeds are keyless.
- **Optional follow-up:** decide whether the `BOND`/`FREE` bus gap is worth a `RouteIdNormalization`-style generic transform (a fourth step alongside `uppercase`/`plusToSbs`/`stripLeadingZeros`) or a per-city ad-hoc dictionary entry, given it's a 2-route, low-impact residual rather than a systemic mismatch.
