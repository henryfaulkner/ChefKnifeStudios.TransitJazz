# Stats retrieval: production before/after performance experiment

Created: 2026-11-01. Original protocol: repeated production before/after campaign.

Execution scope amendment, 2026-11-01: the user explicitly requested no repetitions and accepted the supplied `base2` trace/HAR pair as the baseline against the supplied `new` pair. That single-run comparison is complete in [STATS_RETRIEVAL_PERFORMANCE_RESULTS.md](STATS_RETRIEVAL_PERFORMANCE_RESULTS.md). Repeated-session targets below describe the original design and were not executed; they are not prerequisites for the revised deliverable. The original protocol snapshot/hash is preserved with the experiment artifacts. Unknown measurements remain unknown rather than being inferred or triggering requests for further captures.

## Purpose and comparison

Measure whether the stats retrieval changes improve the experience at `https://www.transitjazz.com/#boston`, and identify any cost to startup, chart rendering, or resource usage. Run baseline A against today's production deployment. After the user deploys the changes, repeat the same protocol against production as candidate B. Do not compare production with localhost or a Debug build.

The earlier browser-automation observations are exploratory only. They timed browser-control calls and polled canvas dimensions; they are not baseline A and must not enter this experiment's results.

This is a sequential production experiment, not a controlled randomized experiment. It can demonstrate a repeatable change in the measured experience. Changing live data, traffic, server cache state, or infrastructure can prevent attribution to code alone. If the evidence is ambiguous, report inconclusive and use the controlled follow-up described below. Do not promise a definitive causal conclusion from two deployment windows.

## Freeze the protocol before collecting A

1. Create an experiment folder outside the repository, for example `C:/Users/hfaul/Documents/TransitJazzPerf/stats-retrieval-001/`, with `A/`, `B/`, and `comparison/` subfolders.
2. Save this guide with the A artifacts. Record its Git revision or file hash. Use the same saved protocol for B; record deviations rather than silently editing the rules after observing results.
3. Record the deployed frontend and API revisions/build identifiers, deployment completion time, and API replica count for A. Record them independently for B; the local checkout is not proof of a deployed revision. If a revision cannot be established, mark deployment identity unverified.
4. Inventory every changed feature in B. In particular, record whether B includes the newly added loading buffer: 37.5 ms before projection, 100 ms after view rendering, and a 150 ms fade. A change to these timings is a visual-transition change, not evidence of a retrieval slowdown.
5. Prefer deploying retrieval changes with identical chart/transition behavior to A for a retrieval-only conclusion. If the actual B release also includes the buffer, keep it fixed throughout B and report both retrieval timings and total user-visible timings. The experiment then compares the whole releases; do not claim that a chart delay proves retrieval regressed.
6. Keep the numeric refresh interval fixed during B. Record A's actual refresh/cache behavior rather than pretending it supports the new setting. Do not introduce cron scheduling or other unrelated changes between A and B.

The agent may collect read-only browser measurements and local artifacts. Deployment remains the user's step. Do not change production configuration, restart services, flush server caches, provision infrastructure, or generate a concurrency load test as part of this protocol.

## Fixed environment manifest

Complete `manifest.md` in each phase. Copy the A values to B and check each one before proceeding.

| Item | Required value / recording rule |
| --- | --- |
| URL | `https://www.transitjazz.com/#boston` for both phases |
| Computer | Same machine, OS, power supply, and power mode; record processor and RAM |
| Browser | Same Chrome version, profile, extensions, and zoom; record exact version; if updated, flag the mismatch |
| Viewport | 1440 x 900 CSS pixels, 100% browser zoom; verify actual `innerWidth`/`innerHeight`; record device pixel ratio |
| DevTools | Undocked, same capture settings throughout; the app remains foreground and visible |
| CPU/network | No artificial throttling for the primary profile; same connection, VPN state, and physical location |
| Other activity | No second TransitJazz tab; close prior experimental tabs; avoid downloads, builds, and other CPU-heavy work |
| Presentation | Light theme, same distance units, no selected routes, closed data tables and route accordion, same scroll position |
| Audio | Muted before the timed map-ready milestone, same in both versions; audio performance requires a separate matched profile |
| Browser asset cache | Enabled for the primary experiment; see priming below; do not confuse this with the app snapshot store or server cache |
| Recording | Performance screenshots enabled, memory capture off, JavaScript samples unchanged; save exact settings |
| API/configuration | Record API hostname, deployment revisions, replica count, stats enablement, refresh policy and ingestion grace |
| Timing context | UTC timestamps, completed-hour cutoff, local date, response generation timestamps, response sizes and traffic/load observations |

Use the same time-of-day window and day type for A and B when practical. Avoid a city-local midnight, a known service incident, and the first 15 minutes following a deployment. Record elapsed time since deployment and any known scaling/restart event. If another deployment occurs during a phase, stop the block and keep its artifacts separately.

Do not invent a matching setting when the old version lacks it. Record `not supported` and describe the difference.

## Required capture capability: verify before the baseline

Use Chrome DevTools Performance recordings with screenshots and the Network log/HAR. Anchor interactions to the actual page click event in the trace, not to the time a browser tool was called or returned. The Performance trace supplies event, main-thread, network, and screenshot timestamps without adding application instrumentation.

Before the campaign, collect one disposable rehearsal trace and network capture. Confirm that:

- The actual click and its target can be identified.
- The stats request, request body/selection, response status, completion, and transferred bytes can be identified.
- Filmstrip screenshots show the old view, loader if present, and the charts after the transition.
- Both recording files can be saved locally and reopened for analysis, with enough timestamp information to correlate them.

If the available browser tool cannot record/export these artifacts, use DevTools through the supported browser UI or ask the user to capture the files. Do not quietly fall back to timing automation calls. Without native recordings, label the measurement unavailable; do not run a weaker experiment and call it equivalent.

If precise chart initialization timings require application timing marks, the identical marks must exist in both versions before A is collected. Marks added only to B cannot support a one-to-one A/B comparison. Native traces are the default because today's live site does not need a preparatory deployment.

Chrome references: [Performance recordings and screenshots](https://developer.chrome.com/docs/devtools/performance), [Network capture, cache settings and HAR export](https://developer.chrome.com/docs/devtools/network/reference).

## Timing definitions

Use one monotonic browser trace timebase for each recording. Store timestamps as milliseconds relative to trace start. HAR wall-clock timestamps may be aligned to the trace only through a documented common request; never subtract unrelated clocks.

| Symbol / metric | Definition |
| --- | --- |
| `N` | Start of the navigation to Boston in a fresh app tab |
| `M` | First screenshot showing the map rendered, introduction dismissed, Open statistics control available, and initial vehicle markers present |
| `C` | Actual page click event on Open statistics, Boston, or All cities; use the same click-event boundary in A and B |
| `Qstart`, `Qend` | Start and complete response-body receipt of each matching POST `/stats/overlay/query`; record every matching request, including startup preparation |
| HTTP duration | `Qend - Qstart`; includes network/server work, not just database execution |
| Request delay | `Qstart - C` for an action-triggered request; if requests start before C, classify as preparation rather than a negative action delay |
| Data available after click | `max(0, Qend - C)` for matching successful evidence used by the view; zero only when retained matching evidence is established |
| `Vlow`, `Vhigh` | Bounds on visible readiness: last screenshot not ready and first screenshot ready, as defined below |
| Click-to-visible readiness | Interval `[max(0, Vlow - C), Vhigh - C]`; do not present filmstrip sampling as exact millisecond timing |
| Post-response work | Bounds on `V - max(C, Qend)` when a matching response can be linked; includes projection, chart setup and deliberate visual delays, not pure rendering CPU |
| Startup map readiness | `M - N`; report screenshot uncertainty here too |
| Loader exposure | Filmstrip interval during which the stats loading mask is visible; record no loader, loader, or unknown |
| Main-thread work | Trace scripting, style/layout and paint durations within the measured interval; report totals and tasks exceeding 50 ms, including WASM work where observable |

Visible-ready means the requested scope is selected, correct scope content is shown (Route activity for Boston; City activity for All), all three chart areas contain their plotted data, and no loading mask or incomplete fade obscures them. Confirm that the chart layout stops changing in a subsequent screenshot. Background vehicle motion does not prevent readiness; assess the stats region.

Canvas existence, nondefault dimensions, an initialization event, and `aria-busy=false` are useful diagnostics but are not proof that chart animation/fading has completed. Do not use them as the primary visual endpoint. Empty/error chart states must be recorded as such, not counted as three successfully plotted charts.

Filmstrip sampling may be too sparse to resolve a 137.5 ms difference. Preserve the bounds. If the uncertainty overlaps the claimed difference, report that difference as unresolved. Screenshots do not expose an exact Chart.js completion timestamp.

## Cache and session definitions

- **Fresh app session:** a newly opened tab with a new WASM app instance and no retained in-memory stats from a prior trial. A refresh or new session does not clear the server cache.
- **Primed browser assets:** load the app once, open Boston and All to load chart assets, then close that tab. Do this identically before each phase. Leave normal HTTP caching enabled. This priming run is excluded by definition, not because it is slow.
- **Fixed-wait session:** a fresh app session left on the map for exactly 30 seconds after M before opening stats. This gives preparation an opportunity without selecting only the candidate's successful preparations.
- **Retained preparation:** verify matching evidence and request ordering from the recording. If preparation is unfinished after 30 seconds, keep the result and flag it; do not wait longer or drop the trial.
- **Server cache:** uncontrolled in this live experiment unless supported server diagnostics demonstrate the outcome. Classify hit/miss/coalesced/unknown when evidence exists. A fast HTTP response alone does not prove a hit.

The new implementation prepares data before opening; the old implementation may fetch only on open. That behavioral difference is the treatment being measured. Count preparation's costs from navigation rather than deleting them from the report.

## Trial matrix and sample counts

Run two blocks of ten fresh sessions for each primary cohort in each phase: 20 immediate-open sessions and 20 fixed-wait sessions per phase. Alternate cohorts within each block to reduce ordering effects. Use the same order in B. Repetitions within a session are related observations; preserve the session ID and do not treat them as independent visitors.

| ID | Scenario | Action sequence and purpose |
| --- | --- | --- |
| S1 | Early open | Fresh app session; click Open statistics as soon as M is reached, target within 500 ms; measure navigation/startup and Boston readiness. Record actual `C-M`; if outside the target, flag a protocol deviation. Do not wait for stats preparation. |
| S2 | Fixed-wait Boston | Fresh app session; remain on map for 30 seconds after M; open Boston stats. Primary test of preparation benefit. Record actual wait; tolerance +/- 1 second. |
| S3 | First All selection | In each S2 session, wait two seconds after Boston is visibly ready, then select All cities. Determine whether data was prepared before this click. |
| S4 | Return to Boston | In the same session, wait two seconds after All is ready, then select Boston. Measure reuse and exact selection correctness. |
| S5 | Close/reopen | Wait two seconds after Boston is ready, close stats, remain on map for two seconds, then reopen. Measure whether an on-open request or loader reappears. |
| S6 | Never-opened startup cost | Five additional fresh sessions per phase; remain on the map for 120 seconds without opening stats. Count stats requests/bytes, trace startup work and map readiness. Do not hide the tab. |

Run each sequence once per fresh session. Stop each interactive trace after two seconds of confirmed final readiness. Allow up to 15 seconds per action; retain timeouts and errors as outcomes. Do not repeatedly retry a failed action within the same trial. Investigate an outage before generating more traffic.

S2-S5 must fit within one refresh window for the primary comparison. If an automatic boundary refresh overlaps, retain the trace and classify it as a boundary trial; collect an additional primary session at the end of that block. Report both the excluded-from-primary count and boundary outcomes. Do not exclude legitimate slow work, incomplete preparation, or cache misses simply because they worsen the result.

Supplementary profiles, kept out of the primary aggregates:

- **Asset-cold:** ten fresh sessions per phase with the browser HTTP cache disabled in DevTools, same capture settings and S1 sequence. This does not make the server cache cold or necessarily reset DNS/TLS/WASM caches. Name it asset-cold, not completely cold.
- **Filtered routes:** ten fixed-wait sessions per phase with one exact Boston route-key set selected from the baseline's real request. Save the request body and reuse exactly those case-sensitive keys. If a route is absent in B, report the mismatch; do not substitute a new route silently.
- **Schedule/idle behavior:** one ten-minute never-opened visible session per phase to compare request rate and byte cost over the same elapsed duration. Separately observe at least two scheduled refresh events per phase if the timing budget allows; compare each phase's real returned schedule. Do not change clocks or shorten B's interval just to accelerate a test. Absence of an hourly boundary in ten minutes is not proof that refresh is broken or eliminated.
- **Audio-on:** a separate matched set with the same user gesture and audio settings, if audio-startup effects are in scope. Muted primary runs cannot establish audio non-regression.

## Capture procedure for every session

1. Ensure all prior experimental TransitJazz tabs are closed. Open a fresh blank tab, attach DevTools to that tab, and begin the Performance recording and Network capture before navigating that same tab. Keep Preserve log on across navigation; disable screenshots in Network if using Performance screenshots so capture settings are identical and overhead is limited.
2. Navigate that tab to the production Boston URL. If the introduction appears, dismiss it consistently. Confirm the preconfigured muted audio setting before M; do not alter unrelated settings. Record the introduction-dismissal timestamp and any settings adjustment so operator delay is visible when interpreting startup time. Record a failure to reach M within 30 seconds as a startup failure.
3. Follow the assigned scenario's exact timing and action order. Use clicks on visible controls; do not issue stats requests directly or invoke application internals.
4. Keep the page foreground. Do not resize, scroll, change theme, open data tables, or use another app during timed interactions. If interrupted, flag it and retain the artifacts.
5. Stop and export the Performance recording and Network HAR for that session. Keep request payloads, statuses, durations and byte sizes needed to verify scope and data. Use sanitized HAR export; retain only experiment-relevant content in shared summaries.
6. Identify C from the trace's actual UI event. For each view, identify Vlow/Vhigh from the filmstrip and link requests by selection payload, response content, and time. If the trace cannot establish the link, write unknown rather than assuming a retained snapshot was used.
7. Record errors, missing charts, request cancellations, rate limits, chart failure fallbacks and wrong-selection content. Keep their denominators in the final report. An unavailable-data state is not a successful fast render.
8. Save the trial row and close the tab before the next fresh session. Do not delete failed runs or overwrite earlier traces.

At phase completion, export available server stats telemetry covering the phase: cache outcomes, fill durations, underlying-read counts, response bytes, rejection/errors, API CPU/memory and replica changes. Record metric availability and attribution limits. Do not claim database savings or per-visitor CPU from aggregate production counters shared by unrelated visitors.

## Artifact layout and data schema

Use filenames such as `A/block-01/fixed-03/performance.json.gz`, `network.har`, and `notes.md`. Keep original exports immutable; derive analysis into a separate file.

Each phase contains `manifest.md`, `trials.csv`, `requests.csv`, raw session captures, and `summary.md`. Empty numeric cells mean unavailable; never use zero for missing measurements.

`trials.csv` header:

```csv
phase,block,session_id,scenario,trial_id,utc_started,frontend_revision,api_revision,navigation_ms,map_ready_low_ms,map_ready_high_ms,click_ms,visible_low_ms,visible_high_ms,matching_evidence_before_click,post_click_stats_requests,startup_stats_requests,total_stats_transfer_bytes,loader_shown,charts_plotted,correct_selection,status,protocol_deviation,trace_path,har_path,notes
```

`requests.csv` header:

```csv
phase,session_id,request_id,scope,city_slug,route_keys,started_ms,completed_ms,status,transfer_bytes,decoded_bytes,generated_at_utc,cutoff_utc,next_refresh_at_utc,cache_outcome,attribution,notes
```

`attribution` is startup preparation, action-triggered, scheduled refresh, retry, or unknown. One request may serve multiple actions; reference its ID rather than counting it as multiple HTTP calls. Store elapsed timestamps relative to the corresponding trace and document any HAR alignment.

## Comparison and decision rules

Write `comparison/summary.md` with the following, separately for each scenario and profile:

1. Attempted/succeeded/failed/timed-out/deviated counts, and total observed rate-limit/wrong-selection/chart-failure counts.
2. Median and interquartile range of successful click-to-visible bounds, HTTP durations, request delays, startup map readiness, and observable main-thread work. Preserve lower/upper readiness bounds and sample counts. With 20 sessions, p95 is descriptive and unstable; do not treat it as a precise production percentile.
3. Counts of retained-data opens with zero new requests; preparation completed by the fixed 30-second wait; loader exposure; stats requests and transferred bytes from navigation through each sequence. Compare S6 startup overhead and never-opened cost alongside S2 warm-open benefits.
4. Data/load differences: payload size, route/city count where observable, cutoffs, freshness, server traffic, instance count, cache outcomes and time-of-day. List unknowns explicitly.
5. Absolute change `B - A`, relative change `(B - A) / A`, and uncertainty. Calculate percentages only for comparable successful measurements with a nonzero baseline.

Treat these as predeclared practical thresholds, not universal statistical guarantees: a latency change is material at the larger of 100 ms or 10% of the A median. Evaluate startup and user-visible latency separately. A new error or wrong-selection result is a correctness concern regardless of latency.

For each latency scenario, calculate the median difference twice: candidate lower bounds minus baseline upper bounds, and candidate upper bounds minus baseline lower bounds. If this interval spans zero, screenshot precision alone cannot establish the direction. For stronger sampling evidence, bootstrap independent sessions within each phase (10,000 resamples, fixed seed 062) and calculate 95% intervals; do not bootstrap S2-S5 actions as independent sessions. Report both sampling and screenshot uncertainty rather than replacing either with an exact-looking number.

Call a material improvement/regression demonstrated in this experiment only when its direction survives measurement uncertainty, both blocks show the same direction, and the observed difference exceeds the practical threshold. If a sampling confidence interval is available, it must also support that direction. Otherwise report mixed or inconclusive. A failure to detect a regression is not proof of equivalence, and an A/B production difference alone does not prove which code path caused it.

Interpretation examples:

- Lower HTTP duration but unchanged chart readiness: retrieval improved, another part still dominates the interaction.
- Zero action-time requests after preparation, but higher startup work: latency was moved earlier; quantify the tradeoff.
- Same HTTP duration but higher post-response delay with a newly added buffer: investigate presentation separately from retrieval.
- Longer request delay while All preparation is in flight: inspect the single-request owner and queue priority; confirm using traces rather than assuming.
- Larger payload or higher production load in B: report the confound; do not attribute the whole difference to retrieval code.

## Handoff before and after deployment

Before the user deploys B, deliver baseline A's manifest, completed sample counts, raw-capture location, summary and protocol deviations. Explicitly state whether the native timing/capture preflight passed. Do not describe the old exploratory measurements as a completed baseline.

After the user confirms B is deployed, verify its frontend and API identities, wait the same deployment-settling period, recheck the manifest, repeat the same priming and trial order, and produce the comparison. Preserve A even if B fails.

If production changes leave the cause unresolved, run a separate controlled follow-up: Release builds of A and B on the same host, identical captured statistics/transit input and server-cache conditions, identical instrumentation, and interleaved randomized A/B runs. This isolates client scheduling/processing costs; a fixture/replay cannot establish real database/server-cache savings. To isolate server costs too, run both backend versions against the same disposable dataset and equivalent cache/load conditions. Do not replace the requested production comparison with this follow-up or claim its results apply to production without qualification.
