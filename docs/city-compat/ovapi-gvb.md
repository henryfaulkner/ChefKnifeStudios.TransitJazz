# GTFS Compatibility Report — GVB (via OVapi / Stichting OpenGeo) (Amsterdam, Netherlands)

> ## 22/100 — Not Viable
> Bus: 0/70 · Rail: 12/20 · Credential: 10/10
> The feed itself is excellent — keyless, standard protobuf, 2,636 live route-attributed
> vehicles at 97.2% lat/lon — but **0% of its route IDs match the platform's join key**:
> OVapi's RT `route_id` is an opaque national surrogate (`152323`) while static's
> `route_short_name` is the rider-facing line (`1`), and the worker keys on
> `route_short_name ?? route_id`. None of the three existing normalizers close a
> surrogate→short-name gap; this needs a new route-ID resolution mechanism, not config.
> Computed per `aggregate-score-formula.md`; every component above is a real measurement
> or fixed categorical lookup, never a guess.

**Evaluated:** 2026-08-29

## Feed health

| | |
|---|---|
| GTFS-RT URL | `https://gtfs.ovapi.nl/nl/vehiclePositions.pb` |
| Static GTFS URL | `https://gtfs.ovapi.nl/nl/gtfs-nl.zip` |
| RT feed size | 361,700 bytes  •  Header ts: `0` (normal — per-vehicle timestamps present at 100%) |
| Static routes | 3,234 routes / 2,644 with shapes / 590 without |

> **Feed source note:** Amsterdam has no city-operator-published GTFS-RT of its own. GVB
> (the municipal operator of Amsterdam's metro, tram, bus and ferry network, ~234M annual
> riders) publishes no realtime endpoint on its own domain; Dutch realtime is aggregated
> nationally by OVapi / Stichting OpenGeo, which converts operator KV6/BISON feeds into
> standard GTFS-RT protobuf. Per the skill's tie-break rule — *when a regional umbrella and
> a city operator both exist, prefer the one that publishes a unified GTFS-RT
> vehicle-positions feed* — the evaluated feed is OVapi's national aggregate, with GVB as
> the underlying Amsterdam authority. Both `gtfs.ovapi.nl` and `gtfs.openov.nl` serve the
> byte-identical file (same 361,296-byte `Content-Length` on simultaneous HEAD requests);
> they are mirrors, not distinct feeds. Sibling GTFS-RT endpoints exist under the same
> directory but are NOT vehicle-positions: `tripUpdates.pb` (4.1M), `trainUpdates.pb`
> (2.0M), `alerts.pb` (540K). The static zip is the **whole Netherlands** (230,369,416
> bytes, 41 agencies) — there is no per-operator GVB slice; `gtfs.ovapi.nl/gvb/` 404s.

## Vehicle positions (GTFS-RT)

| | |
|---|---|
| Total / vehicle entities | 2,636 / 2,636 |
| With `route_id` | **2,636 (100%)** |
| Without `route_id` | **0 (0%)** ← out-of-service / deadheading; skipped as `skippedNoRouteId` |
| lat/lon present | **97.2%** |
| speed present | 0% (optional — degrades gracefully; absent on every vehicle in this feed) |
| bearing present | 0% |
| vehicle.timestamp | 100% |

This is a national, all-mode feed (bus, tram, metro, ferry across 41 Dutch operators), not
an Amsterdam-scoped one. Every one of the 2,636 live vehicles is route-attributed, so there
is zero `skippedNoRouteId` loss. Within an Amsterdam bounding box (52.28–52.45 N,
4.72–5.02 E) it carries **358 live vehicles across 89 distinct routes**, of which 276
vehicles on 45 routes belong to GVB itself — a dense, healthy sample for a city this size.
The 2.8% of vehicles lacking lat/lon are not randomly distributed: 53 of those 73 are
`route_type=1` metro, and 47 of those are specifically GVB metro (see Rail below).

## Route ID alignment (buses + trams + metro + ferries, all modes in one feed)

| | |
|---|---|
| RT distinct route IDs | 901 |
| Static index keys (`route_short_name ?? route_id`) | 1,208 |
| **Matched (as-is)** | **0 (0.0%)** |
| Unmatched RT IDs | all 901 — e.g. `126872`, `126873`, `126877`, `126881`, `126882`, `126885`, `126933`, `126934`, `126935`, `126936`, `126937`, `126938`, `126939`, `126941`, `126942`, `126943`, `126947`, `126948`, `126949`, `126950` … and 881 more |
| Static-only keys | 1,208 (every key — the two sets are disjoint; sample: `1`, `10`, `100`, `101`, `102`, `103`, `104`, `105`, `106`, `107`) |
| Fixable via existing normalizer? | No — mismatch isn't one of the three existing transform shapes; would need new code. Desk-checked all 901 unmatched IDs against `uppercase`, `plusToSbs`, and `stripLeadingZeros`: **0 of 901** are resolved by any of them. |

**This is a structurally different problem from a formatting mismatch.** The RT feed's
`route_id` and the static feed's `route_id` agree almost perfectly — **887 of 901 (98.4%)**
of RT route IDs are present verbatim as a static `route_id`. The gap is entirely on the
*join key*: `GtfsStaticLoader.cs` builds its index as `displayKey = shortName ?? routeId`,
and OVapi populates `route_short_name` on **all 3,234 static routes (zero empty)** with the
rider-facing line designator. So the index keys become `1`, `52`, `369` while the RT feed
sends `152323`, `152363`, `152372`. Not a single RT ID coincides with any static short name
(0 overlap). The three existing normalizers all rewrite an ID's *surface form* — case, a
trailing `+`, leading zeros — and none can map an opaque numeric surrogate onto a
semantically unrelated short name; that requires an actual `route_id`→`route_short_name`
lookup through static's `routes.txt`, which is a new resolution path, not a
`RouteIdNormalization` step.

**Unmatched-route runtime behavior:** any vehicle whose route can't be resolved against the
index above is not silently folded into an existing category (e.g. treated as a bus) — the
platform renders it under an explicit "unknown" category, a deliberate data-quality signal
rather than a defect. Because alignment is 0%, this would affect **every one of the 2,636
live vehicles**: all of them would resolve to `"unknown"`, and none would snap to a route
shape. Nothing would render meaningfully today.

## Rail (heavy rail / `route_type=1`)

| | |
|---|---|
| Static rail routes | 10 — keys `50`, `51`, `52`, `53`, `54`, `A`, `B`, `C`, `D`, `E` (the `50`–`54` set is GVB's Amsterdam Metro; `A`–`E` belong to other Dutch operators) |
| Rail realtime API | Not a separate feed — GVB metro rides the **same** keyless GTFS-RT feed as the buses and trams, under RT `route_id`s `152361`/`152362`/`152363`/`152364`/`152365` |
| Live trains available | 47 vehicle entities across all 5 GVB metro lines (`152361`/Lijn 50 ×11, `152362`/Lijn 51 ×10, `152363`/Lijn 52 ×11, `152364`/Lijn 54 ×9, `152365`/Lijn 53 ×6) |
| Live-position check | **FAIL** — 0 of 47 GVB metro vehicles carry a lat/lon; every metro entity has a `route_id` and a timestamp but no `position` submessage at all |
| LINE ↔ static match | N/A — the RT IDs are the same opaque surrogates as the bus axis, so the same 0% join-key mismatch applies; the underlying `route_id`↔`route_id` correspondence is exact |
| Integration mechanism this would need | Config-only route-ID remap **would not suffice on its own** — a `CityConfig.RailRouteIdMap` could map `152361→50` … `152365→54` (5 entries, no new code), but it would remap vehicles that carry **no coordinates**, so nothing would render regardless. The blocking issue is missing position data upstream, not the ID scheme. |

GVB's metro is present in the feed as **schedule-only entities**: all 5 lines report live
vehicles with valid trip and timestamp fields, but the protobuf `position` field is absent
on every single one. This accounts for 47 of the 73 position-less vehicles feed-wide and is
the dominant reason lat/lon sits at 97.2% rather than ~100%. Rail geometry would still load
fine from static (`route_type=1` shapes are present), so the map would draw the metro lines
— they simply would never show a moving vehicle. This is a genuine upstream data gap in the
KV6→GTFS-RT conversion for GVB metro, independently corroborated by Dutch open-data
community reports of absent KV78Turbo GVB metro vehicle positions. Trams
(`route_type=0`, 122 live vehicles) and buses (`route_type=3`, 107 live vehicles) are
unaffected and do carry positions.

### Trams are `route_type=0`, not rail

GVB's 17 tram lines (`1`, `2`, `4`, `5`, `6`, `7`, `12`, `13`, `14`, `17`, `19`, `24`,
`25`, `26`, `27`, `28`, `29`) are `route_type=0`. Per `GtfsStaticLoader.cs`'s classifier,
route_type 0, 1, and 2 all map to **Rail**, so trams would render on the platform's Rail
axis rather than as buses — worth noting for voicing at onboarding time, and consistent
with how TTC streetcars are treated. Unlike the metro, trams do carry live positions.

## Verdict

- **Buses + trams: PARTIALLY COMPATIBLE** — the feed is technically excellent (100%
  route_id, 97.2% lat/lon, keyless, 2,636 live vehicles), but 0% join-key alignment means
  no vehicle resolves to a route today
- **Rail (GVB Metro): INCOMPATIBLE** — all 5 lines are present in the feed but emit zero
  coordinates; a config-only remap cannot fix missing position data

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **PASS** — 97.2% lat/lon (above the 90% gate), 100% route_id, 100% per-vehicle timestamp |
| Route ID alignment | **FAIL (0.0% match)** — RT sends opaque surrogates, static's join key is the rider-facing short name; disjoint sets |
| Rail line alignment | **FAIL** — same 0% join-key mismatch, compounded by zero live positions on all 47 metro vehicles |

**Bottom line:** The data quality here is genuinely good and the access story is ideal —
keyless, standard protobuf, no terms gate, 358 live vehicles in Amsterdam alone. What
blocks it is a join-key architecture mismatch, not a bad feed: OVapi keys realtime by a
national surrogate `route_id` while the platform keys its route index by
`route_short_name ?? route_id`, and the two sets share zero members. The fix is
well-understood but is **new code, not configuration** — the worker would need to resolve
RT `route_id` → static `route_id` → `route_short_name` (a lookup that succeeds for 98.4% of
IDs, so it would work well once built). Separately, GVB metro would remain
position-less even after that fix. A reasonable onboarding would target GVB's trams and
buses only, and treat the metro as geometry-without-vehicles.

## Adding GVB (via OVapi) as a data source

- **Static GTFS zip:** `https://gtfs.ovapi.nl/nl/gtfs-nl.zip` — 230 MB covering all 41
  Dutch operators; there is no per-operator slice (`gtfs.ovapi.nl/gvb/` 404s), so a GVB-only
  onboarding would either filter by `agency_id = GVB` at load or accept the full national
  parse. Refreshed daily ~03:00 UTC. A flat zip (`agency.txt`, `routes.txt`, `trips.txt`,
  `shapes.txt`, …) — no nested-zip unwrapping needed, unlike SEPTA.
- **GTFS-RT vehicle positions:** `https://gtfs.ovapi.nl/nl/vehiclePositions.pb` (mirror:
  `https://gtfs.openov.nl/gtfs-rt/vehiclePositions.pb`, byte-identical). Sibling endpoints
  exist but are NOT used: `tripUpdates.pb`, `trainUpdates.pb`, `alerts.pb`.
- **Rail realtime API:** n/a — GVB metro rides the same GTFS-RT feed as the buses, but with
  the `position` field absent on every vehicle. There is no separate live rail position feed.
- **Auth:** None for either feed — both are fully keyless and returned HTTP 200 unauthenticated.
  OVapi's README requests (but does not enforce) a descriptive `User-Agent` and
  `If-Modified-Since`/`If-None-Match` conditional requests, and asks for attribution:
  "Dutch integrated real-time transit data (2013-2025) by Stichting OpenGeo." Its terms
  prohibit claiming to represent or impersonate the listed transit agencies — worth honoring
  in any UI copy.
- **Route ID transform needed (buses):** **Not achievable with the existing three
  normalizers.** RT `route_id` matches static `route_id` at 98.4% but matches the platform's
  `route_short_name ?? route_id` join key at 0%. Would require a new
  `route_id`→`route_short_name` resolution step reading `routes.txt` — new code in the
  join-key path, not a `CityConfig.RouteIdNormalization` entry.
- **Rail line transform needed:** A 5-entry `CityConfig.RailRouteIdMap`
  (`152361→50`, `152362→51`, `152363→52`, `152364→54`, `152365→53`) would align the metro
  IDs, but is pointless in isolation — those vehicles carry no coordinates. Also note these
  surrogate IDs are not guaranteed stable across OVapi's daily static rebuilds, so a
  hardcoded remap would be brittle.
- **Config entry (generic city path):** **Not sufficient today.** This authority cannot be
  onboarded as a config-only `CityConfig` entry: with 0% join-key alignment every vehicle
  would render as `"unknown"` and none would snap to a route. It needs the surrogate-ID
  resolution capability described above before a `Cities:` entry would produce anything
  usable. Once that capability exists, the rest of the onboarding is ordinary — keyless
  URLs, standard flat zip, standard protobuf.
- **Optional follow-up:** The surrogate-`route_id` join pattern is a property of the
  **national OVapi conversion**, not of Amsterdam specifically — so building that resolution
  step once would unlock every Dutch city on this feed at once (Rotterdam, The Hague,
  Utrecht, Eindhoven and the rest of the 41 operators), making it a materially better
  return on effort than a single-city adapter. Trams classify as Rail (`route_type=0`) and
  would want tram-appropriate voicing.
