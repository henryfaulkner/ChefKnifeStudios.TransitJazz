# Contract: Telemetry Schema Extension (US2 / D3 / FR-006, FR-016)

Adds four suppression-count columns to the frozen `PerCityCycle` telemetry contract. This is a **paired change** across three artifacts that MUST stay consistent, or telemetry queries reject the new columns.

## New columns

All `int?`, PerCityCycle-only (null on FullCycle rows), summed on FullCycle.

| Column (snake_case — the frozen wire name) | Kind | Reason it counts |
|---|---|---|
| `crossings_suppressed_first_seen` | numeric | baseline null (first-seen / re-seen after prune) |
| `crossings_suppressed_delta_leq0` | numeric | no forward progress (`delta == 0` post-D4; `<= 0` pre-D4) |
| `crossings_suppressed_teleport` | numeric | along-distance jump > 2000 m (reset) |
| `crossings_suppressed_transfer` | numeric | `RouteJoinKey` changed (reset) |

## Artifacts that MUST change together

1. **`src/Server/.../TransitDataWorker/Logging/TelemetryEvent.cs`** — add the four `int?` properties (snake_case, under the "Shared" region, following `tones_emitted`/`vehicles_processed` conventions).
2. **`tools/telemetry-mcp/internal/validate/validate.go`** — add all four to the `kindNumeric` allow-list map (~line 55, next to `tones_emitted`, `vehicles_processed`). Without this, `query_telemetry` rejects any filter/projection naming the columns.
3. **`tools/telemetry-mcp/internal/validate/validate_test.go`** — add an accept vector for each new column and (optionally) a reject vector for a near-miss name.
4. **`src/Server/.../TransitDataWorker.Tests/TelemetryEventSchemaTests.cs`** — assert the four columns exist and round-trip through `ParquetSerializer` as nullable ints.

## Round-trip contract

- A PerCityCycle row written by the sidecar and read back via Parquet.Net MUST preserve the four columns as nullable ints (null when not applicable is legal, but the Worker SHOULD always set them for PerCityCycle rows).
- A FullCycle row MUST carry the summed values (like the other per-cycle ints) or null; the sum path in `Worker.cs` (the tick-wide accumulation ~:121) MUST include them.

## Invariant (verifiable — SC-007)

For any PerCityCycle row:

```
crossings_suppressed_first_seen
+ crossings_suppressed_delta_leq0
+ crossings_suppressed_teleport
+ crossings_suppressed_transfer
+ (# vehicles that emitted ≥ 1 crossing this cycle)
== (# vehicles that ran CrossingDetector.Detect this cycle)
```

A non-zero unexplained remainder means a suppression path is uncounted → the attribution is incomplete.

## Non-goals

- No column is removed or renamed (would break the frozen contract).
- No change to `event_type` discrimination or the FullCycle aggregation shape beyond adding these four to the summed set.
