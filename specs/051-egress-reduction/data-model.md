# Data Model: Egress Reduction at Current Scale (051)

Four data shapes change or are introduced. No database; stores are the in-memory KV repo (WebAPI), the parquet telemetry contract (worker sidecar), and in-memory client state.

---

## 1. `RouteNearestPointRecord` v2 (MessagePack wire contract — Phase 3)

`src/ChefKnifeStudios.TransitJazz.Shared/Events/RouteNearestPointBatchEvent.cs`. Same `[MessagePackObject]`/`[Key]` layout; keys never renumbered.

| Key | Field | Type (v2) | Semantics |
|---|---|---|---|
| 0 | `VehicleId` | `string` | unchanged |
| 1 | `RouteJoinKey` | `string` | unchanged (client animation/audio key — Principle VI) |
| 2 | `PriorNearestLatE5` | `int?` | Latitude ×10⁵. **`null` on steady-state records** (client uses its retained last position). Non-null on first observation and on route change. |
| 3 | `PriorNearestLonE5` | `int?` | Longitude ×10⁵. Null exactly when Key 2 is null (the pair is atomic — both or neither). |
| 4 | `CurrentNearestLatE5` | `int` | `(int)Math.Round(lat * 100_000)` — exact for the 5-decimal precision already applied in v1. Range ±9,000,000. |
| 5 | `CurrentNearestLonE5` | `int` | Same scaling. Range ±18,000,000. |
| 6 | `DurationMs` | `int` | unchanged. 0 on first observation (client snaps into place). |
| 7 | `SpeedMetersPerSec` | `float?` | unchanged |
| 8 | `Bearing` | `float?` | unchanged |
| 9 | `IsStale` | `bool` | unchanged — MUST keep riding every record, including cached/replayed ones (regression guard). |
| 10 | `Category` | `string?` | **`null` when the client can resolve the category from its route catalog** (the normal case). Non-null ONLY for the `"unknown"` data-quality signal (route-join failure fallback, `Worker.cs ResolveCategory`). No default value. |

**Validation rules** (worker-side, enforced by construction + unit tests):
- Prior pair: both null or both non-null; non-null ⇔ (first observation ∨ `RouteJoinKey` differs from the vehicle's prior state).
- First-observation records keep prior == current and `DurationMs == 0` (existing contract).
- `Category` non-null ⇒ value is `"unknown"`.

**Client decode rules** (JS vehicle store):
- Prior non-null → animate prior→current over `DurationMs` (record wins over retained state).
- Prior null ∧ retained position exists → animate retained→current over `DurationMs`.
- Prior null ∧ no retained position (fresh join / post-eviction reappearance replayed from cache) → place at current instantly.
- `Category` null → resolve from route catalog by `RouteJoinKey`; null ∧ not in catalog → `"unknown"` (see the category-catalog contract below — this is the one rule the as-built client does NOT already satisfy).
- Retained-position store is keyed by `VehicleId`, updated to `Current` on every processed record, entry dropped when the vehicle leaves the rendered set. A vehicle that reappears after eviction therefore has no retained entry and snaps into place (third precedence rule) — it must never animate from a stale origin.

### Category catalog contract (FR-013 / FR-013a — required client change)

The as-built client does not satisfy the null-category decode rule today, and two defaults disagree. Both must be reconciled in Phase 3 (task T044a):

| Location | As-built today | Required for v2 |
|---|---|---|
| `vehicle-animator.js` (~L586) | `category: rec.category \|\| 'unknown'` — per-vehicle only; never consults a catalog | `rec.category` when non-null (the `"unknown"` signal); otherwise catalog lookup by `routeJoinKey`; otherwise `'unknown'` |
| `map-interop.js` (~L588) | `ChefMap._routeCategoryByRouteJoinKey[k] = route.category \|\| 'bus'` — **defaults absent categories to `'bus'`** | MUST NOT default to `'bus'` for vehicle-category resolution. An absent route category resolves to `'unknown'`, preserving the data-quality signal FR-013 exists to protect. |

Notes binding on implementation:

- The catalog (`ChefMap._routeCategoryByRouteJoinKey`) lives on `ChefMap` while decode happens in `ChefMapAnimator`; the animator reaches `ChefMap` as a global today (`vehicle-animator.js:399`), so exposing a lookup is a read, not a new dependency. Expose it as an explicit accessor rather than reaching into the raw object.
- **Startup race (FR-013a)**: the `JoinCity` replay is known to beat route-shape loading (the reason `LateRouteLoadCountsTests` exists). Before the catalog is populated every lookup misses, so category-omitted vehicles resolve to `"unknown"` and MUST be re-resolved when the catalog loads — the same seam `map-interop.js:588` already populates.
- `'bus'` remains a legitimate default for any *other* existing consumer of `_routeCategoryByRouteJoinKey` (e.g. checkpoint coloring at `_checkpointColorFor`); this contract governs vehicle-category resolution only. Do not change checkpoint coloring behavior (FR-016).
- The vehicle-dot paint expressions (`map-interop.js:72/74/167/169`) normalize via `['downcase', ['get', 'category']]` and match lowercase `'rail'`, so they are case-safe. Resolved categories MUST stay lowercase so this keeps holding.

**Size**: ~80 B/vehicle (v1) → ~42–48 B steady-state (v2).

---

## 2. `TelemetryEvent` — new column (parquet contract — Phase 0)

`src/Server/.../Logging/TelemetryEvent.cs`. Property name IS the parquet column name (Parquet.Net 5.6.1 — frozen snake_case contract).

| Column | Type | Rows | Semantics |
|---|---|---|---|
| `batch_wire_bytes` | `long?` | PerCityCycle (summed on FullCycle, like sibling counters) | Exact MessagePack-serialized size of the `List<EventEnvelope>` published for that city that tick. `null` (not 0) when nothing was published (empty batch / publish skipped / unhealthy tick); `0` never occurs. |

**Sync obligation**: `tools/telemetry-mcp/internal/validate/validate.go` kindNumeric allow-list gains `batch_wire_bytes` in the same change (existing hard requirement documented in `TelemetryEvent.cs`).

---

## 3. `IRouteShapeResponseCache` (new WebAPI singleton — Phase 1)

Per-city precomputed HTTP response bodies for the two catalog endpoints.

```
CacheKey   = (Endpoint: AllShapes | AllRoutes, CityKey: string)   // CityKey "*" = no-city-param variant of AllShapes
CacheEntry = { Utf8Json: byte[] (immutable), ETag: string (strong, quoted, hash of Utf8Json), GeneratedUtc: DateTimeOffset }
```

**Lifecycle / state transitions**:
- *Empty* → *Populated(city)*: `GtfsStaticLoader` finishes building a city's shapes (initial load) → serialize aggregate once, swap entry atomically (reference assignment).
- *Populated* → *Repopulated*: 24-hour static refresh completes for a city → same swap. Old `byte[]` is simply unreferenced (no invalidation protocol needed; single writer).
- Readers (endpoints) never mutate; a request racing a swap serves either the old or new entry consistently (entry is immutable).

**Endpoint behavior**: not-ready → 503 (unchanged) · `If-None-Match` == ETag → 304 (no body) · else 200 bytes + `ETag` + `Cache-Control: public, max-age=3600`.

---

## 4. Session Attention State (client in-memory — Phase 2)

Owned by `TransitMap.razor.cs` wiring over `ISignalRNotificationService` + `IPageVisibilityJsInterop` + settings.

```
Inputs:   hidden ∈ {true,false}   (Page Visibility API, current value re-read at each event)
          audioEnabled ∈ {true,false}  (SettingsService snapshot + AudioSettingChangedEventArgs updates)
Derived:  desiredDelivery = !(hidden && !audioEnabled)     // true = should be in the city group
Actual:   joined ∈ {true,false}                            // last confirmed hub group state
```

**Transitions** (reconcile `joined` toward `desiredDelivery`; single in-flight guard serializes hub calls):

| Event | New desired | Action when it differs from `joined` |
|---|---|---|
| tab hidden ∧ audio muted | false | `LeaveCity(city)` → joined=false |
| tab hidden ∧ audio playing | true | none (ambient listening keeps streaming) |
| tab visible | true | `JoinCity(city)` → joined=true; hub replays `LastBatchCache.Current(city)` snapshot |
| mute toggled while hidden → muted | false | `LeaveCity` |
| mute toggled while hidden → unmuted | true | `JoinCity` (+ replay) |
| SignalR reconnect while paused | unchanged | rejoin handler MUST respect `desiredDelivery` (do not blind-rejoin when paused) |

**Invariants**: at most one hub transition in flight; after any burst of toggles, final state matches the last-computed `desiredDelivery` (reconcile-after-completion); pause never disposes the hub connection.

- Dependency direction is one-way: the gate owns `IDeliveryControl` and pushes `DesiredDelivery` into it; the delivery control never references the gate. The reconnect rule is enforced by the control reading its own last-pushed flag.
