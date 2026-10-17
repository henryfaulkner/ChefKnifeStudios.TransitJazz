# Reported Vehicle Capture Contract v1

## Source policy and extraction

`ReportedVehicleHistory` has independent Disabled/DryRun/Persistence mode and validated finite limits; default Disabled. There is no per-city capture selector. Disabled enables no cities. DryRun and Persistence enable every city in the configured registry with an explicit valid source catalog. A city with no source policies is unsupported and excluded from capture; an incomplete, misaligned or invalid explicit catalog fails startup. DryRun validates and logs bounded diagnostics without database persistence. Resolve enabled sources before capture begins. Each source has explicit stable key, ID namespace, identity rule/version and expected cadence/gap. Neither source keys nor namespaces include endpoints/credentials. Stable policy fingerprints include membership, rules and gaps. Overlapping feeds share identity domain; independently assigned ID systems differ. Source-set mutation during an hour invalidates that cohort; configuration takes effect cleanly at an hour boundary where possible.

Per decoded source response record receipt UTC from injected TimeProvider immediately before raw extraction. A timestamp at whole-city fetch/cycle completion is prohibited. Parse failures use attempt-completion UTC and failure disposition, not successful-empty. Successful source parse permits empty population; null/malformed root is failure. Preserve the source observation collection when combining city results. Submit observations before processing/publication; source outcome is independent of synthesized feed outcome.

GTFS identities require nondeleted Vehicle record with nonblank descriptor ID; position/trip/route are unnecessary. Missing ID increments unidentifiable-record diagnostics. MARTA original TRAIN_ID is agency-train-id extracted before realtime/Line/coordinate guards; all identifiable original train records contribute. Stable documented source-specific fallback may be enabled by policy with its own identity kind and disclosed limitation; undocumented FeedEntity/trip IDs never substitute. Values retain exact spelling/case. Stale records count; differential feeds count records actually received without recreating previous unchanged entities.

NYC must fetch raw subway responses even if stop-offset readiness fails, then synthesize only with ready table. Apply raw extraction in generic GTFS, MARTA bus/rail, NYC bus/subway and legacy rail helper. Both Worker and WebAPI host construction paths carry the policy to separate NYC option types. Existing map/music identity fallback behavior is unchanged.

## Hour assignment and coverage

- Cohort interval is `[H,H+1h)` in UTC; response at H+1h belongs to successor. Each source owns independent timing state.
- Gap G is explicitly persisted per source; initial proposal 60 seconds, override permitted up to 300 seconds only when greater than configured expected cadence. Source policy/cadence are independent of route-hour policy; validate actual sequential source timing before enabling.
- Start continuity requires prior successful receipt `p <= H` and first successful receipt `f >= H`, with `f-p <= G`. Receipt exactly H can serve as the boundary witness, but retained run/source-policy continuity must still establish capture was active at H. Startup at H without continuity cannot manufacture Complete.
- End continuity requires last successful receipt `l < H+1h` and successful successor receipt `s >= H+1h`, `s-l <= G`. No successor identities move backward. Failed observations prevent Complete even if gap checks pass.
- Maximum interval between consecutive successful receipts including witnesses must be <=G. Every configured source must have successful receipts, both witnesses, no failed attempts, stable membership/rules, and no missing identities for fully covered evidence.
- Bounded predecessor timing state survives only within the running collector. New run/startup lacks continuity and qualifies affected hour as Partial. Record start/end witness booleans and times per source.
- Finalize preceding hour once all successor witnesses resolve or after hour-end + maximum configured G. Independent lifecycle timer sweeps at <=15 seconds and shutdown flushes unresolved states as Partial. At most current + prior grace-hour population/city. Late/regressed receipt events invalidate affected cohort; never reopen finalized history.
- Intact hour with >=1 successful raw response is usable even if other sources failed/missed, and may have zero memberships. Missing identifiers qualify it as Partial without losing identified identities. No successful response is NoData, excluded. Identity loss, unsupported policy, clock regression, source-set mutation and conflict exclude entire hour.
- Complete additionally requires correct membership count and all expected sources' diagnostic evidence. Fallback limitations are always disclosed even with Complete observed-identity coverage.

Use checked integer arithmetic for counters. Overflow means integrity loss. Maintain summary counters and constant-sized source timing state; never retain a poll-event ledger. Repeated missing-ID counts are record counts, not distinct omitted vehicles.

## Proposed finite options

| Option | Initial proposal / rule |
| --- | --- |
| Eligible configured cities / sources per city | <=32 / <=32; enabled nonempty source catalog required |
| Distinct identities per city/hour | <=100,000 |
| Value / domain / kind / source key bytes | <=512 / <=64 / <=64 / <=64 UTF-8; no NUL |
| Open hours per city | <=2; no arbitrary late-hour creation |
| Global active identity estimated bytes | <=64 MiB |
| Global extracted evidence estimated bytes | <=32 MiB across concurrent source tasks |
| Global queued envelope estimated bytes | <=64 MiB; queued batch count <=32 |
| Bytes reserved per identity | max(2,048, 1,024 + 2 * UTF-8 identity tuple length); includes conservative string/set overhead; profile and increase reservation if necessary |
| Source diagnostics | <=32 entries/header; <=16 KiB serialized header diagnostics; fixed allowlisted reason/limitation sets |
| Store rows/command | <=128 memberships; single transaction/batch |
| Write command / attempt / attempts | <=5 sec / <=30 sec / <=3, honoring cancellation |
| Shutdown drain | <=15 sec |
| Retention / delete batch / cleanup cadence | >=7 days (finite configured maximum 30); <=24 expired headers/transaction; timer every 15 minutes |

Count limits are ceilings, not promises that 100,000 long identities fit current memory budget. Byte admission may bind first. Global reservations cover all simultaneously active, queued and extracted populations, including clone/serialization lifetimes. Transfer reservations with envelope ownership, release on disposal/write completion; do not duplicate arrays unaccounted. Avoid allocation of an unbounded extracted identity list before checking admission. Existing decoded feed memory is baseline; any new extractor buffers count toward these limits. Validate configured caps at startup; DryRun also respects bounds.

Limit overflow or extraction error marks the affected population identity_integrity=false and excludes it. Do not silently truncate and report a count. Keep a bounded invalid diagnostic header when possible; release unused identity memory promptly. Queue admission uses TryWrite with byte reservation; failure drops the whole envelope with bounded diagnostics, leaving missing database coverage. Polling never waits for database/queue space. Observations cannot be submitted twice; repeated response IDs deduplicate within envelope naturally.

## Store/writer/ownership

Writer follows existing host registration order: register writer before capture lifecycle and Worker so shutdown stops Worker/sweeper and flushes before writer closes/drains. Persistence needs configured DB connection; dry run uses no DB. Atomic city/hour advisory lock has a reported-specific namespace. Header plus complete membership chunks commit together, including valid empty headers. Exact semantic payload retry is Unchanged; different run/header/membership population sets sticky HasConflict and returns Quarantined. Persisted timestamp and conflict flag do not participate in semantic equality. Sorted exact tuple comparison is authoritative; digest alone is insufficient. NoData/lost/conflicted hours never become complete via retry or union.

Single active capture owner/city is mandatory for persistence enablement. A shared host-level guard reserves Persistence exclusively for WebAPI; standalone Worker Persistence is rejected at configuration binding before hosted services start, even when both hosts receive identical global settings. Standalone Worker supports Disabled and DryRun only. Deployment must document a single WebAPI replica per city (or disjoint configured city registries); concurrent conflicting batches quarantine and emit diagnostics, never merge. This plan adds no distributed leader election. Restart uses a new run ID, and closure of an already persisted partial hour can only quarantine on different evidence.

Retention uses bounded server-side header deletion and cascade with cutoff earlier than every active supported Stats window. Seven days safely exceeds current completed-hour/local-day horizons; validate options against ingestion grace. Cleanup is hosted outside requests, bounded by command deadline and cancellation, no row/identity dumps. Delayed cleanup increases storage, not counts. Schema/data deletion is not part of planning.

## Operational evidence

Log city, hour, bounded reason/status, counts and timing; never log raw IDs or credentials. Track limits, queue bytes/admission loss, write failures/conflicts and retention lag without source URLs/vehicle cardinality labels. Persistence failure represents missing evidence, not a successful-zero row. Release records actual identity availability/domain contracts, ownership, cohort completeness, memory/storage/query plans and combined matched-load overhead before enablement.
