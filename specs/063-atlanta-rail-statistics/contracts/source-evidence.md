# Contract: MARTA rail and source-aware capture

## Rail HTTP boundary

Use only:

```text
https://developerservices.itsmarta.com/mcsmrail/itsmarta/railrealtimearrivals/developerservices/traindata
```

Both Worker/WebAPI hosts, production/development, bind endpoint settings from the named `TransitSources:atlanta:RailRealtime` section, independent of `Cities[]` ordering. The key binds through `TransitSources__atlanta__RailRealtime__ApiKey` using an operator-managed secret reference. Never put its value in production configuration files, command arguments/history, reports or output. Do not restore the retired endpoint or add another rail poll.

MartaCity is the actual caller through ITransitCity. RailRealtimeAdapter is a separate current implementation, not a registered production poller; use shared pure request/normalization behavior to keep it consistent. Encode the apiKey query value and suppress credential-bearing HTTP diagnostics. Missing key fails before HTTP; named RailRealtimeApi clients have a five-second timeout in both hosts with no added retry. HTTP/authentication/timeout/parse failures become bounded rail-source failure without invalidating buses. Caller cancellation propagates.

Success is a non-null JSON array compatible with RailArrivalDto fields TRAIN_ID, LINE, LATITUDE, LONGITUDE, IS_REALTIME, optional EVENT_TIME. Validate actual value types in controlled tests and a later redacted authenticated sample. Only realtime rows with usable identity, known route, finite/ranged coordinates become positioned vehicles. Normalize/group train IDs once and apply deterministic duplicate policy; conflicting coordinates cannot prove full positioned coverage. Preserve original train IDs for existing reported-vehicle policy.

Valid empty/schedule-only content can establish observed realtime-position zero. Realtime records rejected for invalid positions are coverage limitations, not complete zero. Parse/null/HTTP failure is never empty success. Event time must have explicit verified timezone/format semantics; absent/ambiguous/unverified time cannot establish a movement interval. Do not guess server-local time or claim live schema compatibility before verification.

## CityFetchResult extension

Keep existing combined outcome/count fields for monitoring. Independently carry bounded immutable source evidence even when reported-vehicle capture is off:

| Input | Required evidence |
| --- | --- |
| Successful positioned source | Stable source key, success, receipt UTC, positioned coverage validity |
| Valid empty source | Stable key, empty, receipt UTC, valid zero support |
| Failed source | Stable key, failed, receipt UTC, bounded safe failure reason |
| Intentionally disabled source | Stable expected key, disabled, receipt UTC; remains coverage limitation |
| Unknown provenance | Explicit limitation; cannot infer missing dependency from successful siblings |

Reuse configured `marta-bus`/`marta-rail`, never URI/vehicle identifiers. Explicit inventory/dependencies survive the hour, including source/config changes. Atlanta mapping is static bus routes → marta-bus, BLUE/GREEN/RED/GOLD → marta-rail. Other cities without explicit contracts keep legacy behavior. Overlapping feeds require explicit dependencies/deduplication and proven substitution before a single source claims complete coverage.

## Route/category capture decision matrix

| Route evidence | Activity | Movement | Publication | Coverage |
| --- | --- | --- | --- | --- |
| Own source success, processing/index valid | Accept sample/identities | Accept only existing valid intervals | Independently assess | May be Complete if all boundaries/dependencies pass |
| Own source empty success | One zero sample | Valid observed zero support | Independently assess | May be Complete |
| Sibling source failure | Preserve own valid samples | Preserve own accepted intervals | Independently assess | Own route may be Complete; city Partial |
| Own source failure/disabled | No sample | No new valid interval; break baseline | No unsupported success | Partial/NoData, never invented zero |
| Publication-only failure | Preserve valid sample | Preserve valid intervals | Invalid with reason | Partial |
| Recovery | Accept new valid samples | Reseed before next interval | Independently assess | Recovery hour remains Partial |
| Lost/overflow/unknown/index/processing evidence | Exclude impacted measures | Preserve only when unaffected is proven | Exclude impacted measures | Conservative cohort exclusion when impact unknown |

Source-specific predecessor/successor boundaries and maximum gaps cannot use sibling polling. Baseline resets on source failure/disabling/excessive gap preserve watermark/teleport/stale/nonpositive/transfer rules and prevent bridging outages. Category capture uses the same source validity without changing minute/hour reconciliation or substituting its tables into overlay figures.

## Finalization/persistence

Use existing atomic envelope and table with definition `observed-city-route-hour-statistics-v2`, bounded versioned JSONB source summary and independent SQL scalar validity/reasons. [Data model](../data-model.md) defines fields, support alignment and validation. Include every field in validation, mapping, immutable retry equality and conflict tests. Preserve v1 Complete, conservative legacy Partial exclusion, UTC/collation/queue/memory/retention, same advisory lock and sticky quarantine. No per-poll history, vehicle lists, upgrades, relabeling or backfill.

## Source skill contract

During implementation update canonical `skills/mj-gtfs/SKILL.md` with replacement URL, credential requirement and honest verification status. Synchronize via `tools/sync-skills.ps1 -Mode Sync`, inspect generated `.claude/skills/mj-gtfs`, `.agents/skills/mj-gtfs`, `.opencode/skills/mj-gtfs`, then `-Mode Check`. No direct generated-only edit or authenticated-success claim based on keyless/local evidence.
