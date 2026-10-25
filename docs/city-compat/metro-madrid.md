# Metro de Madrid — compatibility spike

**Evaluated:** 2026-10-24; live samples 11:52–11:55 UTC / 13:52–13:55 Europe/Madrid.

**Verdict:** **NO-GO for directly observed train positions on the measured inputs. CONDITIONAL research path for an explicitly estimated Metro experience.** A working, keyless operator arrivals service was found and sampled three times. It carries platform predictions, not train identities or coordinates. It therefore changes the earlier discovery finding but does not make Metro compatible with `GtfsRtCity`.

See the [Madrid city report](madrid.md) for integration with EMT and Cercanías, and [retained evidence](evidence/madrid-metro-emt-2026-10-24.json) for request times, hashes, schemas, joins and measurements. No adapter or position synthesis was implemented.

## Actual realtime source

`GET https://serviciosapp.metromadrid.es/servicios/rest/teleindicadores`

Three unauthenticated requests to this operator-owned host returned HTTP 200 and decoded successfully. Default negotiation returned XML (`VtelindicadoresCollection`); `Accept: application/json` returned an object containing the `Vtelindicadores` array. No credentials were sent. The endpoint was discovered through [client source code](https://github.com/dieguezz/mcp-madrid-public-transport/blob/main/src/transport/metro/infrastructure/metro-api-client.ts); compatibility conclusions below come from direct operator responses, not that project's documentation.

| UTC request start | Received UTC | Format / bytes | Platform records | Prediction age at receipt: median / p95 / maximum | Older than 120 s |
| --- | --- | --- | ---: | --- | ---: |
| 11:52:33.898 | 11:52:36.101 | XML / 263,298 | 545 | 24.1 / 140.1 / 517.1 s | 38/545 |
| 11:53:40.019 | 11:53:41.305 | JSON / 160,010 | 545 | 24.3 / 147.3 / 521.3 s | 38/545 |
| 11:55:04.793 | 11:55:06.098 | JSON / 160,008 | 545 | 25.1 / 118.1 / 389.1 s | 27/545 |

Age uses each record's `FECHAHORAEMISIONPREVISION`, parsed with its explicit UTC offset, against completed receipt time. These are prediction ages, not GPS ages. All 545 records contain that timestamp and at least one non-null countdown field. The first pair shares all 545 platform/destination keys: 480 prediction timestamps and 458 countdown pairs changed. The next pair also shares 545 keys: 498 timestamps and 479 countdown pairs changed. Payload hashes differ across all three requests. Sparse samples establish active updates, not a guaranteed publisher refresh interval.

Fields actually present:

| Field | Measured role / limitation |
| --- | --- |
| `linea`, `nombreli` | Operator line number and line description |
| `idnumerica`, `estaciontel`, `nombreest` | Operator station identity/order/name; not directly interchangeable with CRTM GTFS stop IDs |
| `anden`, `sentido` | Platform and destination/direction |
| `proximo`, `siguiente` | Numeric arrival predictions, with null and zero values observed; confirm units and null/zero semantics with the operator before estimation |
| `FECHAHORAEMISIONPREVISION`, `FECHAHORAREGISTRO` | Prediction and record timestamps, including timezone offsets |
| Train ID, trip ID, moving coordinates, speed, bearing | **Absent from all 545 records in each sample** |

There are 240 distinct operator station IDs and 285 station/line pairs across the full response. `(linea, idnumerica, anden, sentido)` is unique within each snapshot and stable across the sample window. It identifies a prediction board/platform, **not a train**. Neither 545 records nor 240 stations is a reported-vehicle count.

## Modes and route joins

The response includes line values `0`, `1`–`12` and `51`. The 16 rows for `51` describe Pinar de Chamartín–Las Tablas; this is an auxiliary route outside the 13 checked subway GTFS keys. Do not silently include it in Metro subway coverage or infer its complete light-rail integration from these arrivals. Excluding it leaves **529 subway platform records** per sample.

| Mapping check | Result |
| --- | --- |
| Raw line string → checked GTFS short name, full response | 527/545; 18 unmatched (two Ramal rows and 16 auxiliary rows) |
| Explicit `0` → `R` for Ramal, auxiliary rows excluded | 529/529 route-label matches; zero unmatched |
| Above rows whose matched route has a first shape in the checked ZIP | 494/529; 35 line-3 rows lack retained first-shape geometry |
| Exploratory line + normalized station-name join to CRTM itineraries | 527/529 unique matches; two unmatched records at Parque de Lisboa, line 12; zero ambiguous matches |

The station join normalizes accents/case/punctuation and groups inspected topology branch labels by their numeric line prefix (`R` maps to API `0`). This is an exploratory measurement, not a publisher-certified identity crosswalk. Names alone are insufficient at interchanges. For example, line-1 Plaza de Castilla has API `idnumerica=101`, while the joined CRTM station code is `1` (`par_4_1` in the old GTFS). A production crosswalk needs line/platform/direction validation, rename handling and explicit unmatched behavior.

## Static data and a geometry alternative

The [official GTFS item](https://crtm.maps.arcgis.com/home/item.html?id=5c7f2951962540d69ffe8f640d94c246) was downloaded again. Its 1,503,773-byte ZIP has the same SHA-256 as the earlier spike. It contains 13 subway routes, 120 trips and 30 shapes, but service dates end 2026-05-27 and no trips are active on 2026-10-24. The route missing a first referenced shape is **line 3**, `4__3___`. A current ArcGIS catalogue search returned six CRTM GTFS items; its Metro item remains modified in May 2025. The community catalogue still points to that item. No newer Metro timetable was decoded.

There is also an official [CRTM Metro topology service](https://services5.arcgis.com/UxADft6QPcvFyDU1/arcgis/rest/services/M4_Red/FeatureServer), linked from its [catalogue item](https://crtm.maps.arcgis.com/home/item.html?id=0a6c45e7bdd94679b67a2ae662c8838b). Actual complete queries returned:

| Layer/table | Records | Useful data |
| --- | ---: | --- |
| `3` — M4_Andenes | 599 | Platform coordinates, station codes, platform/operator identifiers and direction descriptions |
| `4` — M4_Tramos | 560 | Track/route segment polylines, itinerary, direction, order and line identifiers; 58,613 geometry vertices |
| `5` — M4_ParadasPorItinerario | 592 | Ordered stops/platforms by itinerary and direction |

None of these responses indicated a transfer limit. Queries requested WGS84 (`outSR=4326`) for geometry. The service item was modified in June 2026; all 560 segment records carry `FECHAACTUAL=20260529`. Retrieval today does not prove current closures, schedules or topology. Line labels include branch/direction variants such as `10a`, `10b`, `7a`, `7b`, `9A`, `9B`, `6-1`, `6-2`, `12-1` and `12-2`.

This is useful geometry and stop-order evidence even though the timetable is expired. It is not a live train feed or a substitute timetable. An arrivals-based design might use validated topology without relying on the expired calendar, but would still need geometry reconstruction, branch/direction joins, station offsets and operational validation. The current static loader does not ingest this ArcGIS schema.

The historical NAP download lead `https://nap.transportes.gob.es/api/Fichero/download/1134` was not validated: the local HTTPS request to NAP failed certificate-chain verification. No authenticated NAP ZIP was obtained. Do not describe that lead as a verified current alternative.

## Access, terms and workload

The arrivals service required no key for these probes. No operator-published reuse license, quota, SLA or supported integration contract for this app endpoint was established. Keyless access is not proof of production permission or unlimited polling. The [CRTM license](https://www.crtm.es/licencia-de-uso) governs static CRTM data and requires linked `Powered by CRTM` attribution; it does not automatically cover Metro's separate arrivals service. Metro's normal station webpage returned an access-block notice with HTTP 200 during this investigation; it was not counted as data and no challenge was bypassed.

One full-network arrivals request every 30 seconds would require **2,880 requests/day/instance**, or 5,760 across two continuously active environments, before retries. At the observed JSON size this is about 461 MB/day/instance of response bodies, excluding headers/compression. Ten-second polling would require 8,640 requests/day/instance. These are calculated scenarios, not approved rates. Prefer one shared full-network fetch over hundreds of station requests if the operator permits it. Apply per-record stale handling; an HTTP 200 does not make every platform prediction fresh.

## Paths to inclusion

| Path | What is needed | Current decision |
| --- | --- | --- |
| Actual observed train positions | Authorized positions/track-occupancy feed, train identities, route/direction joins and current geometry | No-go until a source is supplied and measured |
| Estimated movement from live arrivals | Explicit product decision; permitted service access; verified units/zero semantics; station/line/platform crosswalk; track geometry; train association across boards; stale/reversal/branch/loop rules | Conditional design research only; no synthesis approved or implemented |
| Timetable-only animation | Current timetable and a separately approved experience described as scheduled/estimated | Not evaluated; expired GTFS must not be presented as current live service |

For estimated movement, repeated predictions at adjacent platforms may refer to the same approaching train. Creating a vehicle for every prediction would double-count and create false identities. Do not manufacture vehicle descriptors or include estimates in reported-vehicle statistics. Visitor text must say that positions are estimated from arrivals; collection and failure/empty states must remain distinct.

The next operator questions are concrete: is the app endpoint supported for external reuse, what polling budget is allowed, what are the countdown semantics, is a train-identity/position feed available, and where is the current station-ID crosswalk/geometry? These questions were recorded, not sent. A new key alone would not solve the missing position/identity fields.
