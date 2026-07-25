# Contract: Wire & Shape Data Format

The data-format contracts that cross project boundaries. **All three legs change together in one coordinated deploy** (Principle V / D14) — there is no dual-field transition.

## 1. SignalR MessagePack — `RouteNearestPointRecord`

The high-frequency per-tick vehicle batch. Only `Key(10)` changes; keys 0–9 are frozen.

| Key | Field | Type before | Type after | Notes |
|----|-------|-------------|------------|-------|
| 0 | VehicleId | string | string | unchanged |
| 1 | RouteJoinKey | string | string | unchanged |
| 2 | PriorNearestLat | double | double | unchanged |
| 3 | PriorNearestLon | double | double | unchanged |
| 4 | CurrentNearestLat | double | double | unchanged |
| 5 | CurrentNearestLon | double | double | unchanged |
| 6 | DurationMs | int | int | unchanged |
| 7 | SpeedMetersPerSec | float? | float? | unchanged |
| 8 | Bearing | float? | float? | unchanged |
| 9 | IsStale | bool | bool | unchanged |
| **10** | **TransitMode → Category** | `TransitMode` enum (packed int, 1 byte) | **`string`** (`"bus"`/`"rail"`/`"streetcar"`/`"unknown"`, ~5–10 bytes) | **BREAKING.** Default `"bus"`. |

- `enum TransitMode` is **removed** from `Shared`.
- **Compatibility:** none. A pre-change client reading a post-change stream (or vice versa) mis-deserializes `Key(10)` (int vs string). Deploy server (WebAPI+Worker) and client atomically.
- **Test that MUST update in lockstep:** `EventEnvelopeMessagePackTests` — the round-trip constructs `RouteNearestPointRecord` with `TransitMode.Rail` as the 11th positional arg; it becomes a `string` category, or the assertion silently passes against corrupt data.

**Round-trip acceptance vectors:**
| Input `Category` | Expect after MessagePack round-trip |
|---|---|
| `"streetcar"` | `"streetcar"` (string preserved) |
| `"rail"` | `"rail"` |
| default (omitted) | `"bus"` |
| `"unknown"` | `"unknown"` |

## 2. Shape catalog — `RouteShapeProperties` (fetched once at startup by Worker & Client)

| Field | Type before | Type after | Notes |
|-------|-------------|------------|-------|
| RouteId | string | string | unchanged (true GTFS static id) |
| RouteShortName | string? | string? | unchanged |
| Color | string? | string? | unchanged |
| TextColor | string? | string? | unchanged |
| **Mode → Category** | `TransitMode = Bus` | **`string = "bus"`** | renamed + retyped |
| **RouteType** | — | **`int = 3`** NEW | raw GTFS `route_type`; drives client display order (D8). Inserted **before** `City`. |
| City | string? = null | string? = null | unchanged, stays optional-last |
| JoinKey (computed) | `RouteShortName ?? RouteId` | *unchanged* | Principle VI — do not touch |

## 3. GeoJSON leg (hand-serialized by `BuildLineStringFeature`, read by client map JS)

- Serialize: `"mode":"{mode}"` → `"category":{JsonSerializer.Serialize(category)}` **and** add `"routeType":{routeType}`.
- The GeoJSON **property name** consumed by the client JS renames `transitMode` → `category` at all read sites; `vehicle-animator.js` write sites rename to match; the `rec.transitMode || 'bus'` fallback becomes `rec.category || 'unknown'` (D6).
- Deserialize leg: once `Category` is a `string`, `JsonStringEnumConverter` is no longer load-bearing **for this type**. Do **not** blindly delete it — grep for any other enum round-tripping through `JsonOptions.Get()`; remove only if none, else drop just the `Mode`-specific comment.

**Route-classification acceptance vectors** (WebAPI `ClassifyCategory`):
| City config | `route_type` | Expect `Category` | Expect `RouteType` |
|---|---|---|---|
| none (MARTA/WMATA/MBTA/NYMTA) | `0` / `1` / `2` | `"rail"` | 0 / 1 / 2 |
| none | `3` (or other) | `"bus"` | 3 |
| TTC `{0:streetcar,1:rail,3:bus}` | `0` | `"streetcar"` | 0 |
| TTC | `1` | `"rail"` | 1 |
| TTC | `3` | `"bus"` | 3 |
| TTC (configured) | `4` (unmapped) | `"bus"` + **warning log** | 4 |
