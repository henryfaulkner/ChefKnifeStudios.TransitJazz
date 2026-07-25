# Phase 1 Data Model: NYC MTA Bus Support

This feature adds no new persisted storage and no new SignalR event type. The "entities" are configuration shapes and one in-memory transform. All existing models (`FeedMessage`, `RouteShapeFeature`, `RouteNearestPointBatchEvent`, the Worker's `_routeIndex`) are unchanged.

---

## 1. `CityConfig` (modified)

Existing class at `TransitDataWorker/Cities/CityConfig.cs`. **One new field.**

| Field | Type | New? | Meaning |
|-------|------|------|---------|
| `Name` | `string` | existing | City key (`"nymta-bus"`) |
| `GtfsRtUrls` | `string[]` | existing | RT feed URL(s) — one citywide obanyc URL here |
| `StaticZipUrls` | `string[]` | existing | 6 zips (5 NYCT boroughs + Bus Co) |
| `ApiKeyEnvVar` | `string?` | existing | Env var name for the key (see R4 for the `?key=` decision) |
| `RailRouteIdMap` | `Dictionary<string,string>?` | existing | Unused by `nymta-bus` (null) |
| `EmitsTelemetry` | `bool` | existing | `true` for `nymta-bus` |
| **`RouteIdNormalization`** | **`string[]`** | **NEW** | **Ordered list of named transform steps; default `[]`** |

**Validation / invariants**:
- `RouteIdNormalization` defaults to `[]` (empty) → for every existing city, `Apply` is a passthrough, so no behavior change.
- Unknown step names are tolerated (no-op), so a config typo cannot crash a tick.
- Order is significant and preserved as-authored in the config array.

**Optional NEW field (R4 fallback only)**:

| Field | Type | Default | Meaning |
|-------|------|---------|---------|
| `ApiKeyQueryParam` | `string` | `"api_key"` | Query-param name for the credential; set `"key"` for obanyc. Only added if committed-config `${VAR}` substitution proves unavailable. |

---

## 2. `RouteIdNormalizer` (new — pure transform, not persisted)

New static class at `TransitDataWorker/Cities/RouteIdNormalizer.cs`.

```
Apply(routeId: string, steps: IReadOnlyList<string>) -> string
    folds each step over routeId in order; returns transformed routeId
```

**Named steps (v1)**:

| Step name | Transform | Example |
|-----------|-----------|---------|
| `uppercase` | `ToUpperInvariant()` | `"bx3"` → `"BX3"` |
| `plusToSbs` | trailing `+` → `-SBS` suffix | `"M15+"` → `"M15-SBS"` |
| `stripLeadingZeros` | `^([A-Z]+)0*(\d.*)$` → group1+group2 | `"Q06"` → `"Q6"` |
| *(any other)* | no-op passthrough | `"X"` (unknown) → `"X"` |

**Invariants**:
- Total function: never throws for any string input or any step-name input.
- Empty step list ⇒ identity (`Apply(x, []) == x`).
- No letter prefix or no digits after prefix ⇒ `stripLeadingZeros` leaves input unchanged (`"S"`, `"SBS"`).
- Pure: no I/O, no state, deterministic — same input always yields same output.

---

## 3. `CityNames` (modified)

Existing static class at `Shared/CityNames.cs`. **One new constant.**

```
public const string NymtaBus = "nymta-bus";
```

Used by config-name matching and (indirectly, via the hash) the client picker.

---

## 4. Data flow (unchanged pipeline, one new step)

```
obanyc RT protobuf
   → GtfsRtCity.FetchVehiclesAsync
        → merge entities
        → ApplyRailRouteIdMap(merged)          [existing, no-op for nymta-bus]
        → ApplyRouteIdNormalization(merged)    [NEW: RouteIdNormalizer.Apply per Trip.RouteId]
   → FeedMessage returned to Worker.cs
        → V1/V2 passes match Trip.RouteId against _routeIndex[city][RouteJoinKey]  [unchanged]
        → RouteNearestPointBatchEvent published via SignalR                        [unchanged]
   → Client TransitMap (subscribed to #nymta-bus SignalR group) renders            [unchanged]
```

The normalization step is the **only** new node; everything downstream is the existing, city-agnostic pipeline.
