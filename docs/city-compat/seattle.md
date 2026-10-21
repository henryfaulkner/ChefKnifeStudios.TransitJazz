# Seattle feed compatibility: King County Metro + Sound Transit

**Evaluated:** 2026-10-18  
**Proposed city slug:** `seattle`  
**Decision:** **CONDITIONAL GO** to plan one combined Seattle entry using direct agency feeds, with route `512` intentionally omitted from initial map/audio coverage. The user accepted this omission on 2026-10-18. The current generic multi-URL `GtfsRtCity` configuration is not safe to use unchanged; implementation needs source-specific authentication and overlap handling. Confirm the request rate with Sound Transit before rollout.

## Decision and recommended path

The feeds decode as standard GTFS-Realtime and the current direct-agency static feeds can be merged without route-key conflicts. King County Metro has strong measured coverage: all 42 distinct live route IDs in five snapshots joined to a static route with geometry. Sound Transit’s 5 observed live route IDs joined only 4/5 against the current direct-agency static files; route ID `512` had no matching route or shape there. A separate download of the consolidated regional GTFS found geometry for `512`; that probe is diagnostic evidence only and the file is explicitly excluded from the recommended onboarding path.

Use the two direct agency static files and a focused Seattle multi-source adapter that keeps the KCM feed keyless, applies the OneBusAway key only to the Sound Transit source, maps each source’s internal route IDs to the display keys in the static union, and deduplicates the four overlapping vehicle observations seen in each paired sample. The user accepted omission of route `512` on 2026-10-18: exclude it from initial map/audio coverage, preserve applicable raw reported-vehicle capture semantics, and do not use the retiring consolidated feed. Before rollout, confirm acceptable polling volume with Sound Transit. Do not configure both URLs under the current generic city path with a shared API key: it appends the same key to every URL, including the public KCM S3 host.

## Current official guidance and feed sources

Sound Transit’s [OTD downloads page](https://www.soundtransit.org/help-contacts/business-information/open-transit-data-otd/otd-downloads) currently says that agency 40 vehicle-position data covers the 1 Line, 2 Line, T Line, and ST Express buses; it does not list Sounder vehicle positions. It requires an API key for real-time access and says requests are normally processed within two business days. This replaces the July `sound-transit.md` estimate of approximately 20 business days. The same page says the consolidated Puget Sound GTFS will be decommissioned at the end of 2026 and encourages direct agency feeds.

The [King County Metro feed page](https://kingcounty.gov/depts/transportation/metro/travel-options/bus/app-center.aspx) describes GTFS-RT vehicle positions and says they must be used with Metro’s GTFS. Its static description includes Metro bus, Seattle Streetcar, Water Taxi, Link light rail, and some ST Express service. The actual KCM static zip downloaded for this evaluation contained 138 `route_type=3` routes, two `route_type=0` routes, and two `route_type=4` routes; it did not contain a Link route in this snapshot.

Measured static downloads were [KCM OTD GTFS](https://www.soundtransit.org/GTFS-KCM/google_transit.zip), [Sound Transit OTD GTFS](https://www.soundtransit.org/GTFS-rail/40_gtfs.zip), and the [regional consolidated GTFS](https://gtfs.sound.obaweb.org/prod/gtfs_puget_sound_consolidated.zip). Measured real-time endpoints were KCM’s [keyless vehicle-position object](https://s3.amazonaws.com/kcm-alerts-realtime-prod/vehiclepositions.pb) and OneBusAway agency 40. OneBusAway documents the per-agency GTFS-RT endpoint and `key` parameter in its [GTFS-Realtime Export API guide](https://developer.onebusaway.org/api/gtfs-realtime). The documented key requirement remains the production assumption even though the agency 40 endpoint also returned data without a key during this probe.

Sound Transit’s [Transit Data Terms](https://www.soundtransit.org/help-contacts/business-information/open-transit-data-otd/transit-data-terms-use) require the terms to be passed to downstream recipients of the data, usage metrics to be supplied on request, and use of the latest published data. The terms also reserve the agencies’ ability to change, limit, or discontinue data. No numeric request quota or polling interval is stated in the official pages reviewed. Ask OTD to confirm the planned request rate before deployment.

## Measurements

All measurements below were collected on 2026-10-18. Static files were fetched directly from the official OTD URLs. Each real-time feed was fetched five times without a key about 12 seconds apart, from 12:38:11 to 12:39:01 UTC (Sunday morning in Seattle). A separate OneBusAway request containing the privately provisioned key as a query parameter succeeded at 12:42:26 UTC. No credential or credentialed URL is recorded here. The endpoint also returned data without a key, so this response does not independently establish that OneBusAway accepted or validated the supplied key. Protobuf decoding followed the current worker model in `GtfsRtModels.cs`, rather than older decoder examples in the skill notes. Static inspection covered routes, trips, shapes, and stops; `calendar.txt` and `calendar_dates.txt` service-date validity was not checked.

### Static GTFS and geometry

| Static feed | Routes / trips | Shapes / points | Stops | Route-type counts | Routes with geometry |
| --- | ---: | ---: | ---: | --- | ---: |
| KCM OTD | 142 / 32,635 | 424 / 167,087 | 6,265 | 138 bus (`3`), 2 streetcar (`0`), 2 water taxi (`4`) | 142/142 |
| Sound Transit OTD | 7 / 19,606 | 21 / 9,283 | 380 | 3 light rail (`0`), 2 commuter rail (`2`), 2 bus/shuttle (`3`) | 6/7 |
| Direct-file union, KCM then Sound Transit | 149 route rows | — | — | — | 148 route display keys; no duplicate display keys or conflicting route-ID mappings |
| Regional consolidated file (diagnostic-only probe; excluded from onboarding) | 391 / 120,783 | 1,102 / 816,013 | 13,351 | 334 bus (`3`), 5 tram (`0`), 4 rail (`2`), 48 ferry (`4`) | 385/391 |

The direct Sound Transit zip’s one route without geometry was not a match for live route ID `512`; `512` was absent from both direct agency route maps. Sound Transit’s [current route 512 schedule](https://www.soundtransit.org/ride-with-us/routes-schedules/512) confirms the service is active ST Express. Among the official OTD direct static bundles reviewed, no additional direct GTFS source for its route and shape was listed. The consolidated file contained a `512` route with geometry, but this fact is diagnostic only: that file is excluded from onboarding and planning. This recheck found 391 routes in the consolidated file, rather than the 411 reported in `KEY_ACQUISITION.md` on 2026-08-29.

The current worker static loader accepts multiple static zip URLs and merges route shapes by display key. With these direct files, the measured union had 148 geometry-bearing display keys and no duplicate display keys or route-ID-to-display-key conflicts. The worker classifies GTFS route types 0, 1, and 2 as rail and all others as bus; therefore its current categories classify the two KCM streetcar routes as rail and the two water taxi routes as bus. This is an existing categorization limitation to account for in product expectations.

### Real-time decoding, freshness, and route joins

| Feed | Requests | Vehicles per response | Route IDs | Position / descriptor / timestamp coverage | Median vehicle timestamp age | Route-to-geometry join |
| --- | ---: | ---: | ---: | --- | --- | --- |
| KCM direct S3 | 5/5 HTTP 200 | 83–85 | 41–42 distinct per sample; 42 across all five | 100% had route and vehicle descriptor; 98–99% had coordinates and timestamps | 31–56 seconds across samples | 42/42 distinct route IDs mapped to geometry; exact display-key match was 0/42 before mapping |
| Sound Transit agency 40, no key | 5/5 HTTP 200 | 19 | 5 distinct in every sample | 100% had coordinates, vehicle descriptor, and timestamp; 16/19 had route IDs | 40–66 seconds across samples | 4/5 distinct route IDs mapped to geometry; 15/19 vehicles per sample had both a route and direct-static geometry |
| Sound Transit agency 40, request with key parameter | 1/1 HTTP 200 | 20 | 5 distinct | 100% had coordinates, vehicle descriptor, and timestamp; 19/20 had route IDs | 41 seconds | 4/5 distinct route IDs mapped to direct-static geometry |

KCM header timestamps were 8–20 seconds old at receipt and advanced on three of four sample intervals; Sound Transit headers were 2–15 seconds old and advanced on three of four intervals. The response containing the key parameter had a header age of about 14 seconds. None of the sampled vehicles supplied speed or bearing. The OBA route IDs `100451` and `102767` map to display routes `556` and `547`; `100479` maps to `1 Line`; and `2LINE` maps to `2 Line`. Route `512` did not join the direct static union. A route ID’s presence in the feed does not establish support for all Sound Transit modes: official OTD guidance limits stated real-time coverage to the 1 Line, 2 Line, T Line, and ST Express. No Sounder vehicle was observed in this Sunday snapshot, which is not evidence about weekday operation.

Both the no-key requests and the agency 40 request containing the key parameter returned valid GTFS-RT version 2.0 data. The latter contained 20 vehicles and matched the same five distinct route IDs as the no-key samples. Because the endpoint also returned data without a key, key acceptance remains unverified; only successful decoding of the request carrying the provisioned value is established. This test does not establish a quota or sustained availability.

### Cross-feed collisions and request volume

Each of the five paired KCM/Sound Transit snapshots shared exactly four vehicle-descriptor IDs, all on the same two mapped ST Express routes (`556` and `547`); there were no overlapping GTFS-RT entity IDs. Three or four of the four paired positions were within 500 metres in each interval, and all four were within 1 metre in one interval. This is strong evidence that the same vehicles are represented by both feeds. A naive merged entity list would therefore duplicate observations, and the current worker prefers the vehicle descriptor ID for its vehicle state key. Deduplicate overlapping vehicles or retain one source for those routes before merging.

The worker’s default cycle interval is 10 seconds. If both endpoints are fetched once per cycle by one continuously active instance, the implied load is 8,640 calls per endpoint per day, or about 17,280 combined per instance per day, before retries. Production cycle interval and active instance count were not verified. This is a planning estimate, not a documented or approved quota.

## Recheck of prior findings and remaining conditions

- `kcm.md` (2026-07-25) found a 103-entry route map and 157 static routes from its earlier samples. This recheck again found that KCM’s internal real-time `route_id` values differ from display keys, and confirmed every sampled distinct KCM route maps through static GTFS to geometry. The current OTD download contained 142 routes; the five Sunday samples are too small to replace the earlier full-service route inventory.
- `sound-transit.md` (2026-07-25) treated the feed as blocked pending a key and estimated a 20-business-day wait. The current official request page says two business days; a request containing the provisioned key parameter succeeded, but key acceptance remains unverified because the endpoint also returned data without it. The current direct Sound Transit static zip contained 7 routes (6 with geometry), not the report’s prior 8-route count. Current official documentation does not list Sounder vehicle positions.
- `KEY_ACQUISITION.md` (2026-08-29) recorded a 411-route consolidated file and a 43-vehicle sample. The diagnostic-only recheck found 391 routes and 385 route geometries, and paired direct-feed probes exposed cross-feed duplication. The file is scheduled for decommission at the end of 2026 and is excluded from the recommended onboarding path.

Before implementation, account for these three conditions:

1. Add source-specific authentication so the OBA key is sent only to OneBusAway and never to the keyless KCM host.
2. Deduplicate the shared KCM/ST Express vehicles, or select one source for routes `556` and `547`.
3. Implement the user-approved initial exclusion of route `512` from map/audio coverage and document the limitation in visitor copy and acceptance checks. Preserve applicable raw reported-vehicle capture semantics. A future addition requires durable direct-agency geometry; the consolidated feed was probed only for comparison and is rejected as a source.

Before rollout, confirm the proposed polling rate with Sound Transit because the quota is unpublished. Calendar and exception-date validity should also be verified against the intended service period when preparing the production static configuration; it was not measured in this evaluation.

This is a feed-compatibility evaluation only. No runtime code or city configuration was changed.
