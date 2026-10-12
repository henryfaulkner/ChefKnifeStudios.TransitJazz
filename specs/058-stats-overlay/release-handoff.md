# Stats Overlay release handoff

**Navigation simplification (2026-10-12)**: Overlay navigation is withdrawn at the user's request. Stats uses a local visibility flag and the browser's native dialog; Info keeps its existing local open/close behavior. No shared coordinator, history entries/traversals, Info/Stats switching, modal lifecycle manager or special settings replacement is required. Earlier navigation prototype tasks and evidence are historical.

**Scope update (2026-10-12)**: The user has assessed the feature and confirmed it works. Remove the temporary chart prototype and browser-history diagnostic UI. Ship English only: no language selector, saved language preference, runtime language switching or Spanish satellite resource. Earlier Spanish/prototype validation records describe the prior implementation.

**Feature:** 058-stats-overlay  
**Status:** Local implementation, tests, browser checks and final disabled artifacts are complete where identified below. Public exposure, deployment, visitor review, actual retained-history inspection and matched-load acceptance have not occurred.  
**Evidence:** [validation.md](validation.md), [tasks.md](tasks.md), and the [read](contracts/stats-overlay-read-v1.md), [API](contracts/stats-overlay-api-v1.md), and [UI](contracts/stats-overlay-ui-v1.md) contracts.

This record distinguishes measured local behavior from the operator evidence needed for release. No commit or commit-producing hook is permitted. Existing user changes to deployment workflows remain for the user to review.

## Definitions and current configuration

- The schema dependency is `20261010151240_CreateCityRouteHourStatistics`, including the existing `(city_slug, hour_start_utc, route_join_key)` index. The metric definition is `observed-city-route-hour-statistics-v1`.
- Canonical configured cities are `atlanta`, `washington-dc`, `boston`, `new-york-city`, `toronto`, `philadelphia`, and `denver`. Eastern cities use `America/New_York`, Toronto uses `America/Toronto`, and Denver uses `America/Denver`. Resolved source cadence limits currently equal 30 seconds for all seven cities.
- One server instant defines the snapshot. The cutoff is the UTC hour floor of that instant minus two minutes. Today follows each city's local calendar day; Last 24 Completed Hours is an independent UTC interval ending at the cutoff. All sums each city's own local day.
- Zero requires eligible evidence. Partial values retain known contributions; missing evidence is not estimated. Future plot values remain null. Selected coverage uses expected route-hours and available route-hours separately.
- Bubble X is kilometers divided by route-vehicle-hours; Y is kilometers divided by active clock-hours; marker area represents the weighted mean active vehicles. Counts are summed before division. City activity samples count once per city/hour.
- API `StatsOverlay.Enabled=false` and client `AppSettings.FeatureFlags.StatsOverlay=false` remain the source defaults. Capture is independently disabled with dry run enabled in the current source configuration. These defaults establish neither deployed configuration nor actual retained history.
- Limits are 4 MiB request, 8 MiB uncompressed response, 4,096 route keys per city/selection, 32 cities/categories, and 512 UTF-8 bytes per route key. Oversized scopes fail completely; success is never truncated.
- Cache: unfiltered successful snapshots only; at most 33 replaceable scope slots, 64 MiB estimated object weight, and 60-second absolute lifetime measured from the snapshot instant. Four cold reads are admitted without a queue. The read budget is five seconds.
- Rate policy: burst ten, refill one token per second, no queue. The application uses peer IP; trusted Azure ingress provenance and actual replica behavior require operator verification.
- Client: one active Stats request, 200 ms selection debounce, 60-second visible refresh, and generation plus normalized-selection validation. Failure cooldown starts at 60 seconds, grows to five minutes, and honors longer `Retry-After`. Theme/language are presentation updates.
- Chart dependency: Pax 0.9.1, local Chart.js 4.5.1 with license/provenance, and direct client Blazor references pinned to 10.0.9.

## Gate ledger

| Gate | Current evidence | Remaining acceptance |
| --- | --- | --- |
| G1 contracts/fixtures | Independent numerator/denominator fixtures, explicit JSON nulls, policy/window checks; regression suite passed | No contract blocker identified |
| G2 fixed charts | Fixed model browser matrix accepted, including DST, lifecycle, visibility, blocked/late asset fallback, language/theme updates and narrow layout | Full screen-reader, touch and 200%-zoom acceptance remains pending |
| G3 history/modal | Actual map/welcome ownership, Back/Forward, replacement, foreign/obsolete state, reload, structured-clone preservation, audio unlock/mute, focus and delayed close/reopen checks | Reduced-motion runtime and live playback/SignalR feed evidence |
| G4 PostgreSQL/API | Full WebAPI suite passed with both disposable DSN variables; actual acquisition cancellation, blocked-read deadline, pooled recovery and representative EXPLAIN measured | No local correctness blocker identified |
| G5 integrated stories | Healthy/zero/pending/partial/unavailable, exact historical selection, shared mute, safe 429/503/422, stale failure/cooldown and obsolete-response browser cases exercised | Full accessibility matrix and comprehensive refresh/day-boundary matrix |
| G6 actual history | No operator database inspected; source connection string is blank | Per-city retained coverage, accepted 057 dependency, persistence/capture mode and finalizer/writer lag |
| G7 capacity/performance | Current capped-writer synthetic cardinality, over-cap, response-size and <5s safe-read cases measured; tuned query plans preserved | Isolated cache/in-flight memory profile, responsive maximum-route browser timing, trusted ingress/replicas and matched production load |
| G8 release | Runbook, quickstart, disabled API/WASM artifacts and artifact hashes recorded; source exposure remains false | Five-visitor review, operator acceptance, staged rollout and withdrawal evidence |

Do not promote a pending gate based on a skipped test, a source default, a disposable fixture, or a single latency observation.

### Synthetic measurements available so far

An owned PostgreSQL 18 fixture contained 98,304 healthy rows: one synthetic city, 4,096 routes, 32 categories, and 24 hours. The final capped-writer and indexed-mapper harness measured:

| Scenario | Result | Elapsed | Uncompressed / gzip response |
| --- | --- | --- | --- |
| 4,096 ordinary keys, unfiltered | Complete success; 4,096 routes/bubbles, 32 categories; 94,208 today km and 98,304 last-24 km | 1,974.5 ms | 4,779,040 / 96,544 bytes |
| 4,096 ordinary selected keys | Complete success; same route/category bounds and measures | 1,647.4 ms | 6,519,827 / 156,251 bytes |
| 4,096 keys of 512 bytes, unfiltered | 422 `response_too_large` | 3,116.2 ms | No partial success |
| 4,096 selected keys of 512 bytes | 422 `response_too_large`; request 2,109,495 bytes | 3,125.2 ms | No partial success |
| 4,097 retained keys | 422 `scope_too_large` | 1,167.6 ms | No partial success |

The oversized selected run's sampled managed peak was 352,054,536 bytes. It includes pools and prior reads and is not isolated cache-object or four-reader peak memory evidence. The conservative cache-admission estimate is not a heap measurement. These are synthetic single observations, not production p95 or writer-budget acceptance. The maximum-route browser inspection tool timed out on frame-tree inspection, but Escape recovered the page and the app continued polling; this does not establish either a page freeze or accepted maximum-route render latency.

### Representative SQL plan

The current parameterized query was explained against the same 98,304-row disposable fixture. Planning/execution milliseconds were 3.521/1,660.567 for ordinary unfiltered, 3.487/1,694.573 for ordinary selected, 3.848/3,205.170 for 512-byte unfiltered, and 10.783/3,354.401 for 512-byte selected. The planner chose sequential scans; no existing-index utilization is claimed. All four executions exceed the 250ms prepared-query target. Raw plans and query text are in `fixtures/evidence/`; no index migration or global PostgreSQL tuning is justified by these measurements.

The same loopback-only PostgreSQL fixture was used for a real Npgsql lifecycle check: caller cancellation during one-slot pool acquisition returned at 323ms for a 300ms token; a 200ms total budget consumed during a delayed 350ms acquisition stopped before SQL at 367ms; an API read blocked only by an `AccessExclusive` lock in a fixture child database returned safe `read_timeout` at 1,086ms against a 1s command budget; after rolling back that fixture lock, the same pooled query succeeded and `SHOW statement_timeout` was `0`. This demonstrates local cancellation/cleanup, not production contention or load.

## Matched-load protocol (define before collecting release results)

### Record the environment first

The operator must record artifact hashes, schema/index version, city/route/category cardinality, retained interval/coverage, database size and query settings, API/worker resource limits, replica counts, trusted ingress configuration, and the client device/OS/browser/network matrix. Record the actual peer address and ingress behavior without logging arbitrary forwarded headers or route arrays. The run must use representative retaining cities and normal publication/capture; disposable fixtures remain a separate correctness result.

Keep transit inputs, cadence, city/category selection, traffic shape and resource allocation equivalent across these conditions:

| Condition | History capture | Stats reads | Purpose |
| --- | --- | --- | --- |
| A | Off | Off | Equivalent baseline without the added history/Stats work |
| B | On | Off | History cost with Stats removed |
| C | On | On | Combined release workload and total history/Stats budget |
| D | Off | On | Stats comparison with capture held off; valid retained history still used |

Capture and Stats exposure are independent controls. Do not alter retention or historical rows to make a condition pass. Run A/B/C in an interleaved order over equivalent inputs; compare C against A for the total five-percent budget and C against B for the incremental read effect. D verifies independence and does not substitute for C.

### Samples and timing boundaries

1. Warm the same artifacts, connections and representative scopes; discard at least ten warm-up samples per condition.
2. Collect at least 100 measured samples per timing/condition and at least 100 worker cycles per baseline comparison. Record duration, sample count, failures and exclusions; include failures in the outcome summary.
3. Calculate p95 using the sorted sample at one-based rank `ceil(0.95 * N)`. Preserve the samples used for the calculation.
4. Warm useful open measures user activation to visible, consistent cards/charts or useful table fallback on each recorded device/network. Cold read measures admission through a complete safe server result. Prepared read measures the bounded query with the established database environment. Also record the full response wall time and bytes.
5. Exercise city unfiltered, a typical selected subset, the largest supported real union, All, partial/missing data, and grace/hour/midnight transitions. Avoid changing unsupported dates through the public API; windows remain server controlled.

### Admission, isolation and thresholds

- Run ordinary concurrency one, then four distinct cold scopes, then additional requests during saturation. Confirm four-read admission, prompt safe busy rejection and no queued backlog. Do not use coalesced identical cache requests as four independent cold reads.
- Exercise burst ten plus overflow, then refill at one request per second. Repeat through the real ingress with independent client identities and spoofed forwarded-header attempts. Record per-replica behavior and the aggregate effect of scaling.
- Confirm cache success-only admission, absolute expiry, cutoff/policy replacement, no selected-scope caching, and the configured object-weight/slot limits. Measure peak request, query-result, response, in-flight and cache memory separately from process/pool overhead.
- Accept warm useful open p95 <=2 seconds, cold read p95 <=2 seconds, prepared query p95 <=250 ms, and all-read safe completion <=5 seconds. Safe size/capacity/dependency failures must remain localized and dismissible.
- Accept combined history plus Stats worker p95 increase <=5% against A, with no read-induced write/capture blockage or lost eligible observations. Preserve counts/outcomes and inspect bounded worker evidence across the same interval.
- Record finalizer/persistence lag, coverage change visibility through the 60-second cache, and measured support for the two-minute grace. A proposed tuning change requires a rerun of affected checks.

**Protocol status:** Defined for operator review. Representative environment inputs and matched-load execution are pending. No threshold is claimed passed here.

## Final artifacts and deployment order

Current-source local Release build and publish completed on 2026-10-11. The clean output is outside the repository at `C:\Users\hfaul\AppData\Local\Temp\transitjazz-058-final-release-current-20261011` (`api` and `client\wwwroot` subdirectories). API publish and trimmed WASM publish succeeded; published API `StatsOverlay.Enabled` and client `AppSettings.FeatureFlags.StatsOverlay` are both `false`. The client publish uses the existing workflow's `BlazorEnableCompression=false` setting. The Spanish satellite, ICU data, local Chart.js/license, and callback module are present. The current-source trimmed Release browser pass verified saved-Spanish startup, all seven historical options, 50/160 city-card totals, three nonzero charts, and a live English switch updating document language/header while preserving chart canvas identities. No deployment occurred.

| Artifact | SHA-256 |
| --- | --- |
| Client.Shared WASM | `C52FC2CFF4195BD9C399B412BE1D170E8617467C300F72CB6C6490F35A022021` |
| Client.WebApp WASM | `192DEFB2DD9A1D80EF07D8DC69DF69BE98E1D3621D1F7B608F333A932E4B3E0E` |
| Spanish Client.Shared satellite | `5F85FFF3A8BA7A8EFBCE4E9B74AB7A182BF3ABAE9797F277DDFF1261E2AAFCE2` |
| ICU globalization data | `B4EE4EE58CCC4D549A2816E8C40A9ECCE40BF5EC26BA9CD5D89AC1E5191CF1AE` |
| Local Chart.js 4.5.1 | `48444A82D4EDCB5BEC0F1965FAACDDE18D9C17DB3063D042ABADA2F705C9F54A` |
| Local chart callback module | `6C87DEC81E833F2F8BFAD3DD895C2A0E6FD3789982B5FDA0085445C74F0F5AF0` |
| API assembly | `353B9011CEC44885DAB0E17DE9D9ABE669F913DEB67FE499FE5EA198781FD575` |

The existing `server.yml`, `client.yml`, and `client-marta.yml` workflows were inspected. Client workflows install `wasm-tools`, publish Release WASM with compression disabled, and upload `publish/wwwroot`; the local Chart.js distribution/license, callback module, Spanish satellite and globalization payload are included in the clean publish. Chart.js is self-hosted; this does not remove the application's existing MapLibre CDN dependency. Workflow files were not edited during this implementation pass. Deployment order remains existing history schema/index dependency, disabled API, disabled client; any exposure step stays pending all G1-G8 acceptance.

Once the required pre-exposure G1-G8 evidence and the existing 057 operational dependency are accepted, the normal operator workflow is:

1. Record the currently deployed API/client artifact identifiers and independent capture configuration for withdrawal.
2. Validate/apply the existing history schema/index dependency before code that reads it. Preserve retained data; do not reset capture, dry-run, cities or categories.
3. Deploy the accepted API artifact with Stats reads disabled. Confirm normal API/worker health and safe 503 `stats_disabled` without a DB read.
4. Deploy the accepted client artifact with the single Stats launcher flag false. Confirm both launchers are absent and map/audio/Info remain usable.
5. At the accepted exposure step, enable bounded reads, smoke complete/partial/zero/pending/unavailable and safe-error behavior, then enable the shared client launcher flag. Confirm welcome access stays silent and native audio enablement remains separate.
6. Observe the bounded telemetry and worker health over the accepted rollout interval. Any skipped required acceptance leaves exposure disabled.

### Safe error smoke checklist

- Disabled reads: 503 `stats_disabled`; missing dependency: 503 `dependency_unavailable`.
- Deadline: 503 `read_timeout`; cold-read saturation: 503 `busy`.
- Rate refusal: 429 with safe code and `Retry-After`; no queued burst.
- Oversized complete scope: safe 422; no truncated success. Invalid request/scope/city: safe 400.
- Client shows the finite localized reason, honors cooldown across scope/selection/retry/visibility, retains actual stale snapshot time for same-scope failure, and never displays raw server detail.
- A 200 pending/partial/unavailable response is evidence state and does not become a transport retry failure.

## Monitoring and withdrawal

Use the existing Stats meter's bounded scope/status/outcome, cache-event, duration and response-byte instruments together with existing worker cycle/write evidence. Confirm the deployed collector exports them before assigning dashboard/alert checks. Do not add city/route-array labels or a new monitoring service. Evaluate sustained deadline/busy/rate refusal, response-size refusal, cache saturation, read latency and worker capture/write degradation against the measured baseline; operator alert thresholds remain pending matched-load evidence.

Withdrawal order is to hide both launchers, disable reads, and restore the recorded prior client/API artifacts if necessary. Verify map/music/Info after dismissal and after restoration. Preserve history and all independent capture settings. Do not roll back the retained-history schema merely to withdraw Stats.

Record the trigger, operator, time, config/artifact identifiers, expected disabled response, launcher absence and map/audio/worker checks. Local source flags and synthetic failures are not deployment or rollback evidence. T087/T088 stay pending until the operator-controlled actions and observations are recorded.

## Visitor review

After the accepted UI exists, obtain five actual visitor reviews. At least four must explain scope, zero/partial/unavailable evidence, completed-hour freshness and All's separate local days using the visible help/tables. Automated tests, agents and author inspection do not replace this threshold. Review and resulting copy fixes remain pending.
