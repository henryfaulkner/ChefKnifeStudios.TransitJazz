# Evidence Gate Status (T007)

**Feature**: 055-remove-parquet-sidecar
**First read**: 2026-08-30, from `docs/observability/centralized-logging-release-checklist.md`
**Resolved**: 2026-08-30 — **WAIVED by the release owner**
**Authority**: contract `contracts/evidence-gate.md`

## Verdict: **WAIVED — removal proceeded without the gate being satisfied**

The release owner directed that the seven-day dual-run window is not required, and deleted
the Azure telemetry infrastructure manually. Removal is authorized on that decision, **not**
on the evidence the gate asked for.

This is a deliberate, recorded trade, not an oversight. The distinction matters for anyone
reading this later: **no row below was ever marked `PASS` on evidence.** They were set aside.

---

## Gate rows as they stood when waived (2026-08-30)

| Gate | Required | Actual | Disposition |
|---|---|---|---|
| G1 Prerequisites | 4 rows `PASS` | 2 `PENDING`; 2 `PASS (local)` with controlled evidence `PENDING` | **Waived** |
| G2 FR-024 matrix | 9 rows `PASS` | 0 of 9 | **Waived** |
| G3 Routing canaries | 3 rows `PASS` | 0 of 3 | **Waived** |
| G4 Dual-run record | 7 consecutive days | **0 of 7 days recorded** | **Waived** |
| G5 FR-020 historical data | Explicit decision | **DISCARD** (release owner) | **Satisfied** |
| G6 Authorization record | Appended to checklist | Recorded 2026-08-30 | **Satisfied** |
| G7 Post-removal record | Checklist reflects retired state | Updated 2026-08-30 (T060) | **Satisfied** |

G2's **Removal guard** row — "consumer audit, historical blobs preserved" — is the one row
this feature substantively discharged rather than skipped. Research D1–D10 plus
implementation-time verification is the consumer audit; it is recorded in
`docs/observability/centralized-logging-removal-audit.md`. Its "historical blobs preserved"
clause was superseded by the G5 DISCARD decision.

## What the waiver costs

The gate existed to prove contract **C6** — that an investigator can reproduce, through
centralized logs alone, every diagnosis that previously required the telemetry store. **That
proof was not produced.** The data is now gone and the code paths are deleted, so there is no
fallback if a future diagnosis needs a signal only Parquet carried.

What *was* verified, locally and independently of the gate:

- **C1** — all eleven `StructuredLogEventName` values intact; the 054 suites pass unmodified
  apart from the three named dual-run guards, which assert the *legacy* path's survival and
  whose removal is this feature's intended outcome.
- **C2** — anomaly classification tests pass unmodified.
- **C3/C4** — only the three sidecar self-health instruments and their two panels were
  removed; zero alert rules reference them (re-verified).
- **C5** — processing behavior untouched; `WireSize.Measure` retained; 302 of 302 tests pass.

So C1–C5 rest on evidence; **C6 rests on assertion.**

## Infrastructure: deleted manually

The Azure telemetry storage account, its container, and its role assignment were removed by
hand on 2026-08-30, outside the Bicep deployment. The IaC in this feature was therefore
written to **match** an already-deleted state rather than to drive the deletion.

Consequences to carry forward:

1. **T058/T058a were not performed.** No post-deploy cycle window was observed, and the
   absence of the `Storage Blob Data Contributor` role assignment on `serverIdentity` was
   never independently confirmed. Absence of errors is not proof of absence of permission —
   and here, not even absence of errors was measured.
2. **The next infrastructure deployment is the reconciliation point.** If any telemetry
   resource or role assignment survived the manual deletion, that deployment is where it
   surfaces.
3. **No blob inventory was taken** before deletion, so the discarded contents are not
   enumerated anywhere.

## Environment constraint that did *not* materialize

The checklist recorded `Bicep build / ARM regeneration` as `BLOCKED` (Azure CLI could not
download the compiler in the restricted workspace). **That constraint does not apply here** —
Bicep CLI 0.43.8 is available in this workspace. `bicep/main.json` was regenerated from
source with `az bicep build` and never hand-edited. A semantic diff against a baseline build
of the unmodified `main.bicep` confirms the delta attributable to feature 055 is exactly:

- 1 module removed (`telemetry-storage-deploy`)
- 1 parameter removed (`enableLegacyTelemetry`)
- 2 outputs removed (`telemetryStorageAccountName`, `telemetryBlobServiceUri`)

Nothing else. The remainder of the `main.json` diff is the committed artifact catching up to
feature 054's already-merged Bicep changes, which had left it stale.
