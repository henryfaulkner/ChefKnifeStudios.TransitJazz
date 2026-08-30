# Contract: Retained Observability

**Feature**: 055-remove-parquet-sidecar
**Binds**: FR-005, FR-006, FR-007 · SC-003, SC-004

The load-bearing guarantee of this feature is that removing the Parquet path costs the
project **no diagnostic capability it still uses**. This contract states precisely what
must still work afterward, and the one narrow class of signal that is allowed to disappear.

---

## C1 — Structured log events: unchanged, byte for byte

Every event emitted by the 054 path MUST survive with identical name, level, fields, and
emission conditions. The removal MUST NOT alter coalescing, redaction, reminders, or
recovery behavior.

**Event names that MUST still emit** (from `StructuredLogEventName`):

`CityInputFailed`, `CityInputPartial`, `CityInputEmpty`, `RouteIndexUnavailable`,
`CityCycleAnomaly`, `PublishFailed`, `PublishRecovered`, `WorkerCycleFailed`,
`WorkerStarted`, `WorkerStopped`.

**Verification**: the full `StructuredLogging*` and `WorkerStructuredEvent*` test suites
pass **without modification**, with exactly two named exceptions:

| File | Method | Why it is exempt |
|---|---|---|
| `StructuredLoggingVolumeTests.cs` | `LegacyTelemetryConfigurationAndWorkerMetricPathRemainPresent` | A 054 dual-run guard asserting the Parquet path still exists (source-text + JSON asserts) |
| `StructuredLoggingCityCoverageTests.cs` | `TelemetryRemainsConfiguredIndependentlyOfStructuredEvents` | Same — asserts `EmitsTelemetry` and `Logging:Telemetry:Enabled` are still present |

Both exist *specifically* to prove the legacy path was preserved during the dual run. This
feature is the authorized end of that dual run, so deleting them is the intended outcome,
not evidence of over-cutting. Neither is compiler-checked; both fail only at run time.

**Any edit required to any other `StructuredLogging*` or `WorkerStructuredEvent*` test IS
the stop-and-reassess signal.**

**Rationale**: `StructuredEventEmitter` writes through `ILogger` directly and holds no
reference to the sidecar (research D1/D2). A change in its behavior would mean an
unintended coupling was severed.

---

## C2 — Anomaly classification: unchanged

`CityAnomalyClassifier.Classify(CityCycleOutcome)` MUST keep producing the same
`StructuredLogReasonCode` for the same input. `CityCycleOutcome`'s shape is unchanged.

**Verification**: `CityAnomalyClassifierTests.cs` and `CityCycleOutcomeTests.cs` pass
unmodified.

---

## C3 — Retained metrics: unchanged

Every OpenTelemetry instrument in `Metrics/` other than the three named in C4 MUST keep
emitting with the same name, type, unit, and dimensions — including cycle duration,
allocated bytes, vehicles processed, tones emitted, feed freshness, and all cache-size
gauges.

**Verification**: every Grafana panel other than the two named in C4 continues to populate
after deployment, with no gap attributable to the change.

---

## C4 — The permitted disappearance: sidecar self-health only

Exactly three instruments and two panels MAY be removed, because their measurement subject
ceases to exist:

| Instrument | Panel |
|---|---|
| `transitjazz.worker.log_buffer_occupancy` | "Sidecar queue occupancy" |
| `transitjazz.worker.log_dropped_records` | "Sidecar failure rate" |
| `transitjazz.worker.log_persist_failures` | "Sidecar failure rate" |

**Constraint**: they MUST be removed, not zeroed. Leaving them reporting a constant `0`
would present a retired component as a healthy one.

**Precondition, verified**: `observability/grafana/alerts/transitjazz-worker-alerts.json`
contains zero references to these three metrics. No alert rule may break. Re-verify before
merging — if an alert has since been added against them, it must be removed in the same
change.

**Out of bounds**: no other instrument, panel, dimension, or alert may be touched. This
contract does not authorize dashboard tidying.

---

## C5 — Worker processing behavior: unchanged

Removal MUST NOT alter cycle cadence, per-city iteration, feed fetching, snap/lerp passes,
crossing detection, batch composition, or SignalR publication.

**Specifically preserved**: `WireSize.Measure` at the publish site stays. Only its parquet
*column* retires.

**Verification**: cycle-duration and vehicles-processed metrics stay within normal
historical variation across a full post-deploy cycle window; `CrossingDetectorTests`,
`SubwaySynthesisTests`, and the city-isolation suites pass unmodified.

`CityLoopTests` passes unmodified **except** its two `EmitsTelemetry` gate methods
(`ITransitCity_EmitsTelemetry_IsConfigurablePerCity`,
`Loop_TelemetryGate_BranchesOnlyOnEmitsTelemetry`) and the stub `EmitsTelemetry` members,
removed by T014f. Those assert feature 031's INV-3 telemetry gate, whose subject this
feature removes. INV-1 ("the loop never branches on a city name") survives on the file's
other assertions and MUST continue to hold.

---

## C6 — Investigation capability: preserved

An investigator MUST be able to reproduce, through the centralized log workflow alone,
every diagnosis that previously required the telemetry store.

**Verification**: reproduce one known anomaly per the `transitjazz-logs` skill and confirm
the same evidence — city, cycle identifier, reason code, counts, publish outcome — is
recoverable.

**Note**: this is the substantive claim the 054 evidence gate exists to prove. This
contract does not re-litigate it; it requires that the gate's passing record be the
evidence (see `evidence-gate.md`).

---

## Acceptance summary

| # | Guarantee | Verified by |
|---|---|---|
| C1 | Structured events identical | 054 test suites pass unmodified |
| C2 | Anomaly classification identical | Classifier tests pass unmodified |
| C3 | All non-sidecar metrics intact | Panels populate post-deploy |
| C4 | Only 3 instruments / 2 panels removed; zero alerts affected | Alert file re-verified clean |
| C5 | Processing behavior unchanged | Processing test suites pass (CityLoopTests less its 2 gate methods); cadence within variation |
| C6 | Investigation capability preserved | Anomaly reproduced via centralized logs |
