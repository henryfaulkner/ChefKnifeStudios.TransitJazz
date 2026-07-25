# Phase 1 Data Model: MARTA Rail Realtime

Three representations: the **raw inbound DTO** (`RailArrivalDto`), the **de-duped intermediate**
(`RailTrain`), and the **outbound** existing `FeedEntity` (`GtfsRtModels.cs`, unchanged). The
adapter's job is `List<RailArrivalDto>` → `List<RailTrain>` → `List<FeedEntity>`.

---

## Entity 1: `RailArrivalDto` (NEW — raw inbound JSON element)

One element per `(train, upcoming-station)`. **All values are JSON strings.** Only the fields
the adapter uses are required; the rest are carried for debugging / future use.

| JSON field | DTO property | Type | Used in v1 | Notes |
|------------|--------------|------|-----------|-------|
| `TRAIN_ID` | `TrainId` | string | ✅ | → `FeedEntity.Id` and `Vehicle.Vehicle.Id` |
| `LINE` | `Line` | string | ✅ | → `Vehicle.Trip.RouteId`; expected ∈ `{RED,GOLD,BLUE,GREEN}` |
| `LATITUDE` | `Latitude` | string | ✅ | `double.TryParse`, `InvariantCulture` → `Position.Latitude` (float) |
| `LONGITUDE` | `Longitude` | string | ✅ | `double.TryParse`, `InvariantCulture` → `Position.Longitude` (float) |
| `IS_REALTIME` | `IsRealtime` | string | ✅ | drop row unless equals `"true"` (case-insensitive trim) |
| `EVENT_TIME` | `EventTime` | string | ✅ | parse `MM/dd/yyyy hh:mm:ss tt` → `Vehicle.Timestamp` (Unix seconds) |
| `STATION` | `Station` | string | ⬜ | per-station; ignored after dedup |
| `NEXT_ARR` | `NextArr` | string | ⬜ | reserved for optional ETA-pacing (out of v1 scope) |
| `WAITING_SECONDS` | `WaitingSeconds` | string | ⬜ | reserved for optional ETA-pacing |
| `DESTINATION` | `Destination` | string | ⬜ | debug only |
| `DIRECTION` | `Direction` | string | ⬜ | debug only |
| `DELAY` | `Delay` | string | ⬜ | debug only |

**Validation / parse rules**
- A row is **dropped** (not an error) if `IsRealtime` is not `"true"`.
- A row is **skipped with a diagnostic counter** if `Latitude`/`Longitude` fail `double.TryParse`,
  or if `TrainId` or `Line` is null/empty.
- `EventTime` parse failure → `Vehicle.Timestamp = null` (tolerated; the staleness check at
  `Worker.cs:197` simply won't flag staleness for that train that tick).

## Entity 2: `RailTrain` (NEW — de-duped intermediate, one per train)

Produced by grouping surviving `RailArrivalDto` rows by `TrainId`.

| Field | Type | Source | Rule |
|-------|------|--------|------|
| `TrainId` | string | group key | non-empty |
| `Line` | string | first row | non-empty; route-index key |
| `Latitude` | double | first row (parsed) | finite |
| `Longitude` | double | first row (parsed) | finite |
| `EventTimeUtc` | `DateTime?` | first row (parsed) | UTC; null tolerated |

**Contract guard (FR-013)**: within a `TrainId` group, **all** rows must share one
`(Latitude, Longitude)` (compared on the parsed doubles). If not, log a loud `Warning`
("rail live-position contract violated for TRAIN_ID {id}") and still emit using the first row —
the warning is the signal that OQ-1's assumption broke.

## Entity 3: `FeedEntity` (EXISTING — `GtfsRtModels.cs`, emitted unchanged)

The adapter constructs these so reconciliation cannot distinguish trains from buses.

| Target (existing) | Value from `RailTrain` |
|-------------------|------------------------|
| `FeedEntity.Id` | `TrainId` |
| `FeedEntity.Vehicle` (`VehiclePosition`) | new instance (below) |
| `VehiclePosition.Vehicle.Id` (`VehicleDescriptor`) | `TrainId` |
| `VehiclePosition.Trip.RouteId` (`TripDescriptor`) | `Line` (`"RED"` etc.) |
| `VehiclePosition.Position.Latitude` (float) | `(float)Latitude` |
| `VehiclePosition.Position.Longitude` (float) | `(float)Longitude` |
| `VehiclePosition.Position.Speed` | `null` (feed has no speed) |
| `VehiclePosition.Position.Bearing` | `null` (derived downstream from along-shape direction if at all) |
| `VehiclePosition.Timestamp` (ulong?) | `EventTimeUtc` → Unix seconds, or `null` |

> `Position.Latitude/Longitude` are `float` in `GtfsRtModels.cs` (lines 94/96). Parse to `double`
> for the dedup-coordinate comparison (precision), then cast to `float` when building `Position`.

## Entity 4: `RailRealtimeOptions` (NEW — bound config)

| Property | Config key | Default | Notes |
|----------|-----------|---------|-------|
| `BaseUrl` | `Marta:RailRealtime:BaseUrl` | (committed) | full endpoint incl. path; key appended at call time |
| `ApiKey` | `Marta:RailRealtime:ApiKey` | (secret/env) | **never committed**; may be empty if endpoint is keyless |
| `Enabled` | `Marta:RailRealtime:Enabled` | `true` | lets ops/tests toggle rail off → proves additive-only (FR-009) |

## Flow summary

```
HTTP GET {BaseUrl}?apiKey={ApiKey}
   → List<RailArrivalDto>                       (System.Text.Json)
   → where IsRealtime == "true"                 (FR-004, R3)
   → where lat/lon/trainId/line parse OK        (skip + count otherwise)
   → group by TrainId, assert single coord      (FR-003/FR-013, R2)
   → List<RailTrain>
   → List<FeedEntity>                            (Entity 3 mapping)
   → concat into bus FeedMessage.Entities        (Worker.cs ExecuteAsync, R7)
   → existing ProcessSpatialReconciliationAsync  (UNCHANGED)
```

On **any** exception in the fetch/parse chain: log `Warning`, return **empty**
`IReadOnlyList<FeedEntity>` (FR-008, best-effort — bus path unaffected).
