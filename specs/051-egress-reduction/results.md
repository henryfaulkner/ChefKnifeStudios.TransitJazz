# Results: Egress Reduction at Current Scale (051)

Measured evidence for the SC-00x success criteria. Appended to as phases land.

---

## Phase 0 baseline — `batch_wire_bytes` (T013)

**Captured**: 2026-08-01, from production telemetry via the `telemetry-query-bridge` MCP tool.
**Window**: ≥5 days of data confirmed present (spot-checked 2026-07-27, 07-29, 07-31). The
≥3-day gate on Phase 6 (US4) is **satisfied**.

**Important caveat on the numbers below**: the MCP query bridge returns raw rows and caps
results at ~100 rows per query — it has no aggregate (`SUM`/`GROUP BY`) capability. The
figures are therefore **per-tick averages and ratios over a ~100-tick sample per day**, not
full-day totals. Ratios (bytes/vehicle) are the reliable output and are what the decision
rests on; absolute daily sums are **not** established here.

### Per-city sample (2026-07-31, PerCityCycle, published ticks only)

| City  | Ticks | Avg bytes/tick | Vehicles | **Bytes/vehicle** |
|-------|------:|---------------:|---------:|------------------:|
| nymta |    17 |        194,450 |   48,513 |          **68.1** |
| ttc   |    16 |         68,838 |   16,730 |          **65.8** |
| wmata |    14 |         46,531 |   10,223 |          **63.7** |
| rtd   |    14 |         45,902 |    6,777 |          **94.8** |
| mbta  |    17 |         30,648 |    8,145 |          **64.0** |
| septa |    12 |         22,752 |    4,579 |          **59.6** |
| marta |    10 |         12,704 |    2,033 |          **62.5** |

### Cross-day stability (FullCycle, all 7 cities)

| Date       | Ticks | Avg bytes/tick | Bytes/vehicle |
|------------|------:|---------------:|--------------:|
| 2026-07-27 |    97 |        284,813 |          67.9 |
| 2026-07-31 |    97 |        415,023 |          68.2 |
| 2026-07-29 (nymta only) | 100 | 187,671 |     68.2 |

**The per-vehicle cost is stable at ~68 B/vehicle across days and across cities.** Per-tick
totals vary with fleet size in service (time of day), which is expected.

### NYMTA's share (source doc open question 2)

NYMTA is the dominant contributor — ~194 KB/tick vs. ~12.7 KB for MARTA, roughly **47% of
sampled total wire bytes** and the largest single lever. Its per-vehicle cost (68.1 B) is
*ordinary*; its volume comes from fleet count, not from unusually fat records. So NYMTA is
not a special case to optimize separately — a per-record win applies to it proportionally.

RTD is the per-vehicle outlier at 94.8 B/vehicle (longer route-join keys / vehicle IDs), but
its absolute volume is small.

---

## SC-004 go/no-go analysis for US4 (wire slimming)

The v2 wire revision was sized empirically with a throwaway MessagePack probe (1,000
records, realistic NYC coordinates, 5-decimal rounding as v1 already applies; the probe was
deleted after measurement — it was a decision input, not a shipped test).

| Variant | Per-record | Note |
|---|---:|---|
| v1 (as-built shape) | **69.8 B** | matches production's ~68 B/vehicle — probe is realistic |
| v2 (steady state: prior pair omitted, category omitted, coords as scaled int) | **42.8 B** | |
| **Reduction** | **38.7%** | data-model §1 estimated ~42–48 B — **confirmed accurate** |

The probe's v1 figure landing within ~2 B of the independently-measured production
`batch_wire_bytes`/vehicle ratio is the key validation: the model of the wire is correct.

### Record-mix check

The 38.7% assumes steady-state records dominate (prior pair omitted). Verified against
telemetry: NYMTA `crossings_suppressed_first_seen` = 112 over 275,019 vehicles processed =
**0.04%** first-observation records. Route-change records are likewise rare. Steady-state is
effectively the whole population, so 38.7% is the realistic blended figure, not a best case.

### Verdict: **38.7% measured vs. SC-004's original ≥40% threshold — a narrow miss.**

This is a real finding, not a rounding artifact: the shortfall is consistent and the
remaining bytes are dominated by the two strings (`VehicleId`, `RouteJoinKey`), which v2
does not touch. No amount of coordinate/prior/category slimming reaches 40% while those
strings stay full-width.

### Decision (2026-08-01, product owner): **SHIP US4 at 38.7%; amend SC-004 to ≥35%.**

38.7% of SignalR payload is a substantial genuine win, and 40% was an estimate-era target
rather than a physical boundary. String slimming (per-batch vehicle/route ID dictionary)
would clear 40% but is explicitly out of scope for 051.

The amended threshold is **≥35%**, deliberately set as a regression *floor* beneath the
measured 38.7% rather than pinned to the exact figure — the size-budget unit test (P3-U2)
must catch a real regression without turning normal encoding variance into a red build.

Amended in: `spec.md` (SC-004 + US4 acceptance scenario 1 + Independent Test),
`plan.md` (Performance Goals), `contracts/wire-slimming.md` (C6 size vector),
`quickstart.md` (Phase 3 steps 1, 5), `tasks.md` (Phase 6 header, T034, T049).

---

## US4 implementation (2026-08-01)

Code and Tier 0/1 tests for Phase 6 are complete. **Not deployed** — T047 (local end-to-end),
T048 (coordinated ship), T049 (post-deploy measurement) remain.

**Test suites**: 257 .NET tests green (Shared 28, Client.Shared 28, WebAPI 111, Worker 90) plus
the Go validator suite. The in-test size budget (P3-U2) independently reproduces the 38.7%
figure against a frozen v1 replica.

| Task | What landed |
|---|---|
| T034/T035 | v2 round-trip + boundary + exactness vectors; 1,000-record size budget; `JoinCityV2` / unversioned-`LeaveCity` gate constants frozen |
| T036/T037 | Record v2 (scaled-int coords, nullable atomic prior pair, nullable no-default Category); `HubMethods.JoinCity` → `"JoinCityV2"` — same change |
| T038 | Worker emit rules: `ScaleE5`, prior pair only on first-obs/route-change, `NullIfKnownCategory` |
| T039 | 7 emit-rule tests driving the real reconciliation seam via a spy publisher |
| T040 | Compile-only builder updates across 6 existing suites (verified as 0-semantic diffs) |
| T041 | Null-prior replay + upsert survives with `IsStale` intact; cache production code untouched |
| T042/T043 | Hub method renamed; all client call sites confirmed to route through the constant (no bypassing literals) |
| T044/T044a/T045 | Single client decode seam (÷1e5, nulls preserved); `ChefMap.getVehicleCategory` catalog accessor that never defaults to `'bus'`; animator retained-position precedence + FR-013a re-resolution on catalog load |
| T046 | TestHost gate proven with a real MessagePack `HubConnection`: legacy `"JoinCity"` → `HubException` + no batch; `"JoinCityV2"` → replay received |

**Deviation from tasks.md**: T034 specified extending `EventEnvelopeMessagePackTests.cs` in
`Shared.Tests`; that file actually lives in `WebAPI.Tests`. Its v2 updates were made in place
there. One test in it (`Category_DefaultsToBus_WhenOmitted`) asserted the removed `"bus"`
default and was rewritten as `NullCategory_RoundTripsAsNull_NotADefault` — a deliberate
semantic change, since v2 removes that default by design.

---

## SC-005 denominator (T013a) — NOT CAPTURED

`batch_wire_bytes` measures **SignalR payload only**. The Azure Monitor SWA data-out and
Container App egress metrics that form SC-005's HTTP half were never captured before US2
(compression + caching) shipped its code. Per T013a's own warning, once compression is
deployed the pre-feature HTTP baseline is unrecoverable from metrics.

**Consequence**: SC-005 (60–75% total egress reduction) can only be reported as
**projected**, not measured, unless US2 has not yet been *deployed* — in which case
capturing the Azure Monitor baseline now still rescues it. This must be confirmed before
any further deploy.
