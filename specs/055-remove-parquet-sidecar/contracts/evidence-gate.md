# Contract: Evidence Gate

**Feature**: 055-remove-parquet-sidecar
**Binds**: FR-018 … FR-022 · SC-010
**Authority**: `docs/observability/centralized-logging-release-checklist.md`

Feature 054 shipped centralized logging while deliberately keeping the Parquet path alive,
and wrote its own removal guard:

> Do not disable `Logging:Telemetry`, delete resources, or remove Parquet consumers while
> any gate is pending.

This feature is precisely the action that guard forbids until its conditions are met. This
contract states those conditions and the state of each as of 2026-08-30.

---

## G0 — Current status: **BLOCKED**

The checklist's own summary line reads:

> Current state: **BLOCKED — release evidence and approvals are not yet supplied.**

**Every** gate row is `PENDING`. Not one dual-run day has been recorded. Implementation of
this feature may proceed; **merging and deploying it may not.**

---

## G1 — Prerequisites (must be `PASS`)

| Prerequisite | Status 2026-08-30 |
|---|---|
| Feature 053 topology/artifact approval | `PENDING` |
| Workspace-scoped `Log Analytics Reader` principal | `PENDING` |
| Existing Grafana metrics path unchanged | `PASS (local)` — controlled evidence `PENDING` |
| Existing Blob/Parquet dual-run path preserved | `PASS (local)` — controlled evidence `PENDING` |

## G2 — FR-024 evidence matrix (all nine must be `PASS`)

JSON shape and v1 fields · Redaction · Volume and coalescing · Routing · Table
plans/retention · Read-only skill · `doctor` · Grafana correlation · Removal guard.

**All nine currently `PENDING`.**

The **Removal guard** row — "Consumer audit, historical blobs preserved" — is the row this
feature directly discharges. Research D1–D10 *is* the consumer audit; the "historical blobs
preserved" clause is what G5 converts into an explicit decision.

## G3 — Routing canaries (all three must be `PASS`)

Safe pre-change canary · Fresh post-change marker 1 · Fresh post-change marker 2.
**All `PENDING`.** Allow up to 90 minutes for diagnostic activation plus ingestion time;
an immediate empty query is not proof of route failure.

## G4 — Dual-run record: seven consecutive days

All seven rows must pass, each with an approver:

Day 1 safe event · Input failure · Publish failure · Zero-tone anomaly · Repeated
failure/recovery · Redaction · Day 7 retention/cost/query.

**All `PENDING`.** This is the longest-lead item: it cannot be compressed, because it
measures behavior over a seven-day window.

## G5 — FR-020 historical-data decision — **RESOLVED: DISCARD**

Before storage deletion, the release owner MUST record an explicit decision to discard or
first export the historical telemetry data, with approver and date.

**Decision (2026-08-30, release owner)**: **DISCARD.** All historical telemetry data,
including the `batch_wire_bytes` series, is discarded with the storage account. No export
is performed.

**What this forgoes**: `batch_wire_bytes` was the *only* record of the feature 051 Phase 3
egress baseline (research D5). Discarding it means 051 Phase 3, if ever revived, must
re-establish its baseline from new measurements rather than from history. The release owner
accepted this explicitly.

**Effect on execution**: no export step is required before storage deletion. `WireSize.cs`
and its unit tests are still retained (they are live measurement code, not history), so a
future baseline can be re-gathered through the metrics path if needed.

## G6 — Authorization record

Before merge, append to the release checklist:

```
## 055 removal authorization
Evidence window: <first day> .. <seventh day> (7 consecutive days, all rows PASS)
Historical data decision: <DISCARD | EXPORTED to …>  (FR-020)
Authorized by: <approver>   Date: <UTC date>
```

## G7 — Post-removal record (FR-022)

After removal completes, the checklist MUST no longer describe the Parquet path as an
active dual run. The "Dual-run record" section becomes a completed historical record, and
the "Current state" line reflects the retired state.

---

## Sequencing rule

```
G1..G4 all PASS ──▶ G6 authorization recorded ──▶ code removal ships (Slices A+B)
                                                          │
                                              post-deploy verification passes
                                                          │
                                          G5 decision recorded ──▶ infra removal (Slice C)
                                                          │
                                                    G7 record updated
```

**No step may be skipped or reordered.** In particular, G5 gates only the *infrastructure*
step: code removal does not destroy data, so it does not require the discard decision —
only storage deletion does.

---

## What an implementer may do while BLOCKED

Permitted: write the code changes, run local builds and tests, prepare the branch, open a
draft PR, **and rotate the `randomstoragehenry` account key** — that account sits outside
the dual-run path entirely, so rotating it neither disables `Logging:Telemetry` nor deletes
any resource this gate protects. (Done 2026-08-30; see research D8.)

**Not permitted**: merging to `main` or `deploy/marta-jazz`; disabling
`Logging:Telemetry` in any deployed environment; deleting any Azure resource; marking any
checklist row `PASS` without the controlled evidence behind it.

---

## Known environment constraint

The checklist already records `Bicep build / ARM regeneration` as **`BLOCKED`** — the Azure
CLI could not download the Bicep compiler in the restricted workspace, and `bicep/main.json`
was deliberately not hand-edited.

**Implication for Slice C**: regenerating `bicep/main.json` (removal-surface S10) will hit
the same constraint in the same environment. Plan for the Bicep edit to be completed where
the compiler is available, and do not hand-edit the generated ARM JSON as a workaround.
