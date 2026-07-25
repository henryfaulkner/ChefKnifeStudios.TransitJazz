# Contract: MARTA Rail Realtime Feed (inbound)

**Direction**: Inbound — MARTA → `RailRealtimeAdapter`.
**Transport**: `GET {BaseUrl}?apiKey={ApiKey}` over HTTPS (port 18096). Standard
`IHttpClientFactory` client, normal TLS validation (no override).
**Response**: JSON **array**; one element per `(train, upcoming-station)`. All values are strings.

## Element schema (fields the adapter reads in **bold**)

```json
{
  "DESTINATION": "Airport",
  "DIRECTION": "S",
  "EVENT_TIME": "01/23/2025 11:50:06 AM",   // → Vehicle.Timestamp (parse MM/dd/yyyy hh:mm:ss tt)
  "IS_REALTIME": "true",                    // → keep only when == "true"
  "LINE": "RED",                            // → Vehicle.Trip.RouteId  (route index key)
  "NEXT_ARR": "11:52:49 AM",
  "STATION": "AIRPORT STATION",
  "TRAIN_ID": "401",                        // → FeedEntity.Id / Vehicle.Vehicle.Id
  "WAITING_SECONDS": "107",
  "WAITING_TIME": "1 min",
  "DELAY": "T582S",
  "LATITUDE": "33.660274",                  // → Position.Latitude  (double.TryParse, InvariantCulture)
  "LONGITUDE": "-84.447091"                 // → Position.Longitude
}
```

## Adapter obligations (load-bearing)

1. **Realtime filter (FR-004)**: drop any element whose `IS_REALTIME` (trimmed, case-insensitive)
   is not `"true"`, **before** dedup.
2. **De-dup (FR-003)**: collapse to **one record per `TRAIN_ID`**.
3. **Contract guard (FR-013)**: all surviving rows for a `TRAIN_ID` MUST share one parsed
   `(LATITUDE, LONGITUDE)`. Violation → loud `Warning` log; still emit using the first row.
4. **Parse safety**: `LATITUDE`/`LONGITUDE` via `double.TryParse` + `InvariantCulture`; rows that
   fail, or that lack `TRAIN_ID`/`LINE`, are skipped and counted (diagnostic), never thrown.
5. **Best-effort (FR-008)**: any HTTP/parse failure → log `Warning`, return empty list.

## Accept / reject vectors

| # | Input | Expected adapter behavior |
|---|-------|---------------------------|
| A1 | Element with `IS_REALTIME:"true"`, valid lat/lon, `LINE:"RED"` | Accepted → one `FeedEntity` (after dedup). |
| A2 | Same `TRAIN_ID` appears 11× (11 stations), identical lat/lon | Collapsed to **one** `FeedEntity`; no warning. |
| A3 | `LINE:"GREEN"` / `"GOLD"` / `"BLUE"` | Accepted; `RouteId` matches index key verbatim. |
| A4 | Empty array `[]` | Empty `FeedEntity` list; bus path unaffected. |
| R1 | `IS_REALTIME:"false"` | Dropped before dedup (no entity). |
| R2 | `IS_REALTIME` missing / `"True "` with whitespace | Trim + case-insensitive compare; `"True "`→ kept, `""`/missing → dropped. |
| R3 | `LATITUDE:"abc"` (unparseable) | Row skipped + counted; not thrown. |
| R4 | `TRAIN_ID` empty or `LINE` empty | Row skipped + counted. |
| R5 | One `TRAIN_ID` with **two different** coordinates across its rows | Loud `Warning` (FR-013); emit using first row. |
| R6 | HTTP 500 / timeout / non-JSON body | `Warning` logged; **empty** list returned (FR-008). |
| R7 | HTTP 200 with **no** API key configured | Works (endpoint observed keyless 2026-06-23); do not hard-fail on empty key. |

## Notes

- `EVENT_TIME` is per-row (e.g. `06/23/2026 10:52:57 PM`) and parseable; on parse failure set
  `Vehicle.Timestamp = null` (staleness simply not flagged that tick).
- `WAITING_SECONDS` / `NEXT_ARR` are **reserved** for the optional ETA-pacing refinement and are
  out of scope for v1.
