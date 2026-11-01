# Stats retrieval: single-run baseline versus new release

Status: single-run comparison complete within the user's revised scope, with a supplemental new2 trace now providing Boston chart timing. No repeated-trial campaign was requested or performed.

## Scope and preserved evidence

The user explicitly replaced the proposed repeated-trial campaign with a comparison of the supplied captures. Baseline A is `transitjazz_trace_base2` plus `transitjazz_har_base2`; candidate B is `transitjazz_trace_new` plus `transitjazz_har_new`, with `transitjazz_trace_new2` supplied later for visual chart timing. New2 has no matching HAR; its visual result is kept separate from the earlier B HTTP observations. Earlier unmatched/aborted rehearsals are excluded. The original repeated-trial guide remains preserved as protocol.md; this report documents the scope amendment rather than rewriting that frozen protocol.

Both pairs come from the production domain, using the same recorded Android/Pixel 10 user-agent emulation and main viewport approximately 592 x 845.333 CSS pixels, DPR 2, host DPR 1.5. They are single mobile-emulated observations, not desktop results, medians or production percentiles. Old/new release identities are supplied by the user; deployed frontend/API revision IDs are not independently established.

Originals and parser outputs (paths below are relative to `C:/Users/hfaul/Documents/TransitJazzPerf/stats-retrieval-001/`):

- A: `A/preflight/base2/trace/` and `A/preflight/base2/har/`.
- B network capture: `B/single-run/trace/` and `B/single-run/har/`.
- Supplemental B chart capture: `B/chart-transition-new2/trace/`, `visual-metrics.json`, `findings.md` and evidence images in that folder.
- Structured comparison: `comparison/single-run-metrics.json`.
- Baseline chart evidence: `A/preflight/base2/first-chart-detail.jpg`.
- Candidate evidence: `B/single-run/new-last-frame.jpg` and `B/single-run/filmstrip-overview.jpg`.

## Observed result

**The new release moves the two stats requests before interaction and eliminates action-triggered requests for both opening Boston and switching to All Cities. The supplemental new2 trace shows Boston's first visible chart appearing about 0.8-1.0 seconds sooner (roughly 40%) than the baseline. This is a single-observation comparison, not a general production percentile or causal proof.**

| Metric | Baseline A | New B | Interpretation |
| --- | ---: | ---: | --- |
| Stats POSTs after navigation but before opening stats | 0 | 2 | Background preparation is observed in B |
| New stats POSTs when opening Boston | 1 | 0 | Opening uses prepared evidence instead of starting another request |
| New stats POSTs when switching to All Cities | 1 | 0 | No request is added by the scope change in B |
| Total stats POSTs for the recorded post-navigation sequence | 2 | 2 | Work moved earlier; fewer total requests are not demonstrated by this sequence |
| Boston: click to first visible plotted timeline | About 2.15-2.25 s | About 1.26-1.34 s (new2) | About 0.8-1.0 s / 38-44% sooner in these samples |
| All Cities: click to visible plotted chart | Unknown | Unknown | Neither filmstrip covers this transition |
| Stats transferred bytes reported by HAR | 66,232 | 65,828 | Similar reported volume; B has aborted-completion markers |
| Browser document load event after navigation | 599 ms | 1,631 ms | B is later in this sample; this is not map readiness or isolated stats overhead |

The first Boston click in B is at trace-relative 6,475.005 ms. The Boston request's termination event is at 5,423.921 ms (1,051.084 ms before the click); All Cities terminates at 6,081.959 ms (393.046 ms before the click). No further stats POST appears after the Boston or All Cities clicks through the recording end. Boston's actual totals and city controls appear in the filmstrip, supporting that usable prepared data reached the view.

This supports the intended architectural change: HTTP work is no longer initiated by these two interactions. The supplemental new2 visual evidence also shows a shorter click-to-first-visible-Boston-chart duration. Neither observation establishes full-dashboard readiness or reduced hourly traffic from a single sequence.

## HTTP observations and completion caveat

| Scope | A recorded HTTP duration | B recorded HTTP duration | Raw difference |
| --- | ---: | ---: | ---: |
| Boston | 948.480 ms | 565.401 ms | -383.079 ms (-40.39%) |
| All Cities | 101.460 ms | 110.217 ms | +8.757 ms (+8.63%) |

These are the HAR's recorded total durations, including browser queueing and preflight-related waiting. **The B entries are HTTP 200 at headers but contain `response._error=net::ERR_ABORTED`, lack embedded response JSON, and lack a successful native network finishTime.** The raw percentage differences must not be presented as a proven successful-download speedup or regression.

The native trace and HAR do match within B: Boston terminates after 565.406 ms in the trace versus 565.401 ms in the HAR; All terminates after 110.219 versus 110.217 ms. Request order, absolute timing, endpoints and status agree, and HAR payloads identify Boston/unfiltered and All. Thus this is a matching capture pair, unlike the earlier rehearsal.

The error markers do not by themselves prove an application failure. B's Boston controls and populated totals appear without an additional stats request. The abort's cause and complete-response acceptance timestamps are not established; retain both the error markers and the visual evidence rather than dropping the run or silently calling it clean HTTP completion.

For A, both requests are successful HTTP 200 completions, embedded JSON bodies verify exact response selections, and HAR durations equal native ResourceSendRequest-to-finishTime durations. They include cutoff 2026-11-01T21:00:00Z. The matching B response bodies/metadata are not exported, so B's generation/cutoff cannot be independently verified from its HAR.

The waiting portion for Boston changes from 853.598 ms to 62.322 ms, while the receiving portion changes from 60.655 to 471.331 ms. This demonstrates why a raw total cannot identify which server/client stage improved: network state, cache state, renderer scheduling and completion semantics all differ. No DB execution timing or server cache-hit classification is available.

## Chart rendering evidence, updated with new2

A's first Boston click dispatch is at 9,876.687 ms. The cumulative chart is blank at screenshot timestamp 12,052.563 ms and plotted at 12,128.198 ms, with a confirming following frame. Event timestamps give 2.176-2.252 s; screenshot expected-display timestamps give 2.146-2.241 s. The conservative envelope is approximately 2.15-2.25 s. Lower charts below the fold are not covered by this endpoint.

The original B capture's last screenshot is at 7,516.238 ms, or 1.041 s after its Boston click. Its expected-display timestamp is 1.017 s after the click. The screenshot shows populated Boston totals but an empty cumulative chart frame. Thus the new timeline is **not yet drawn at the last observed frame**, but no later screenshot establishes when it appears. It could finish before or after A's roughly 2.2 seconds. A blank frame at 1.04 seconds is neither proof of a regression nor proof of a speedup.

The baseline and original B recordings contain exactly 450 screenshots, ending before the All Cities action. B's All Cities click occurs at 9,727.239 ms, after its last screenshot. DOM creation or completion of network work cannot fill this visual evidence gap. The user subsequently supplied new2, analyzed below; no repeated-trial campaign is requested.

### Supplemental new2 visual timing

The new2 trace starts at 2026-11-01T23:05:01.359Z. Boston stats is opened at 8,880.086 ms. The final blank cumulative-chart frame is at 10,166.187 ms, the first plotted frame at 10,217.547 ms, and the following frame at 10,236.504 ms confirms a stable plot. Screenshot event timestamps bound the transition at 1.286-1.337 s; expected-display timestamps bound it at 1.257-1.324 s. Across both conventions, the conservative envelope is **1.26-1.34 seconds**.

Compared with baseline's 2.15-2.25 s, this is **0.808-0.994 seconds sooner**, or **37.67-44.16% shorter**. These ranges reflect screenshot timing uncertainty, not confidence intervals. The improvement is consistent with eliminating baseline's roughly 0.95-second action-triggered Boston request, but other release differences also affect rendering.

The matching visible viewport is approximately 592 x 844 CSS px / DPR 2, compared with 592 x 845.333 in A. Live totals changed because new2 was recorded almost two hours after baseline. There is no new2 HAR; do not splice the earlier B HTTP durations into this trace. Only the cumulative chart is visible; lower charts and All Cities visual timing remain unmeasured. New2's All click is at 13.038 s, after screenshots stop at 12.239 s. Evidence is preserved under `B/chart-transition-new2/`.

## Startup and comparability limits

The browser load event is later in B by approximately 1,031 ms in this sample. This is a real recorded event difference, but it is not the map-usable milestone. The recordings do not establish map readiness or audio readiness. Chart library loading appears earlier in the B navigation, while A loads chart assets after opening stats; release-level resource-loading changes are a confound for attributing load time solely to stats preparation.

The stats click occurs approximately 4.124 s after the Boston navigation in A and 3.304 s in B. Neither is the guide's fixed 30-second wait. Both show the introduction before the stats action. CPU/network throttling, backend load, cache outcomes, and exact deployment configuration are not fully verified. Do not use the single samples to establish general startup regression, statistical significance, equivalence, or a causal claim about one code path.

## Conclusions supported by the supplied captures

1. **Preparation worked:** B made Boston and All requests before interaction, then added no HTTP request on either action.
2. **The full sequence did not use fewer stats POSTs:** both used two post-navigation POSTs; B paid their cost earlier.
3. **Boston's first visible chart is faster in the supplied samples:** base2 is about 2.15-2.25 s; supplemental new2 is about 1.26-1.34 s, approximately 0.8-1.0 s / 38-44% sooner. The endpoint is the first visible cumulative chart, not all three charts or a production percentile.
4. **Startup deserves a separate interpretation:** B's document load event is later, while map/audio readiness and the isolated contribution of stats preparation are unknown.

This completes the requested single-run comparison. The missing endpoints remain explicitly unknown; they are not a request to repeat the experiment.
