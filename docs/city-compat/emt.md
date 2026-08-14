# GTFS Compatibility Report — EMT (Empresa Municipal de Transportes de Madrid) (Madrid, Spain)

> ## 15/100 — Not Viable
> Bus: 0/70 (blocked — no live feed measured) · Rail: 20/20 (N/A, desk-checked from static) · Credential: 0/10 (blocked)
> Ceiling applied: NO-USABLE-FEED, capped at 15
> No vehicle-position feed exists in any format — EMT's sole GTFS-RT protobuf endpoint carries only service alerts, and its separate proprietary REST API (key-gated) exposes only per-stop arrival predictions, never a vehicle's lat/lon.

**Evaluated:** 2026-08-14

## Feed health

| | |
|---|---|
| **Blocking classification** | **NO-USABLE-FEED** — see the sub-reason table this template opens with |
| Static GTFS URL | `https://datos.emtmadrid.es/dataset/9b23259a-4491-494b-9695-36a7709b2c12/resource/3cba2058-9833-422c-a704-bf992d31d2ee/download/gtfs_emt.zip` — verified live, HTTP 200, ~16.0 MiB (16,791,351 bytes) zipped |
| GTFS-RT vehicle positions (buses) | Exists but is service-alerts only — no vehicle-positions endpoint found. |
| Rail realtime (trains) | N/A — EMT has 0 `route_type=1` routes in static; Madrid's heavy rail (Metro de Madrid) is run by a separate authority, out of scope for this report. |

Two commonly-cited static mirrors (`https://servicios.emtmadrid.es:8443/gtfs/transitemt.zip` and `http://servicios.emtmadrid.es:8080/GTFS/transitEMT.zip`) were both unreachable from this environment (TLS reset / connection timeout respectively) — the working URL above was recovered from EMT's own open-data portal (`datos.emtmadrid.es`) CKAN listing; a future onboarding pass should use that portal link rather than either legacy mirror, since CKAN resource IDs can rotate.

EMT's own Mobility Labs developer portal (`mobilitylabs.emtmadrid.es`) and its API docs (`apidocs.emtmadrid.es`), the Mobility Database (`mdb-793` static, `mdb-3102` realtime), and the `transitland-atlas` DMFR registration were all checked. Exactly one live, keyless GTFS-RT protobuf endpoint exists — `https://openapi.emtmadrid.es/v1/bus/servicealerts/proto` — confirmed reachable in this run (returned real, current service-disruption entries dated within the last two weeks) but carrying incident text, not vehicle positions. A separate proprietary JSON REST API family (`openapi.emtmadrid.es/v1/transport/busemtmad/...`) exposes only route/stop metadata and per-stop arrival-time predictions, and requires an `EMT_CLIENT_ID`/`EMT_PASS_KEY` pair obtained via registration at `mobilitylabs.emtmadrid.es` — moot regardless, since it never carries a bus's lat/lon. Directly probing the sibling URL pattern `https://openapi.emtmadrid.es/v1/bus/vehiclepositions/proto` (guessed from the confirmed service-alerts path) returned a bare `HTTP 404`, confirming no such endpoint exists.

## Static GTFS (verified by direct parse)

| | |
|---|---|
| Routes | 237 total, effectively all with shapes (100% of 67,729 trips carry a `shape_id`, spread across 464 distinct shapes) |
| `route_type=3` (bus) | 237 routes — `route_id` is a zero-padded 3-digit internal code (e.g. `001`) while `route_short_name` is the plain rider-facing number (e.g. `1`) |
| `route_type=1` (rail) | 0 routes — none (Madrid Metro rides under a separate authority, not this feed) |

The `route_id` vs. `route_short_name` zero-padding gap is exactly the shape the platform's existing `stripLeadingZeros` transform closes — a future onboarding pass would need no new code for this specific quirk, only a real live feed to apply it against.

## Vehicle positions / route ID alignment (buses)

**Not assessable without a live feed.** No endpoint publishing bus vehicle positions was found in any format — GTFS-RT protobuf or proprietary JSON/XML — so there is nothing to fetch and decode. Field completeness (route_id %, lat/lon %, speed/bearing %) cannot be measured until EMT publishes an actual vehicle-positions data source.

## Verdict

- **Buses: INCOMPATIBLE (NO-USABLE-FEED)** — no vehicle-position feed of any format is published; the one GTFS-RT protobuf endpoint carries service alerts only, and the proprietary REST API (key-gated) carries arrival predictions only, never a vehicle's lat/lon.

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **UNASSESSED** — no feed of any format publishes a vehicle position to check |
| Route ID alignment (buses) | **N/A** — nothing to align against |
| Rail line alignment | **N/A** — 0 `route_type=1` routes exist in static at all |
| Blocking classification | **NO-USABLE-FEED** |

**Bottom line:** Static data is clean, keyless, and complete — 237 bus routes with full shape coverage, and a zero-padding quirk between `route_id` and `route_short_name` that an existing config-only transform already handles. But no realtime vehicle-position feed exists in any format: EMT's sole GTFS-RT endpoint is service-alerts only, and its separate proprietary API (itself key-gated) exposes only arrival-time predictions. Even obtaining that API key would not unlock a vehicle position — this is structurally the `tfl.md`/`idfm.md` case, not a credential gap. Onboarding EMT would require EMT to publish a genuinely new data source first; no bespoke adapter can be built against data that doesn't exist today.

## Adding EMT Madrid as a data source

- **Static GTFS zip:** `https://datos.emtmadrid.es/dataset/9b23259a-4491-494b-9695-36a7709b2c12/resource/3cba2058-9833-422c-a704-bf992d31d2ee/download/gtfs_emt.zip` — no auth required, drop-in as a config-only `CityConfig` entry; would pair with the existing `stripLeadingZeros` route-ID normalizer once a live feed exists.
- **Bus realtime:** no usable feed exists — `openapi.emtmadrid.es/v1/bus/servicealerts/proto` (service alerts, keyless) and the proprietary `busemtmad` REST API (arrival predictions, key-gated) are the only two real-time surfaces found, and neither carries a vehicle position; would need a net-new `ITransitCity` implementation *if and only if* EMT ever publishes an actual vehicle-positions source — there is nothing to adapt to today.
- **Rail realtime:** n/a — no rail to onboard (Madrid Metro is a separate authority, Metro de Madrid, not evaluated by this report).
- **Auth:** the static zip requires none. The proprietary `busemtmad` API requires an `EMT_CLIENT_ID`/`EMT_PASS_KEY` pair issued via registration at `mobilitylabs.emtmadrid.es` — moot regardless, since that API carries no vehicle positions to onboard.
- **Config entry vs. new code:** NO-USABLE-FEED — no consumable vehicle-position feed exists in any format today; there is nothing to build a config entry or adapter against.
- **Effort scope:** not viable today — no known unblocker short of EMT publishing a genuinely new vehicle-positions data source.

## Open items for a follow-up pass

- Periodically re-check EMT's Mobility Labs / API docs portal for a future vehicle-positions (or "bus location") endpoint — agencies sometimes add one after service-alerts support ships.
- If Madrid's transit ecosystem is revisited, evaluate Metro de Madrid / CRTM separately — it runs the city's heavy rail and is a distinct authority from EMT, out of scope for this report.
