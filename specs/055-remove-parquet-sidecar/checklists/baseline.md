# Pre-Removal Verification Baseline

**Feature**: 055-remove-parquet-sidecar
**Captured**: 2026-08-30
**Branch**: `054-centralized-logging`
**Purpose**: Make the FR-005 / FR-007 "unchanged" claims provable. Any deviation observed
after this point is attributable to feature 055.

---

## T001 — Full-solution build and test baseline

Command: `dotnet test src/ChefKnifeStudios.TransitJazz.sln`
Result: **all four test projects green, zero failures, zero skipped.**

| Test project | Passed | Failed | Skipped | Total |
|---|---|---|---|---|
| `ChefKnifeStudios.TransitJazz.Shared.Tests` | 33 | 0 | 0 | 33 |
| `ChefKnifeStudios.TransitJazz.Client.Shared.Tests` | 42 | 0 | 0 | 42 |
| `ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests` | 121 | 0 | 0 | 121 |
| `ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests` | 129 | 0 | 0 | 129 |
| **Total** | **325** | **0** | **0** | **325** |

Build: succeeded. Warnings present are pre-existing `CS8632` nullable-annotation-context
warnings across the WebAPI project, unrelated to this feature.

**Post-removal expectation**: the worker suite drops by the tests deleted in T024
(six files), T014f (two methods), T026 (one method), and T026a (one method). Every other
count must hold. No project may report a failure.

---

## T002 — Grafana alert references to the three sidecar metrics

File searched: `observability/grafana/alerts/transitjazz-worker-alerts.json`
Pattern: `log_buffer_occupancy|log_dropped_records|log_persist_failures`

Result: **0 matches.** Confirms the contract C4 precondition — retiring the three sidecar
self-health instruments breaks no alert rule. Re-verify immediately before merge.

---

## T003 — Structured-log event names (contract C1 comparison set)

Source: `src/Server/…Worker/Logging/StructuredLogEvent.cs`, enum `StructuredLogEventName`.
Every name below MUST still emit, unchanged in name, level, fields, and emission
conditions, after removal.

1. `WorkerStarted`
2. `WorkerStopped`
3. `CityInputFailed`
4. `CityInputPartial`
5. `CityInputEmpty`
6. `RouteIndexUnavailable`
7. `CityCycleAnomaly`
8. `PublishFailed`
9. `PublishRecovered`
10. `WorkerCycleFailed`
11. `WorkerCycleRecovered`

**Note — contract C1 amendment**: contract `retained-observability.md` C1 enumerates ten
names and omits `WorkerCycleRecovered`, which is present in the enum as shipped. The
working tree is authoritative; all **eleven** names are the comparison set. This is a
transcription gap in the contract, not a code defect, and it widens rather than narrows the
retention obligation.

Supporting enums also unchanged by this feature: `StructuredLogOutcome` (3 values),
`StructuredLogReasonCode` (13 values), and the 21 bounded fields on `StructuredLogEvent` /
`StructuredLogDiagnosticContext` — including `BatchWireBytes`, which is the surviving
`WireSize` measurement surface after the parquet `batch_wire_bytes` column retires.
