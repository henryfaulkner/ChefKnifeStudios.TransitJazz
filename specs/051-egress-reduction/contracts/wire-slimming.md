# Contract: Wire Slimming — `RouteNearestPointRecord` v2 + Version Gate (Phase 3)

Binds spec FR-010 … FR-016 / US4. ONE coordinated MessagePack contract revision across worker → hub → WASM client, landing on `main` AND `deploy/marta-jazz`. MUST NOT ship until Phase 0 has recorded a multi-day `batch_wire_bytes` baseline.

## C1. Record v2 field contract

Authoritative field table in [data-model.md §1](../data-model.md). Summary of the three changes (all in `RouteNearestPointBatchEvent.RouteNearestPointRecord`, keys never renumbered):

1. **Coords → scaled int**: Keys 4/5 `int` = degrees ×10⁵ (exact for the 5-decimal rounding v1 already applied; ~1.1 m). Worker multiplies; client divides by `1e5` once at decode.
2. **Prior pair → nullable, usually omitted**: Keys 2/3 `int?`, atomic pair. Non-null ⇔ first observation ∨ route change. First-observation records keep prior==current + `DurationMs 0`.
3. **Category → nullable, unknown-only**: Key 10 `string?`, non-null ⇔ `"unknown"` (data-quality signal preserved; never silently reclassified). Client resolves null from its route catalog.

## C2. Worker emit rules (accept vectors)

| Vehicle state at tick | Emitted record |
|---|---|
| First observation | prior = current (non-null), `DurationMs 0`, category per rule 3 |
| Seen before, same route | prior pair **null**, current = new snap, real `DurationMs` |
| Seen before, `RouteJoinKey` changed | prior pair **non-null** (self-contained; client must not tween across routes) |
| Route-join failure fallback | `Category = "unknown"` (non-null) — all other vehicles `Category = null` |
| Stale (same GPS fix) | exactly as today except encoding: `IsStale true`, record still emitted, still upserted to cache |

## C3. Client decode rules

Precedence: record prior (if non-null) → retained last position (JS vehicle store, keyed `VehicleId`) → snap into place. Divide-by-1e5 at the single decode seam (`TransitMap.razor.cs` payload builder, ~L496-519). Rendering, animation timing, staleness handling, audio triggering otherwise byte-identical in behavior (spec FR-016).

**Category resolution (FR-013 / FR-013a) — the one client behavior that must actually change.** Full contract in [data-model.md §1 "Category catalog contract"](../data-model.md). Summary:

| Order | Rule |
|---|---|
| 1 | Record `Category` non-null → use it verbatim (this is the `"unknown"` data-quality signal). |
| 2 | Null → look up the route catalog by `RouteJoinKey` (`ChefMap._routeCategoryByRouteJoinKey`). |
| 3 | Null ∧ catalog miss (incl. catalog not yet loaded) → `"unknown"`. **Never `"bus"`.** |
| 4 | Vehicles resolved `"unknown"` only because the catalog had not loaded MUST be re-resolved when it does (the known `JoinCity`-replay-beats-shape-load race). |

`map-interop.js:588`'s existing `|| 'bus'` default MUST NOT be the source for vehicle-category resolution — it would fabricate `"bus"` for exactly the routes FR-013 wants flagged. Other consumers of that map (checkpoint coloring) keep their current behavior unchanged.

## C4. `LastBatchCache` / join-replay invariants (regression guards)

- Cache code is UNCHANGED: upsert-per-vehicle including stale records, `EvictAfterCycles = 3`, crossing age cap — all untouched.
- Replayed null-prior records are correct for joiners by construction (no retained state → snap into place). The `ILastBatchCache.cs` "synthetically all moving" regression MUST stay fixed: `IsStale` rides every cached record; `LastBatchCacheCrossingExclusionTests` and sibling tests must pass unmodified.
- Long-stationary vehicles keep appearing in join snapshots (eviction is presence-based, not motion-based — unchanged).

## C5. Version gate & deployment

- `HubMethods.JoinCity` value changes `"JoinCity"` → `"JoinCityV2"` in the SAME commit as the record change. Server `TransitHub` method name and client join + reconnect-rejoin call sites move together. `LeaveCity` (Phase 2) is NOT renamed — group membership only matters post-join.
- **Failure semantics (FR-015)**: stale WASM client (old bundle) → its `JoinCity` invoke faults with `HubException` (unknown method) → logged error, no group membership, no data, empty map until reload. It MUST NOT receive v2 batches (would silently mis-decode: MessagePack `ReadDouble` accepts int encodings). New client vs old server fails symmetrically at join.
- **Order**: server+worker container (atomic, same image) deploys first; SWA client immediately after; `deploy/marta-jazz` gets the identical revision. NYMTA's 5 MB `MaximumReceiveMessageSize` ceiling stays (headroom, not a target).

## C6. Round-trip / size vectors (Shared.Tests)

| Vector | Expectation |
|---|---|
| Serialize→deserialize v2 record, all fields populated | lossless round-trip |
| Steady-state record (null prior, null category) | round-trips; encoded size ≤ 55% of an equivalent v1 record |
| lat 89.99999 / lon −179.99999 | scaled values fit `int`; decode returns exact 5-decimal values |
| lat scaling of already-rounded v1 values | zero precision loss vs. `Math.Round(x,5)` |
| Batch of 1,000 steady-state records | total encoded bytes ≥40% smaller than v1 equivalent (SC-004 proxy) |
