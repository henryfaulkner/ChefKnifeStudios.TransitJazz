# Time-to-First-Note — Deep Diagnostic Discovery Document

**Date:** 2026-07-20
**Symptom:** ~10–15 s average from dismissing the AudioUnlockOverlay to the first audible note on the deployed site. Users can't tell whether the app is working.
**Verdict:** The latency is real, structural, and reproducible from production telemetry. It is **not** an audio-stack loading problem — it is a **tone-supply problem**: the server emits far fewer checkpoint crossings than the fleet geometry predicts, in ~30-second bursts, and the client has no audible output of any kind until the first crossing fires. The measured tone cadence alone predicts a 10–20 s expected wait from a random unlock moment, matching the anecdote without needing any other contributor.

---

## 1. How a note happens (the critical path)

1. Worker ticks every **10 s** (`Worker.cs:51`, one `PeriodicTimer` for all cities), fetches each city's GTFS-RT feed, snaps vehicles to route shapes, and runs `CrossingDetector` over the distance advanced since the previous tick.
2. Crossings ride the SignalR batch (`RouteCrossingBatchEvent`) to the client.
3. `TransitMap.HandleVehicleBatchAsync` (buffered until the map is ready) forwards them to `crossing-dispatcher.js`, which asks the animator how long until the animated dot reaches each checkpoint (`vehicle-animator.js:184` `crossingDelayMsFor`) and schedules one `setTimeout` per crossing.
4. At fire time `transit-synth.js triggerNote` plays — **iff** the context is unlocked and the route's palette-slot Sampler is built (lazy: 2 anchor MP3s fetched from gleitz.github.io + decode + `reverb.generate()` on first use).

A note therefore requires: a live batch (not the replay) → containing a crossing → whose fire-timer has elapsed → after unlock → after the slot's sampler builds. Every stage was measured or bounded below.

---

## 2. Measured evidence

### 2.1 Production telemetry — tone supply (the dominant bottleneck)

Queried the worker's own `PerCityCycle` telemetry (dataset `telemetry`, dt=2026-07-19, ~00:00 UTC ≈ 8 PM ET Saturday; the query tool returns the first ~140 rows of the day per filter):

| City | Ticks sampled | Vehicles/tick (avg) | Tones/tick (avg) | Max | **Zero-tone ticks** |
|---|---|---|---|---|---|
| MARTA | 139 (23.2 min) | 192 | **1.14** | 9 | **97 / 139 (70%)** |
| TTC | 35 | 915 | **0.94** | 7 | **24 / 35 (69%)** |
| MBTA | 34 | 385 | 5.35 | 12 | 0 |
| WMATA | 35 | 630 | 8.23 | 19 | 0 |
| NYMTA | 35 | 2,071 | 15.63 | 85 | 7 / 35 (20%) |

Two findings inside the MARTA sample:

- **Burst periodicity.** Silent-run lengths before each tone-carrying tick: `run=2` occurred **36 times** (vs. run=1 ×2, run=5 ×3, run=6 ×1). Tones arrive almost metronomically every **3rd tick (~30 s)**, ~3.8 tones per burst (159 tones / 42 tone-ticks). This is the signature of a feed whose vehicle positions only advance every ~30 s: on the two intermediate ticks `delta ≤ 0` for nearly every vehicle, so `CrossingDetector` emits nothing (FR-008 path, `CrossingDetector.cs:47`).
- **Mean silent gap 22.6 s, max observed 60 s.** A listener unlocking at a random moment waits on average roughly half a burst period plus dispatch spread — **~10–20 s expected time-to-first-note in steady state**. This alone reproduces the reported 10–15 s.

**Suppression magnitude.** 192 vehicles × (say 8 m/s × 10 s / 400 m spacing) ≈ **38 expected crossings/tick** for a fully-moving fleet; observed **1.14** — a ~34× shortfall (TTC: ~180× if its fleet moved at bus speeds). Even discounting layovers and stopped vehicles, the shortfall is far too large to be organic. Candidate suppressors, all of which silently return `[]` in `CrossingDetector.cs`:

| Path | Line | Suspected contribution |
|---|---|---|
| `delta <= 0` — no forward progress along the shape | :47 | Feed refresh cadence (proven ~30 s for MARTA) **and** any vehicle travelling the *reverse* direction of the single stored shape per routeJoinKey — those vehicles' along-distance monotonically decreases, so they can **never** emit a crossing |
| Teleport reset (`delta > 2000 m`) | :51 | Out-and-back shapes: nearest-point snapping flips between overlapping direction legs → large jumps → baseline reset, nothing emitted |
| First observation seeds baseline, emits nothing | :30 | Re-triggered every time a vehicle is pruned (5-min prune loop, `Worker.cs:581`) and re-seen |
| Route transfer reset | :37 | Minor |

None of these paths is currently counted, so attribution is a hypothesis — see Next Steps #2.

Also note: `TriggerPointGenerator.cs:15` spacing is **400 m**, but the design comments at lines 11–13 still analyze 200 m ("200m @ 10 m/s → 20s per trigger"). At 400 m a 10 m/s bus crosses every 40 s — the shipped constant halves the intended musical density and doubles every wait computed above.

### 2.2 Cold-start structure — the first cycle is guaranteed silent

- `TransitHub.JoinCity` replays `LastBatchCache.Current` — and `LastBatchCache.Set` (`ILastBatchCache.cs:54–99`) rebuilds **only** `RouteNearestPointBatchEvent`. **The replay contains zero crossings**, deliberately (see `LastBatchCacheCrossingExclusionTests` and the `TransitMap.razor.cs:119` comment about the "rapid pulsing" regression this prevents).
- There is no REST crossing snapshot either (removed in the same fix). So after page load the first *possible* note is in the first live batch: uniform 0–10 s wait (mean 5 s), then a 70% chance (MARTA, evening) that batch carries no tones, then the animator's fire delay (0…`DurationMs`, which for MARTA's ~30 s fix interval can itself be tens of seconds).
- Crossing dispatch is additionally gated on map readiness (`HandleVehicleBatchAsync` buffers into `_pendingBatches` until MapLibre + MapTiler style load) — only relevant in the fast-click path, ~1–3 s.

### 2.3 Audio-stack costs — measured, and **not** the bottleneck

Measured from this machine against production endpoints (relative magnitudes are what matter):

| Asset | Size | Time |
|---|---|---|
| `GET /gtfs/routes/shapes?city=marta` (deployed Container App) | 140 KB | 574 ms |
| Tone.js via esm.sh (`tone@15.5.30/es2022/tone.mjs` + wrapper + 2 deps) | ~240 KB + deps | ~170 ms/req |
| Sampler anchor MP3s (gleitz.github.io, 2 per instrument) | 14–25 KB each | 45–135 ms each |

- Tone.js is warmed during overlay display (`PreloadAsync` after route load, and `attachUnlockGesture` imports it at overlay first-render), so it is cached before the user clicks in the typical case.
- First-note sampler build (2 MP3 fetches + decode + `reverb.generate()` IR) is **~0.5–1 s** on broadband, maybe ~2 s on weak mobile. A real cost, but an order of magnitude below the symptom.
- The feature-040 module-instance split (interop vs. dispatcher importing different copies of `transit-synth.js`, which would make `_unlocked` never propagate) is confirmed fixed: `TransitSynthJsInterop.cs:23` uses the bare path, matching the dispatcher's bare sibling import.

### 2.4 Perceptual amplifier — the app is *totally* silent until the first note

The pink-noise ambient bed (`transit-synth.js:245–249`) lives on the master bus, which is built lazily inside `instrumentForSlot` — i.e. **on the first note trigger**. Between unlock and the first crossing there is no sound of any kind, no audible confirmation the unlock worked. Combined with a 10–20 s expected first-note wait, this is precisely the "am I using this correctly?" confusion.

### 2.5 Edge case — unlock can silently fail on slow connections (iOS)

`attachUnlockGesture` **awaits the Tone import before attaching** the native click listener (`transit-synth.js:342–354`). A user who clicks Enable before esm.sh finishes falls back to the Blazor `UnlockAsync` path, where `Tone.start()` runs outside the browser's gesture trust window — on iOS Safari the AudioContext can remain suspended → **permanent silence** for that session. Likely the tail of the worst anecdotes rather than the average.

---

## 3. Ranked bottlenecks

| # | Bottleneck | Layer | Evidence | Est. contribution to the 10–15 s |
|---|---|---|---|---|
| **B1** | **Tone scarcity: crossing suppression + ~30 s effective feed cadence** (reverse-direction vehicles structurally mute; delta≤0 between feed refreshes; teleport resets; 400 m spacing) | Worker | 1.14 tones/tick @ 192 vehicles (~34× below geometric expectation); 70% silent ticks; metronomic 30 s bursts; mean gap 22.6 s, max 60 s | **~10–20 s (dominant, steady-state)** |
| **B2** | Cold-start: JoinCity replay strips crossings + 10 s publish cadence + map-ready gating | Server + client | `ILastBatchCache.cs:54–99`, `Worker.cs:51`, `TransitMap.razor.cs:460+` | +5–15 s (fast-click path only) |
| **B3** | Zero ambient output until first note (noise bed built lazily with first sampler) | Client JS | `transit-synth.js:239–253` | Perceptual — converts wait into "it's broken" |
| **B4** | Lazy first-note sampler build (CDN MP3s + decode + reverb IR), not warmed at unlock | Client JS | Measured 0.5–1 s broadband | +0.5–2 s |
| **B5** | Unlock listener attached only after Tone import completes → non-gesture fallback on fast clicks | Client JS | `transit-synth.js:342`, `AudioUnlockOverlay.razor:284` | 0 typically; ∞ on iOS edge case |

**Ruled out with evidence:** Tone.js/CDN load time (preloaded during overlay, ~400 KB total), route-shapes fetch (140 KB / 0.6 s), SignalR connect (starts at app boot, `App.razor:27`, well before unlock), the 040 module-split bug (verified fixed).

---

## 4. Next steps (shortlist)

1. **Immediate audible feedback at unlock (client, ~1 day, fixes the *confusion* even before the *latency*).** Build the master bus and start the noise bed inside `unlock()`/the gesture handler instead of lazily at first note; optionally play a quiet confirmation motif on unlock. Also warm the 3 prod samplers at unlock (fetch+decode are legal pre-gesture; only output needs the gesture) — removes B4 and makes the first crossing instant.
2. **Instrument the suppression (worker, small).** Add per-tick counters for each `CrossingDetector` early-return path (first-seen / delta≤0 / teleport / transfer) to the `PerCityCycle` telemetry row. This turns B1's cause attribution from hypothesis to measurement — do this **before** fixing, so the direction fix can be verified.
3. **Fix reverse-direction muteness (worker, likely the biggest tone-rate lever).** One shape per routeJoinKey means ~half the moving fleet has monotonically decreasing along-distance and can never emit. Options: per-direction shape matching, or detect sustained reverse motion and walk trigger points in reverse.
4. **Kill the guaranteed-silent first cycle (server).** Cache the last N seconds of crossings alongside the position snapshot and replay them on JoinCity with age-adjusted fire delays (guard against the "rapid pulsing" regression the current exclusion fixed — cap age, respect the animator's dot positions).
5. **Revisit 400 m trigger spacing** once 2–3 land: the in-file analysis still assumes 200 m; at current suppression levels halving the spacing is the cheapest 2× density lever, but re-measure first — fixing B1 may make 400 m musically sufficient.
6. **Measure the real user timeline.** Implement the browser-console benchmark harness in §5 so the 10–15 s anecdote becomes a tracked, per-version metric and each fix above is verifiable. The 25-minute / single-evening telemetry sample here should also be widened (the query tool currently returns only the first ~140 rows per day-filter).

### 4.1 Predicted impact per fix (falsifiable forecasts)

Each fix below carries a numeric prediction and the metric that verifies it. If a deployed fix misses its forecast, the attribution behind it was wrong — stop and re-diagnose before stacking the next fix on top.

Baseline (MARTA, evening): 1.14 tones/tick · ~3.8 tones per ~30 s burst · audible mean gap ~8.8 s (dispatcher spreads each burst across the ~30 s tween) · dwell-TTFN ≈ 10–20 s · fast-click adds ~5–15 s · `trigger→audible` ≈ 0.5–1 s.

| Fix | Forecast | Verify with |
|---|---|---|
| #1 Unlock warming + noise bed | `trigger→audible` → **~0 ms**; audible output at t=0 regardless of TTFN (perceived-broken problem eliminated) | `[TTFN]` line; ear check at unlock |
| #3 Reverse-direction fix | Emitting fleet ≈ ×2 → tones/tick 1.14 → **~2.3**, tones/burst ~3.8 → ~7.6, audible gap ~8.8 → **~4.4 s**, dwell-TTFN median roughly halves. ⚠ Telemetry **zero-tick fraction stays ~70%** — it reflects MARTA's 30 s feed cadence, not density. Verify with tones/tick avg, not zero-ticks | `PerCityCycle` tones/tick; dwell-scenario TTFN |
| #4 Replay crossings on JoinCity | Cold-start penalty (~5–15 s) → ~0; **fast-click TTFN converges to dwell TTFN** | fast-click vs. dwell medians |
| #5 200 m spacing | Further ×2 → **~4.6 tones/tick**, audible gap **~2.2 s**, dwell-TTFN median < 5 s (combined with #3) | same as #3 |
| #2 Suppression counters | No user-facing change; converts §2.1's ~34× shortfall from hypothesis to attribution | new counter columns sum ≈ vehicles_processed − tones-emitting vehicles |

### 4.2 Regression map — known landmines per fix

Constraints from this codebase's own history that each fix walks near. Check these in review; they are cheap to respect and expensive to rediscover.

| Fix | Landmine |
|---|---|
| #1 Sampler warm-at-unlock | The ~1.2–1.7 GB Sampler RAM regression (see `transit-synth.js` header + BROWSER_MEMORY_INVESTIGATION doc). Warming is safe **only** because the prod ship list is 3 fixed slots (flat cost). Also: `EvictInactiveRouteAudioAsync` (`TransitMap.razor.cs:526`) disposes a slot after 3 absent batches — a warmed sampler for a quiet route can be evicted before its first note. Either pin warmed slots briefly or accept the re-build. |
| #1 Noise bed at unlock | Must respect the persisted mute setting — `SetAudioEnabledAsync` is pushed during `TransitMap.OnInitializedAsync` with the saved value; starting the bed unconditionally in `unlock()` would sound while muted. Gate on `_audioEnabled` exactly as `getMasterBus` already does. |
| #2 Telemetry counters | The parquet column contract (feature 013) is **frozen snake_case** consumed by the feature-014 allow-list validator — new columns require a matching update to `tools/telemetry-mcp`'s column allow-list or this document's own §7 Step 1 queries will reject them. |
| #4 Replay crossings | (a) The original "rapid pulsing" regression the exclusion fixed — cap replayed-crossing age and respect the animator's current dot positions. (b) Scoping win worth stating: `RouteCrossingBatchEvent` is already in the MessagePack `[Union]` wire contract, so this is a **server-only** change — no 3-lane wire deploy, no client/branch coordination. |
| #5 200 m spacing | Doubles crossing volume system-wide. Check the NYMTA batch against the feature-040 5 MB SignalR ceiling at peak (85 tones/tick observed evening; peak will be higher). Also doubles client timer volume per batch — the crossing dispatcher was built for this, but re-check at NYMTA scale. |

### 4.3 Ongoing tone-supply health check (uses telemetry that already ships)

Define "musical density" as a monitored property: for each city over a rolling hour, **flag when the zero-tone-tick fraction exceeds ~30%** (and, post-#3, when tones/tick falls below ~half its per-city baseline). This catches tone-supply regressions in `PerCityCycle` data that already exists — no new instrumentation — instead of via user anecdotes. Note: **TTC fails this check today** (69% zero ticks from 915 vehicles); its near-silence should be tracked as its own issue, not assumed fixed by the MARTA work.

---

## 5. Measuring time-to-first-note in the browser console (benchmark harness)

The metric to iterate against is one number with two attributable halves:

```
TTFN  =  (unlock → first triggerNote that passes the gates)   ← tone supply: B1 + B2
       + (that triggerNote → sampler.triggerAttackRelease)    ← audio build: B4
```

Splitting it this way tells you *which* fix moved the needle: server/cold-start work shrinks the first half, unlock-time warming shrinks the second.

### 5.1 Permanent probe (recommended — ~15 lines in `transit-synth.js`)

Follow the existing `window.MemoryProbe` idiom: a module-scope object exposed on `window`, marks via `performance.now()`, one summary `console.log` when the first note sounds. All touch points already exist in `transit-synth.js`:

```js
// module scope
const _ttfn = { version: 'SET-PER-DEPLOY', unlockAt: null, firstTriggerAt: null,
                firstAudibleAt: null, droppedWhileLocked: 0 };
window.TtfnProbe = _ttfn;
function _ttfnMarkUnlock() {
    if (_ttfn.unlockAt !== null) return;
    _ttfn.unlockAt = performance.now();
    performance.mark('ttfn:unlock');
}
```

- In **both** unlock paths — the gesture handler's `T.start().then(...)` (`attachUnlockGesture`) and `unlock()` — call `_ttfnMarkUnlock()`.
- In `triggerNote`: the `if (!_unlocked) return;` guard becomes `if (!_unlocked) { _ttfn.droppedWhileLocked++; return; }`; after the `_audioEnabled` gate set `_ttfn.firstTriggerAt ??= performance.now();` and after `sampler.triggerAttackRelease(...)`:

```js
if (_ttfn.firstAudibleAt === null && _ttfn.unlockAt !== null) {
    _ttfn.firstAudibleAt = performance.now();
    performance.measure('ttfn:unlock→audible', 'ttfn:unlock');
    console.log(`[TTFN] v=${_ttfn.version}`
        + ` unlock→trigger=${(_ttfn.firstTriggerAt - _ttfn.unlockAt).toFixed(0)}ms`
        + ` trigger→audible=${(_ttfn.firstAudibleAt - _ttfn.firstTriggerAt).toFixed(0)}ms`
        + ` total=${(_ttfn.firstAudibleAt - _ttfn.unlockAt).toFixed(0)}ms`
        + ` droppedWhileLocked=${_ttfn.droppedWhileLocked}`);
}
```

Notes: the ±20 ms humanize jitter is noise-floor; `trigger→audible` measures the `await instrumentForSlot(...)` (MP3 fetch + decode + reverb IR), i.e. exactly B4. `droppedWhileLocked` doubles as a scenario label: **> 0 at unlock ⇒ steady-state path** (notes were already flowing behind the overlay); **0 ⇒ cold-start path** (B2 is in play). The `performance.mark/measure` calls also make the span visible in the DevTools Performance panel. Read `window.TtfnProbe` at any time for the raw numbers. Set `version` from the deploy (commit short-SHA) so console lines are comparable across iterations.

### 5.2 Console-only proxy for the *currently deployed* build (no redeploy)

The deployed synth logs `[TransitSynth] unlocked…` but nothing at first note. A paste-in proxy: t0 from that log line, t1 from the first gleitz.github.io sampler fetch after unlock (which only happens on the first post-unlock trigger, since locked notes return before `instrumentForSlot`). Paste **before** clicking Enable:

```js
const _t = {}; const _log = console.log;
console.log = function (...a) {
    if (!_t.unlock && String(a[0]).includes('[TransitSynth] unlocked')) _t.unlock = performance.now();
    return _log.apply(console, a);
};
new PerformanceObserver(list => {
    for (const e of list.getEntries()) {
        if (_t.unlock && !_t.mp3 && e.name.includes('gleitz.github.io')) {
            _t.mp3 = e.responseEnd;
            _log(`[TTFN proxy] unlock→sampler-fetch ${(e.responseEnd - _t.unlock).toFixed(0)}ms (audible ≈ + decode + reverb IR)`);
        }
    }
}).observe({ type: 'resource' });
```

Caveats: underestimates by the decode + IR tail (~100–300 ms measured class), and only works on a fresh page load (one sampler build per session). Good enough to baseline the current version before the probe ships.

### 5.3 Benchmark protocol (per version)

| | |
|---|---|
| **Scenarios** | (a) *fast-click*: click Enable the moment it renders — exercises the B2 cold path; (b) *dwell*: wait ≥ 30 s on the overlay, then click — exercises steady-state B1. Run both; they answer different fixes. |
| **Trials** | ≥ 10 per scenario per version; report **median and p90** (the distribution is bursty — means mislead). |
| **Controls** | Same city hash (`#marta`) and comparable time-of-day/day-of-week — tone supply is the dominant term and varies with service level (§2.1 shows evening MARTA; midday will differ). Note cold vs. warm HTTP cache. |
| **Record** | The `[TTFN]` line verbatim (it carries the version stamp) + `droppedWhileLocked` to confirm which scenario actually occurred. |
| **Success criteria** | Step 1 (unlock warming + noise bed): `trigger→audible` → ~0 ms, and audible output at unlock regardless of TTFN. Steps 2–4 (server fixes): dwell-scenario `unlock→trigger` median < 5 s. |

### 5.4 Benchmark log

Append one row per (version × scenario) run, per the §5.3 protocol. This table is the running scoreboard for the iteration loop; compare each new row against the §4.1 forecast for the fix that version ships.

| Version (SHA) | Date | City | Scenario | n | unlock→trigger med | p90 | trigger→audible med | TTFN total med | droppedWhileLocked | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| _current prod_ | _TBD_ | marta | dwell | — | — | — | — | — | — | Baseline via §5.2 proxy (underestimates by decode+IR tail) |
| _current prod_ | _TBD_ | marta | fast-click | — | — | — | — | — | — | Baseline via §5.2 proxy |

---

## 6. Data appendix

- Telemetry: `telemetry` dataset, dt=2026-07-19, `event_type = 'PerCityCycle'`, windows ~23:58–00:23 UTC (≈ 8 PM ET Sat). MARTA feed_freshness ≈ 6.3 s at sample time (the *fetch* is fresh; the *fixes inside it* advance ~every 30 s).
- MARTA tones/tick distribution (139 ticks): 0×97, 1×5, 2×6, 3×8, 4×9, 5×7, 6×4, 7×2, 9×1.
- Network measurements taken 2026-07-20 from a residential connection against `marta-jazz-dev-ca-server.jollytree-dd5ca774.eastus2.azurecontainerapps.io`, `esm.sh`, `gleitz.github.io`.

---

## 7. Optimized re-investigation playbook

How to re-run (or deepen) this diagnosis in ~30 minutes instead of a full code excavation. Steps are ordered by evidence yield per unit effort — the first two usually settle the question before any code is read.

### Step 1 — Telemetry first: is it tone supply? (~5 min, zero code)

One query against the worker's own metrics answers the dominant-bottleneck question:

```
tool:    telemetry-query-bridge → query_telemetry
dataset: telemetry
date:    <UTC day of interest>
filter:  event_type = 'PerCityCycle' AND city_name = 'marta'
```

Parse gotchas (cost real time in this investigation): the result is a box-drawing text table — read the saved JSON with **UTF-8 encoding** (`Get-Content -Raw -Encoding UTF8`) and split rows on the `│` character (`[char]0x2502`), or the columns silently misalign. The tool caps output at **~140 rows per query** (≈ 23 min of ticks), returned from the start of the day partition (00:00 UTC ≈ 8 PM ET the prior evening). To widen coverage, query multiple `date`s and use complementary filters (`city_name != 'marta'` returns all other cities in one call).

Compute exactly three statistics — each maps straight to user experience:

| Statistic | Meaning |
|---|---|
| `tones_emitted` avg per tick | steady-state note rate; mean note gap = 10 s / avg |
| % of ticks with `tones_emitted = 0` | dead-air fraction |
| silent-run-length distribution (consecutive zero ticks before each tone tick) | expected wait from a random unlock ≈ ½ the dominant run period; also exposes feed-refresh periodicity (MARTA's `run=2 ×36` pattern ⇒ 30 s fix cadence) |

**Decision point:** if tones/tick is high and evenly spread (like WMATA/MBTA) but users still report silence, the problem is client-side — skip to Step 2. If tones are sparse/bursty (MARTA/TTC), the problem is server-side supply — skip to Step 4.

### Step 2 — Browser console: split client vs. supply (~10 min)

Use §5.1's `[TTFN]` probe if deployed; else the §5.2 paste-in proxy. `unlock→trigger` ≫ `trigger→audible` confirms supply; the reverse implicates the audio build path. `droppedWhileLocked > 0` proves batches and crossings are flowing pre-unlock (rules out connection/dispatch failures without reading any code).

### Step 3 — Rule out the audio stack by measurement, not inspection (~5 min)

All three externals are measurable with one `Invoke-WebRequest` block (sizes/times in §2.3). Don't re-derive: Tone.js is preloaded during overlay display (`PreloadAsync` + `attachUnlockGesture`), samplers are 2 MP3s of 14–25 KB each, route shapes are 140 KB. Unless a CDN is regionally degraded, this stage contributes < 1 s and is not worth further effort.

### Step 4 — Code path anchors (read only what the data implicates)

The full critical path, so nobody re-excavates it:

| Stage | Anchor |
|---|---|
| Publish cadence (10 s, all cities, one timer) | `Worker.cs:51` |
| Crossing emission + all four silent-suppression paths | `CrossingDetector.cs:30,37,47,51` |
| Trigger spacing (400 m; comments still claim 200 m) | `TriggerPointGenerator.cs:11–15` |
| Cold-start replay strips crossings (deliberate) | `ILastBatchCache.cs:54–99`, `TransitHub.cs:21`, exclusion test + `TransitMap.razor.cs:119` comment |
| Client batch handling / map-ready buffering | `TransitMap.razor.cs:460` (`HandleVehicleBatchAsync`), `:155` (`OnCrossingsAsync`) |
| Fire-delay computation against the animated dot | `crossing-dispatcher.js:79`, `vehicle-animator.js:184` (`crossingDelayMsFor`) |
| Unlock gates, lazy sampler build, noise bed, esm.sh import | `transit-synth.js:342` (gesture), `:370` (`triggerNote`), `:285` (`instrumentForSlot`), `:239` (master bus/noise), `:225` (`getTone`) |
| SignalR init at app boot | `App.razor:27` → `ApplicationViewModel.cs:74` → `SignalRNotificationService.cs:36` |

### Step 5 — Deepen supply attribution without a deploy (~30 min, the current frontier)

The one question this document leaves open — *which* `CrossingDetector` suppression path eats the ~34× — can be answered locally: run the TransitDataWorker on a dev machine against the live GTFS-RT feeds (it is read-only toward the agencies) with temporary counters on the four early-return paths, and log per-tick `{firstSeen, deltaLeq0, teleport, transfer, emitted}` per city. Two or three 10-minute runs give the attribution that decides between the reverse-direction fix (Next Steps #3) and a feed-cadence/spacing response — before committing to the telemetry-schema change in Next Steps #2.

### Known dead ends — do not re-verify

- The feature-040 module-instance split (`_unlocked` never propagating) is **fixed**: `TransitSynthJsInterop.cs:23` and the dispatcher both import the bare module path.
- The replay's crossing exclusion is **intentional** (prevents the "rapid pulsing" burst on load) — don't treat it as a bug; any change must re-solve that regression (Next Steps #4).
- `feed_freshness_seconds` being small (6.3 s) does **not** mean positions are fresh — MARTA's fetch is fresh while the fixes inside it advance ~every 30 s. Don't rule out feed cadence on freshness alone.
- Notes are not lost to mute/filter gates at fire time by default: `_audioEnabled` defaults true and the crossing filter is null with nothing selected.

---

## 8. Addendum (2026-07-20) — Time-to-first-render: counts label & route filters

Same symptom family, different output: after dismissing the overlay, the vehicle-count rows (`TransitRunningLabel`) and the route-filter pills stayed blank for a long time even though their data was already on the client. Diagnosed from code, same supply-vs-render split as §1–2. Three causes, all fixed on `045-time-to-first-note`:

| # | Cause | Anchor | Fix |
|---|---|---|---|
| **R1** | **Blank-label staleness (dominant).** `ApplicationViewModel.InitializeAsync` awaited the full SignalR handshake *before* starting the shape fetch, so the JoinCity replay (which seeds every vehicle count) usually landed while `CategoryOrder` was still empty — the counts recompute painted nothing. When `BuildRouteItems` later filled `CategoryOrder` it never recomputed counts, and `TransitRunningLabel`'s **only** re-render trigger is an `ActiveCountsByCategory` PropertyChanged (it has zero parameters, so parent re-renders — including overlay dismissal — skip it). The next recompute required a batch containing a **new** vehicle; the replay had already seeded them all, so the label could stay blank for minutes. | `RouteFilterViewModel.BuildRouteItems`, `TransitRunningLabel.razor:117` | `BuildRouteItems` now ends with `RecomputeActiveTransitCounts()` — whichever half (shapes / counts) arrives last completes the render. Regression test: `LateRouteLoadCountsTests` |
| **R2** | Serialized startup: the shape fetch (what the filter pills render from) queued behind SignalR negotiate + WS upgrade + JoinCity (~0.5–2 s). | `ApplicationViewModel.InitializeAsync` | `Task.WhenAll(ConnectSignalRAsync, LoadRoutesAsync)` |
| **R3** | Duplicate shape fetch: `TransitMap.LoadRoutesAsync` issued its own second `GetAllRouteShapes` (140 KB MARTA, multi-MB NYMTA) alongside `ApplicationViewModel`'s, competing for startup bandwidth. | `TransitMap.razor.cs:554` | TransitMap awaits the new `ApplicationViewModel.RoutesLoadedTask` (never faults; false on load failure) and copies from the shared cache |

**Expected outcome (falsifiable):** both the counts label and the filter pills become renderable at `max(SignalR connect, shape fetch)` ≈ 1–3 s after page load — i.e. already painted by the time a dwell user closes the overlay, and ≤ ~3 s for a fast click. If the label still lags the pills after this, R1's attribution was wrong — re-diagnose before stacking fixes. Verify in-browser: the label paints without waiting for the next 10 s live batch.
