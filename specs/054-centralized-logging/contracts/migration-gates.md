# Contract: Migration and Removal Gates

## State transitions

| From | To | Required evidence | Prohibited action |
|---|---|---|---|
| Parquet-only | Dual run | Event/redaction tests, Basic-path proof, Azure route/table/RBAC plan, and pre-change canary | Disable `Logging:Telemetry` or delete legacy evidence. |
| Dual run started | Dual run verified | Seven consecutive days, day-one safe event retained at day seven, input/publish/zero-tone evidence in both paths | Treat configuration alone as retention/correlation proof. |
| Dual run verified | New Parquet writes disabled | All FR-024 checks: routing, plans/retention, redaction, JSON shape, rate limiting, volume/cost, skill, doctor, Grafana correlation, and approval | Remove code/resources in the same unverified step. |
| New writes disabled | Legacy removed | One normal centralized-logs-only release, no-other-consumer audit, reviewed removal change, and feature 053 supersession update | Delete historical blobs/storage without separate archival/deletion approval. |

## Required evidence matrix

For every scenario retain UTC time, city when applicable, deployment revision, expected signal, central KQL/evidence location, legacy evidence location during dual run, outcome, approver, and secret-free notes.

| Scenario | Mandatory proof |
|---|---|
| Safe canary/routing | Pre-change canary and two fresh post-change markers after activation allowance. |
| Field and redaction contract | Identity/correlation/outcome/reason fields complete; zero secret-bearing values. |
| Normal success | No paid informational row solely duplicating metrics. |
| Zero-tone anomaly | One bounded `CityCycleAnomaly` with required reason, city/cycle/counts/publish outcome. |
| Failure coalescing | Ten identical failures produce fewer than ten failure rows and retain initial plus recovery. |
| Investigation | Direct KQL, table/JSON output, `doctor`, and Grafana city/time correlation are read-only and reproducible. |
| Retention and cost | Tables declare 30 days; day-one event is queryable at day seven; ingestion and query scans are reviewed. |
| Legacy removal | Consumer audit and normal new-path-only release; historical blobs remain intact. |

Any missing, stale, failed, or unreviewed evidence is a cutover blocker. These artifacts cannot substitute for constitutional amendments and governance approvals carried from feature 053.
