# Madrid, Spain — transit inclusion spike

**Evaluated:** 2026-10-24, approximately 11:34–11:39 UTC / 13:34–13:39 Europe/Madrid (Saturday daytime, with active service observed).

**Expanded Metro/EMT investigation:** approximately 11:51–11:56 UTC the same day. See the dedicated [Metro report](metro-madrid.md), expanded [EMT report](emt.md) and [follow-up evidence](evidence/madrid-metro-emt-2026-10-24.json).

**Decision: CONDITIONAL-GO for Cercanías, with separate conditional paths for EMT and Metro.** Cercanías live positions and exact static trip joins work in three updates but need an adapter and scoped static data. EMT has a documented fleet-position collection whose current API must be resolved and measured. Metro has a working live arrivals feed; its records lack train identities and positions, so movement would require a separately designed and explicitly described estimation model. None of these is a configuration-only addition. Full three-mode inclusion remains blocked on the operator-specific gates below.

This is Section 1 of [Adding a city](../ADDING_A_TRANSIT_CITY.md), not an onboarding specification or deployment. Proposed city slug: `madrid`; time zone: `Europe/Madrid`. Measurements are retained in [madrid-2026-10-24.json](evidence/madrid-2026-10-24.json). No runtime configuration was changed.

## Scope and mode decision

| Source / mode | Evidence | Decision |
| --- | --- | --- |
| Renfe Cercanías Madrid, commuter rail (`route_type=2`) | Public nationwide feed; 79, 79 and 81 Madrid rail vehicles with coordinates; all join through static trips and have shape references | Conditional-go; recommended initial scope |
| Renfe replacement buses (`route_type=3`) | Two Madrid vehicles per snapshot, on C9 replacement-bus routes | Exclude from rail-only scope; adding them requires a geometry/category decision |
| EMT urban buses | Current static GTFS; official fleet-position collection `flotapos` discovered; its published/derived v1 data paths return 404. Arrivals API documents bus coordinates and returns 401 without credentials | Conditional-go for access validation; prioritize current fleet-feed routing, then authenticated freshness, coverage and quota |
| Metro de Madrid, subway (`route_type=1`) | Three live arrivals snapshots, 545 platform records each; 529 subway records after excluding auxiliary line 51. Official topology available; checked timetable expired | No-go for direct train positions; conditional design research for movement estimated from arrivals, subject to reuse permission, joins and identity limitations |
| Metro Ligero / tram, CRTM interurban/other urban buses, Renfe long-distance services | No position-compatibility assessment; auxiliary line-51 arrivals observed separately in the Metro response | Unassessed for vehicle positions; excluded |

Cercanías is distinct from Metro de Madrid. A Cercanías-only release must describe regional commuter trains rather than promise the entire network. Those positions are supplied by Renfe. If Metro is later added through arrival-based estimates, distinguish those estimates from source-reported coordinates in visitor copy and statistics. No Metro synthesis is approved or implemented by this spike. Independent GPS ground-truth accuracy was not tested.

## Official sources, access and permitted use

Documentation and catalogue metadata were retrieved on 2026-10-24. Source publication dates are distinguished from this evaluation date.

| Source | Access / documentation | Terms / limits |
| --- | --- | --- |
| [Renfe vehicle locations](https://data.renfe.com/dataset/ubicacion-vehiculos) | Keyless GET; publisher documents 20-second updates. CKAN metadata modified 2026-10-23 | CC BY 4.0. [Renfe conditions](https://data.renfe.com/legal) require source credit, update/license metadata and no implied endorsement. Use `Origen de los datos: Renfe Operadora`, disclose adaptation. No numerical quota found on checked dataset/legal pages: **unverified**, not unlimited |
| [Renfe Cercanías schedules](https://data.renfe.com/dataset/horarios-cercanias) | Keyless GET; metadata modified 2026-10-24 | CC BY 4.0 and Renfe conditions; no static-download quota established |
| [EMT static GTFS](https://datos.emtmadrid.es/dataset/gtfs-de-emtmadrid) | Keyless GET; catalogue API metadata modified 2026-10-24; ZIP last-modified 04:01:09 UTC that day | Catalogue labels static data Open Data Commons Attribution License (`odc-by`); preserve attribution/license information |
| [EMT realtime catalogue](https://datos.emtmadrid.es/dataset/tiempo-real-para-autobuses-de-emt) | Public alerts; authenticated arrivals. Metadata modified 2025-07-08 | Realtime labelled ODbL (`odc-odbl`), separately from static. Confirm applicable API terms and attribution; do not assume the static license covers realtime |
| [EMT fleet-position collection](https://mobilitylabs.emtmadrid.es/es/portal/collectionsDetail/ff594c7a-8a7c-423a-8a06-c14a4fac5bff) | Official Mobilitylabs-owned REACTIVE collection, sharing label PUBLIC, reference `flotapos`; listing dated 2018-10-04. Schema redirects to login; no live fleet decode | [Portal terms](https://mobilitylabs.emtmadrid.es/sip/terms-of-use) require an identifier for dynamic access and `Powered by EMT de Madrid` credit, preferably linked; confirm collection-specific rights and current endpoint/quota |
| [EMT API docs](https://apidocs.emtmadrid.es/) ([served schema](https://apidocs.emtmadrid.es/api_data.js)) | Login with `X-ClientId` / `passKey` headers; `accessToken` on subsequent calls. Served JS last-modified 2024-06-20; embedded generator date 2022-05-25 | Prose says basic up to 25,000/day, advanced 250,000/day; legacy example mentions 20,000/150,000. **Granted account quota unverified.** Inspect actual `apiCounter.dailyUse`. Documented credit names EMT Madrid MobilityLabs |
| [CRTM Metro catalogue](https://datos.comunidad.madrid/dataset/red_de_metro_oferta_planificada_horarios_topologia) | Links to public [official ArcGIS item](https://crtm.maps.arcgis.com/home/item.html?id=5c7f2951962540d69ffe8f640d94c246); successfully retrieved | [CRTM static license](https://www.crtm.es/licencia-de-uso) requires `Powered by CRTM` linked to CRTM, update/license metadata and truthful reuse; does not authorize an unidentified realtime API |
| [Metro app arrivals service](https://serviciosapp.metromadrid.es/servicios/rest/teleindicadores) | Three keyless HTTP 200 responses, XML or JSON by content negotiation; prediction records advance | Operator-hosted, but external reuse contract, numerical quota and supported cadence remain unverified; CRTM static license does not automatically cover this separate service |

Renfe needs no account/key on the measured endpoints. A human would register an EMT application at [MobilityLabs](https://mobilitylabs.emtmadrid.es/) and confirm access, terms and quota. No account, credential or agency contact was created. Proposed local names `EMT_CLIENT_ID` / `EMT_PASS_KEY` are future adapter conventions, not existing generic settings; never record their values.

## Endpoints actually checked

| Payload | URL | Result |
| --- | --- | --- |
| Renfe positions | `https://gtfsrt.renfe.com/vehicle_positions.pb` | Three HTTP 200 responses; protobuf decoded |
| Renfe companion JSON | `https://gtfsrt.renfe.com/vehicle_positions.json` | HTTP 200; schema cross-check, not an extra polling requirement |
| Renfe static | `https://ssl.renfe.com/ftransit/Fichero_CER_FOMENTO/fomento_transit.zip` | HTTP 200; 15,546,483 bytes; ZIP decoded |
| EMT static | `https://datos.emtmadrid.es/dataset/9b23259a-4491-494b-9695-36a7709b2c12/resource/3cba2058-9833-422c-a704-bf992d31d2ee/download/gtfs_emt.zip` | HTTP 200; 21,206,013 bytes; ZIP decoded |
| EMT alerts | `https://openapi.emtmadrid.es/v1/bus/servicealerts/proto` | Two HTTP 200 responses; each decoded to 158 alerts and **zero vehicle entities** |
| EMT arrivals | `https://openapi.emtmadrid.es/v3/transport/busemtmad/stops/62/arrives/all/` | One unauthenticated POST returned HTTP 401; **no authenticated live payload measured** |
| CRTM Metro static | `https://crtm.maps.arcgis.com/sharing/rest/content/items/5c7f2951962540d69ffe8f640d94c246/data` | HTTP 200; 1,503,773 bytes; ZIP decoded, service dates expired |
| Metro arrivals | `https://serviciosapp.metromadrid.es/servicios/rest/teleindicadores` | Three HTTP 200 responses; 545 decoded platform records each; no train IDs or coordinates |
| CRTM Metro topology | `https://services5.arcgis.com/UxADft6QPcvFyDU1/arcgis/rest/services/M4_Red/FeatureServer` | Layers queried successfully: 599 platforms, 560 segments, 592 itinerary-stop records; static geometry, not trains |
| EMT fleet collection detail/schema | [Collection](https://mobilitylabs.emtmadrid.es/es/portal/collectionsDetail/ff594c7a-8a7c-423a-8a06-c14a4fac5bff) / [schema](https://mobilitylabs.emtmadrid.es/es/portal/collectionScheme/ff594c7a-8a7c-423a-8a06-c14a4fac5bff) | Detail HTTP 200; schema redirects to login. Documented discovery and derived reactive-read v1 paths return 404; exact probes in [EMT report](emt.md) |

The initial EMT CKAN searches did not establish a fleet-wide position URL. The deeper MobilityLabs search found an official fleet collection, but its live API routing remains unresolved. EMT's [2025-03-17 announcement](https://www.emtmadrid.es/Noticias/Madrid-cuenta-con-la-posicion-de-los-autobuses-de.aspx) also confirms sharing GTFS-RT positions with Google; access for TransitJazz is not established. Ask about both sources before choosing thousands of stop requests. A collection label or a 404 is not a measured live-fleet result.

## Static GTFS measurements

| Measure | EMT | Renfe, nationwide | Checked Metro ZIP |
| --- | ---: | ---: | ---: |
| Route rows | 237 | 273 | 13 |
| Trip rows | 88,516 | 129,971 | 120 |
| Stop rows | 4,933 | 1,147 | 1,050 |
| Shape IDs / points | 474 / 194,812 | 144 / 123,734 | 30 / 57,474 |
| Routes whose first referenced shape exists | 237/237 | 261/273 | 12/13 |
| Calendar rows / exceptions | 5 / 17 | 450 / 0 | 4 / 56 |
| Calendar date envelope | 2026-10-23–2026-12-31 | 2026-10-24–2026-11-22 | 2025-01-01–2026-05-27 |
| Trips active on 2026-10-24 | 26,241 | 3,943 | 0 |
| Route rows active that day | 221 | 236 | 0 |
| Route types | 237 bus | 223 rail; 50 bus | 13 subway |

All three ZIPs include routes, trips, shapes, stops, stop times and calendar files. EMT and Metro also include calendar exceptions and frequencies. Activity applies weekday flags and exceptions. The date envelope does not mean every route runs daily. Stop-time referential integrity and frequency expansion were not exhaustively validated; the proposed positions adapter needs a trip-to-route lookup, not timetable-derived positions.

The inspected Renfe Madrid namespace is `route_id` starting `10`, verified against route names and joined Madrid coordinates. It contains 40 route rows: 38 rail and two C9 replacement-bus rows; 36,045 trip rows (35,965 rail); 1,114 active trips on the sample date (1,104 rail). All 40 route rows have a first shape. The referenced Madrid shape set contains 12,290 points; the rail subset references 24 shapes including opposite directions.

The 12 Madrid display keys are C1, C2, C3, C4, C4a, C4b, C5, C7, C8a, C8b, C9 and C10. Ten appear as active rail vehicles; C4 and C9 do not, so live operation of those rail keys remains unverified. The two C9 bus vehicles must not be advertised or snapped as trains.

The checked Metro ZIP's `feed_version` is `20250527`, with final calendar date 2026-05-27. A second download returned identical bytes. The missing first-shape route is line 3. The current community catalogue and CRTM ArcGIS search still lead to this item; no newer timetable was decoded. Separate official topology is available, detailed below. No live Metro positions were decoded; live arrivals were.

## Live Renfe measurements and joins

UTC times. Each sample is a separate request with different payload hashes and advancing header timestamps. Age is measured at request start and excludes network latency.

| Request start | Header time | Bytes | Nationwide vehicles | Madrid rail / coordinates / trip+shape joins | Madrid replacement buses | Rail timestamp age |
| --- | --- | ---: | ---: | --- | ---: | --- |
| 11:34:09.837 | 11:34:07 | 25,911 | 301 | 79 / 79 / 79 | 2 | 2.84 s |
| 11:34:46.798 | 11:34:30 | 25,948 | 301 | 79 / 79 / 79 | 2 | 17.80 s |
| 11:37:03.759 | 11:36:50 | 26,413 | 307 | 81 / 81 / 81 | 2 | 14.76 s |

Across the respective nationwide denominators of 301, 301 and 307:

- Trip ID, vehicle descriptor ID and vehicle timestamp are present in every entity.
- `trip.route_id`: **0/301, 0/301, 0/307**. Direct route matching cannot run.
- Exact `trip_id` joins to downloaded static trips: **301/301, 301/301, 307/307**; unmatched counts all zero.
- Coordinates: 300/301, 300/301, 306/307. The missing-position entity is outside Madrid rail.
- Speed and bearing are absent throughout; optional for the worker.

The first two snapshots share all 79 Madrid rail descriptor IDs: 15 changed coordinates and all 79 advanced timestamps. The next pair shares 75 IDs: 37 changed coordinates and all 75 advanced timestamps. This demonstrates active updates and short-window continuity, not all-day physical-fleet identity stability. The first header interval is 23 seconds; sparse sampling does not establish sustained cadence. The publisher documents 20 seconds.

Sample exact joins from the first payload:

| Realtime `trip_id` | Static `route_id` | Short name | Trip shape | Result |
| --- | --- | --- | --- | --- |
| `1095S27455C4b` | `10T0016C4b` | C4b | `10_C4b_INV` | Matched; position present |
| `1095S27254C4a` | `10T0013C4a` | C4a | `10_C4a` | Matched; position present |
| `1095S27937C5` | `10T0018C5` | C5 | `10_C5_INV` | Matched; position present |
| `1095S27938C5` | `10T0017C5` | C5 | `10_C5` | Matched; position present |

Joined Madrid rail coordinates span roughly latitude 40.035–40.738 and longitude -4.066–-3.182, consistent with regional coverage. Exact example coordinates/timestamps are retained in the evidence. Track-level accuracy, tunnel behavior and browser snapping were not measured.

### Route collisions and current worker behavior

Inspected current code: [GtfsRtCity.cs](../../src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/GtfsRtCity.cs), [CityConfig.cs](../../src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/CityConfig.cs), [GtfsStaticLoader.cs](../../src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs), [Worker.cs](../../src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs), [GtfsRtModels.cs](../../src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/GtfsRtModels.cs).

`ProcessSpatialReconciliationAsync` requires a position and nonempty `Vehicle.Trip.RouteId`; it does not resolve trip IDs. All positioned Renfe vehicles would currently be skipped for missing route identity. `RailRouteIdMap` and `RouteIdNormalization` modify an existing route ID; neither supplies the missing value.

`BuildRouteIndex` uses short names as primary keys **and aliases retained raw route IDs**. The old skill/report assumption that only short names match is outdated. Static loading trims CSV whitespace (important for Renfe's padded columns), chooses the first trip shape per route and keeps the first feature per display key. An unfiltered national ZIP is unsuitable:

- Nationwide, 37 short-name groups repeat, covering 272 of 273 route rows. Repeats include directions/variants and different cities. A national C1 must not become Madrid's C1.
- Madrid has 40 route rows but only 12 display keys, including directional shapes and rail/bus C9 overlap. Later features and their raw aliases can be suppressed; a selected-geometry policy is required.
- EMT and Renfe collide on **C1 and C2**. A combined city needs source/mode-specific canonical join keys with separate friendly labels.
- Metro and EMT also collide on **all numeric display keys 1–12**. Adding Metro makes a combined source/mode namespace mandatory; changing realtime IDs without matching static/client keys would not resolve the collision.

All sampled Madrid rail trips have source shape references. That does **not** prove the current loader retains each route/shape. Validate selected branch/directional geometry and snapping during implementation acceptance.

## Metro path: live arrivals and topology, with missing train identity

The [dedicated Metro report](metro-madrid.md) replaces the earlier discovery gap with measured evidence. The operator's full-network arrivals endpoint returned three distinct updates: **545 platform records each**, of which 529 match the checked subway route labels after excluding auxiliary `51` and mapping Ramal `0` to `R`. All records have prediction timestamps and at least one countdown value. None supplies a train identity or moving coordinate.

Prediction median age was 24–25 seconds; p95 ranged from 118–147 seconds, with maxima of 389–521 seconds. The first two snapshots changed prediction times for 480/545 platform keys and countdowns for 458/545. Treat stale predictions per record; an HTTP 200 is not whole-feed freshness. The API's stable platform key is not a vehicle ID.

Official CRTM topology queries provide **599 platforms, 560 segment polylines and 592 ordered itinerary-stop records**, without transfer-limit flags. They include branch/direction identifiers and 58,613 geometry vertices. All segment records are dated 2026-05-29; current operational coverage still needs validation. An exploratory line/name join matches 527/529 subway platform records uniquely; two Parque de Lisboa records remain unmatched. This is not an accepted production identity crosswalk.

For inclusion, choose explicitly between acquiring real train positions and designing estimates from arrivals. An estimation design needs permitted API use, countdown semantics, validated stop/platform/direction crosswalks, track offsets, train association across boards and stale/branch/loop rules. It cannot count platform predictions as reported trains. The topology service could supply geometry without depending on the expired timetable, but its ArcGIS schema needs an ingestion path. This is a conditional research opportunity, not approval to simulate current trains from stale schedules.

## EMT path: fleet-level access first, arrivals as fallback

The earlier [EMT report](emt.md) has been replaced. EMT's Google announcement and served arrivals schema contradict its absolute no-positions conclusion. The schema identifies `Arrive.geometry` as bus position and includes `bus` and `line`; examples contain different coordinates per approaching bus. These are **documentation examples, not live measurements**.

The deeper investigation also found official collection `ff594c7a-8a7c-423a-8a06-c14a4fac5bff`, **Posición de la Flota de Autobuses de EMT**, owned by Mobilitylabs with REACTIVE reference `flotapos`. Its detail page is public, but the schema requires login and both the documented v1 discovery route and derived reactive-read route returned 404. Its age and missing API response prevent a live compatibility claim. The next step is confirming the supported host/version/link ID and access scope with the publisher, not assuming a key fixes a retired route.

Arrivals are JSON, POST and session-header authenticated. `GtfsRtCity` only GETs protobuf and appends a query key. A generic API-key setting cannot handle EMT login/token renewal or decoding. Static loading remains keyless; do not attach an EMT session credential to its ZIP.

Authenticated samples must establish freshness, agreement across stop records for the same bus, stable IDs, line joins, successful-empty semantics and the actual fleet subset visible. Do not use stop geometry as vehicle position, or response time as an independently measured bus GPS timestamp. No bus route-join denominator is available. Static examples include `001` → short name `1`; worker aliases mean zero-stripping should not be assumed necessary before measuring live line values.

The [EMT report](emt.md) now compares fleet collection, shared GTFS-RT and selected-stop fallback paths, records exact failed probes and lists operator questions. [Published MobilityLabs terms](https://mobilitylabs.emtmadrid.es/sip/terms-of-use) require an identifier for dynamic web services and `Powered by EMT de Madrid` credit, preferably linked, with update/license metadata retained. A PUBLIC catalogue label does not remove that access requirement. Actual granted quota and collection-specific rights remain unverified.

## Polling budget and worker load

The current production-host base and development configuration set `Worker:CycleIntervalSeconds` to **10**. Generic cities fetch realtime URLs each cycle; `CityConfig` has no general per-city cadence field. Effective deployed instance/environment counts were not inspected.

For endpoint count `E`, active instances across all environments `I`, interval `T` seconds and retries `R/day`:

`requests/day = E × I × 86,400 / T + R`, plus login and static refreshes.

| Scenario | Baseline requests/day before retries/login |
| --- | ---: |
| One Renfe endpoint / one instance at current 10 s | 8,640 |
| One Renfe endpoint / one instance at proposed 20 s | 4,320 |
| One Renfe endpoint / one instance at 30 s | 2,880 |
| Two environments / one Renfe endpoint and instance each at 20 s | 8,640 combined |
| EMT: one selected stop / one instance at 30 s | 2,880 |
| EMT: one working fleet endpoint / one instance at 30 s, if made available | 2,880 |
| Metro: one full-network arrivals request / one instance at 30 s | 2,880 |
| Potential Renfe + EMT fleet + Metro arrivals, each at 30 s / one instance | 8,640 combined |
| Same three-source scenario / two continuously active environments | 17,280 combined |
| EMT: all 4,933 stops / one instance at 10 s | 42,621,120 |
| EMT: all 4,933 stops / one instance at 30 s | 14,207,040 |

Full-stop EMT polling exceeds even the documentation's 250,000/day figure. At 10 seconds that figure permits fewer than 29 stop calls per cycle for one instance before overhead; actual quota may differ. Selected stops cover only a measured subset, not a complete fleet. Prefer an authorized fleet feed if offered.

The three-source scenario is a budget calculation, not a working configuration: EMT fleet routing, Metro reuse permission and each publisher's allowed cadence must be verified separately. Metro JSON snapshots measured about 160 kB, or about 461 MB/day of response bodies per instance at 30 seconds. Static topology belongs in a refresh cache, not a per-cycle download.

Renfe payloads measured 25.9–26.4 kB nationwide: approximately 112–114 MB/day of response bodies at 20 seconds per instance, excluding headers/compression. Use a cached static lookup: **do not download the 15.5 MB ZIP or call trip/stop APIs each cycle**. National `trips.txt` alone is 19,625,772 uncompressed bytes; memory, refresh CPU and worker overhead are unbenchmarked. Existing static refresh defaults to 24 hours; coordinate mapping and geometry refreshes and handle unmapped new-service trips explicitly.

Twenty-second polling is a proposed starting point matching Renfe documentation, contingent on permitted workload. It requires an adapter throttle/cache or planned cadence extension; changing the global interval affects other cities. Cached data must not become a fresh source observation. Include retries, timeout/backoff, cancellation and actual deployment owners in the final budget. No load or rate-limit stress test was performed.

## Selected implementation path and remaining gates

**Measured starting path:** a focused `MadridCity : ITransitCity` using existing protobuf models, exact static trip-to-route lookup and Madrid-only `route_type=2` selection for Cercanías. For full-city scope, add independently validated source components: EMT's eventual fleet/arrivals authentication and JSON handling, and either a genuine Metro positions source or a separately approved arrivals-estimation design. Reuse existing infrastructure, with one consistent namespaced route/geometry representation and independent source outcomes. No synthesis is implemented by this spike.

Before implementation:

1. Confirm Renfe's polling allowance through current official guidance or the publisher; calculate the actual production/development/replica/retry budget. No key is needed for measured rail access.
2. Specify exact trip/static city attribution, trimming, unmapped-trip behavior, coordinated refresh and selected directional/branch geometry. Exclude other cities and replacement buses. Do not identify Madrid solely from labels such as C1.
3. Resolve reported-vehicle identity and coverage. Nationwide descriptor IDs `71891` and `26922` each repeat twice per snapshot. Short-window continuity does not prove permanent physical-train identity. Never count the national feed as Madrid. Madrid-scoped capture, duplicates and unknown-trip coverage need an explicit contract; otherwise keep reported-vehicle counting unsupported. Do not invent fallback identities or freshness from cached polls.
4. For EMT, establish the current fleet endpoint before assuming credential provisioning is sufficient. A human obtains appropriate access; authenticated samples must pass equivalent checks with a quota-feasible strategy. For Metro, secure permission and choose observed positions versus estimated movement; the live arrivals evidence does not solve train identity. Resolve both C1/C2 and numeric 1–12 cross-mode collisions, truthfully distinguish estimates and keep unsupported vehicle-count coverage explicit.

Once those gates are satisfied, prepare specification/plan/tasks under the onboarding guide, covering both-host registration, static selection, failure versus successful-empty handling, attribution and truthful rail-only visitor copy. Build/startup, live worker matching, browser motion, listening, production load and deployment remain **unverified**. This spike provides no implementation or release approval.
