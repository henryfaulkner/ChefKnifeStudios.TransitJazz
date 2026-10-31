# Stats Refresh API / Read Contract v1

Planning extension to feature 058 contracts. During implementation update `specs/058-stats-overlay/contracts/stats-overlay-{api,read,ui}-v1.md` to remove conflicting minute-TTL/unfiltered-only/dialog-owned rules. Other metric, coverage, endpoint and safety contracts remain binding. No implementation or runtime validation is claimed here.

## Endpoint and additive response

POST `/stats/overlay/query`, same request and anonymous bounded input. Keep `stats-overlay-v1`, `observed-city-route-hour-statistics-v1`, HTTPS, rate limiting, safe problems, compression, response caps and `Cache-Control: no-store`. Unknown request controls remain rejected. No raw identities, ETags, bulk requests or persistent storage.

New servers return three camelCase fields on every valid response:

```json
{
  "generatedAtUtc": "2026-10-30T10:59:00Z",
  "cutoffUtc": "2026-10-30T10:00:00Z",
  "refreshIntervalMinutes": 60,
  "refreshWindowStartUtc": "2026-10-30T10:02:00Z",
  "nextRefreshAtUtc": "2026-10-30T11:02:00Z"
}
```

Excerpt only; all existing required response fields remain. Times describe original evidence and never change on a hit. Cutoff is still floorUtcHour(generation-existing grace), not quarter-hour arithmetic. Actual local-day end can make nextRefresh earlier than scheduled next. PreviousDay remains existing authoritative early-morning evidence, never manufactured client-side.

Fields are optional only for old-server deserialization; do not assign default 60 to an absent field. Preserve wire presence sufficiently to distinguish all three absent from explicit invalid null/partial metadata. Old clients ignore additive fields. New clients accept absent-all legacy fields with minute freshness/hard expiry GeneratedAtUtc+60 seconds; never infer hourly freshness or extended stale fallback. A supplied null, partial set, invalid interval, non-UTC/unusable time or bad order is invalid_response, preserving prior valid evidence. Add serialization fixtures for both directions and invalid presence combinations.

## Server policy

| Setting | Default / validation |
| --- | --- |
| RefreshIntervalMinutes | 60; integer 1–60, divides 60; reject fractional configuration |
| IngestionGraceMinutes | Existing 2; existing 1–10 validation |
| MaximumCacheEntries / Bytes | Existing 33 / 64 MiB maximum |
| MaximumFilteredCacheEntries | 8, nonnegative and <=total entries; zero disables retention only |
| MaximumFilteredCacheBytes | 16 MiB, nonnegative and <=total bytes; positive when filtered entries >0 |
| MaximumConcurrentReads / ReadTimeoutSeconds | Existing maximum four / five |
| CacheSeconds | Retired from active schedule-aware server policy/config/validation/tests |

At lower configured total bounds, configure valid filtered subset explicitly; startup fails inconsistent settings. Configuration uses existing restart path with equal settings per replica. Open clients adopt settings on next accepted response/reload; no push.

Use one TimeProvider-backed helper:

```text
origin = Unix epoch UTC
I = interval minutes
G = existing grace minutes
windowStart = origin + floor((now-origin-G)/I)*I + G
scheduledNext = windowStart + I
effectiveNext = min(scheduledNext, earliest future root city LocalDayEndUtc)
```

Half-open windows advance exactly at boundary. Use mathematical floor, not truncation. Grace may exceed a short interval; do not add an undocumented grace<interval restriction. Reporting plan still uses real request time, existing city-local dates/DST and completed UTC hours. Past PreviousDay day ends do not govern refresh. Local-day transitions invalidate identity even inside the same fixed window. No timer or server stale fallback.

## Exact identity and fill behavior

Canonical structured fields: normalized scope/city, ordinal sorted distinct case-sensitive route-key array, refresh window/interval, cutoff, root city dates/time zones, configured membership/cadence, grace, contract/metric definitions and reported-vehicle settings/source/cadence fingerprints. No ambiguous delimiter concatenation. Equality must verify canonical content if digest-indexed. Cache and in-flight identities are identical; selection slots include exact route array. Policy/date/cutoff changes invalidate reuse immediately. Never relabel unfiltered evidence as filtered; All is one independent consistent read.

Order: exact unexpired hit -> join exact fill -> acquire one read slot without queue or existing busy -> one bounded query/projection/size-validation fill. Publish promise under short lock, run outside lock. Waiter cancellation affects its wait only. Fill deadline and host shutdown cancel shared work. On every result release slot/remove only matching promise; failures removable for retry. No lock across query/serialization/external wait. All valid 200 coverage states, including unavailable and zero, are eligible for retention; transport/problems are not.

Original boundary is absolute and hits never extend it. Expire on UTC or monotonic deadline; observed clock regression flushes retained state. Crossing fills cannot be cached or overwrite newer evidence. They may return original expired metadata, allowing normal admitted client follow-up. Failure after expiry returns existing safe problem, no expired response fallback.

## Retention and observability

Total/filtered count and estimated-byte caps apply together. Include response, canonical keys/entry overhead; current conservative response estimate is UTF-8 size*8+4096. Serialize at most once for validation/admission. Expired entries removed first; filtered admission evicts filtered LRU only, never baseline; baseline evicts filtered before oldest baseline. Nonfitting valid response returns uncached with bounded shared fill preserved. With 33 baseline scopes no filtered retention need fit.

Process-local replicas can generate different timestamps; no global one-fill promise. Record bounded scope, selection-kind and result, entries/estimated bytes, fill duration, skipped admissions and eviction reason. No raw route set/key/hash/visitor metric label. Measure transient query/projection/serialization memory separately from retention.

## Required compatibility and correctness checks

Hourly/15-minute/default grace; divisor validation; exact-before/at/after fixed and local-day/DST boundaries; unchanged completed-hour arithmetic; exact route reorder/duplicate joining versus case/route/city isolation; delimiter-collision fixtures; filtered zero-retention coalescing; four distinct active fills/busy; caller cancellation, synchronous completion/failure, own deadline and shutdown cleanup; expired crossing and older replacement races; clock rollback; subset/total counts/bytes/LRU protection; valid partial/pending/unavailable/zero retention; no cached failure; original metadata on hit; old/new serialization/presence; reported-vehicle policy isolation; unchanged one-statement All/read arithmetic and no sensitive fields.
