# Phase 0 Research: Time-to-First-Note

All Technical Context unknowns were resolvable from the diagnostic document (`docs/TIME_TO_FIRST_NOTE_DISCOVERY_DOCUMENT.md`) plus direct code inspection during planning. No open `NEEDS CLARIFICATION` remain. Each decision below carries the discovery-doc forecast it must satisfy (FR-015) and the regression landmine it walks near (discovery §4.2).

---

## D1 — Immediate audible feedback: build master bus + start noise bed inside `unlock()`

**Decision**: Move master-bus construction and the pink-noise bed start out of the lazy `getMasterBus`/`instrumentForSlot` path and into the unlock path (`unlock()` and the `attachUnlockGesture` handler), gated on `_audioEnabled`. The noise bed already exists (`transit-synth.js:245–249`) and `getMasterBus` already conditionally starts it (`if (_audioEnabled) noise.start()`); the change is *when* the bus is first built — at unlock, not at first crossing.

**Rationale**: Between unlock and the first crossing there is currently no sound of any kind (§2.4), which converts a 10–20 s wait into "it's broken." Building the bus at unlock gives audible confirmation at t=0. `getMasterBus` is idempotent (`if (_masterBus) return`), so the later first-note path reuses it — no double build.

**Landmine**: Must respect the persisted mute setting — `SetAudioEnabledAsync` is pushed during `TransitMap.OnInitializedAsync` with the saved value. Gate the bed start on `_audioEnabled` exactly as `getMasterBus` already does (FR-002). Starting the bed unconditionally would sound while muted.

**Alternatives considered**: (a) A separate always-on ambient node independent of the master bus — rejected, duplicates the noise recipe and complicates mute gating. (b) Play only a one-shot welcome motif — rejected as insufficient; a continuous bed is what signals "working" during the whole wait. A welcome motif MAY be added on top but is optional and must be data-derived (Principle VIII), not hand-composed.

---

## D2 — Warm the 3 prod samplers at unlock

**Decision**: After the gesture, kick off `instrumentForSlot(i)` for the slots covered by `PROD_INSTRUMENTS` (pizzicato_strings / acoustic_bass / acoustic_grand_piano → their 3 slot indices), so the MP3 fetch + decode + `reverb.generate()` IR are done before the first crossing. Fetch/decode are legal pre-gesture; only *output* needs the gesture, which unlock already satisfies.

**Rationale**: First-note sampler build is a measured ~0.5–1 s (broadband), ~2 s weak mobile (§2.3, B4). Warming removes it from the critical path so `trigger→audible ≈ 0 ms` (§4.1 forecast for fix #1).

**Landmine**: The 1.2–1.7 GB Sampler RAM regression (`transit-synth.js` header, BROWSER_MEMORY doc). Warming is safe **only** because the prod ship list is 3 fixed slots — flat cost regardless of route/vehicle count. **Do not warm the full 6-voice PALETTE.** Also: `disposeInactiveRoutes` (called from `TransitMap.EvictInactiveRouteAudioAsync`) can dispose a warmed slot for a quiet route before its first note. Mitigation: warm exactly the fixed prod slots (which map to whatever routes hash to them, so they're rarely fully inactive); accept a rare rebuild rather than adding a pin mechanism (YAGNI — a rebuild is the pre-fix behavior, no worse).

**Alternatives considered**: Warm on-demand per active route — rejected, that's the current per-route lazy build and reopens both the latency and (if keyed wrong) the RAM regression. Slot-based warming of the fixed prod set is the flat-cost choice.

---

## D3 — Instrument the four `CrossingDetector` suppression paths (before fixing)

**Decision**: Change `CrossingDetector.Detect` to report *why* it emitted nothing, so the Worker can count per reason: `firstSeen` (baseline null, `:31`), `deltaLeq0` (no forward progress, `:47`), `teleport` (delta > 2000 m reset, `:51`), `routeTransfer` (join-key change, `:37`). Sum per city per tick into new `PerCityCycle` telemetry columns. Deploy this **before** the direction fix.

**Rationale**: The ~34× shortfall's cause is currently a hypothesis (§2.1, Next Steps #2). Counting turns it into attribution and lets the direction fix be verified (§4.1: "new counter columns sum ≈ vehicles_processed − tones-emitting vehicles"). Doing it first prevents stacking an unverified fix.

**Implementation shape**: `Detect` currently returns `IReadOnlyList<RouteCrossingRecord>` and returns `[]` on all four suppression paths. Add an `out` suppression-reason enum (or return a small struct `(records, reason)`), default `None` when it emits. The Worker accumulates four ints and stamps them on the `CityTickResult` → `PerCityCycle` row. Keep the signature change internal to the Worker assembly.

**Landmine (FR-016)**: The parquet column contract is frozen snake_case (feature 013) consumed by the feature-014 Go allow-list (`tools/telemetry-mcp/internal/validate/validate.go`, the `kindNumeric` map at ~:55). New columns require a matching validator entry + a `validate_test.go` accept vector, or the mj-data-explorer queries reject them. Also update `TelemetryEventSchemaTests`.

**Column names (proposed, snake_case)**: `crossings_suppressed_first_seen`, `crossings_suppressed_delta_leq0`, `crossings_suppressed_teleport`, `crossings_suppressed_transfer`. All `int?` nullable (PerCityCycle-only), summed on FullCycle like the other per-cycle ints.

---

## D4 — Fix reverse-direction muteness

**Decision**: Allow vehicles travelling opposite the stored single shape's direction to emit crossings. Under the current model, one shape per `RouteJoinKey` means a reverse-direction vehicle's along-distance **monotonically decreases**, so `delta <= 0` on every tick (`CrossingDetector.cs:47`) and it can *never* emit. Detect sustained reverse motion and walk trigger points in reverse (emit crossings for trigger points the vehicle passes going backward along the shape), OR track a per-vehicle direction sign and treat `|delta|` beyond a threshold as forward-in-that-direction.

**Rationale**: This is "likely the biggest tone-rate lever" (Next Steps #3): ~half the moving fleet is structurally mute. §4.1 forecast: emitting fleet ≈ ×2 → tones/tick 1.14 → ~2.3 (SC-002's ≥2× target).

**Chosen approach (research conclusion)**: Track a per-vehicle direction on `CrossingBaseline` (add a `Direction` / last-signed-delta field). When `delta < 0` consistently (not a one-off teleport artifact), treat the vehicle as reverse-travelling and collect trigger points in the window `(currentDistM, LastCrossedAlongDistanceM]` (the reverse window), advancing the baseline downward. A direction flip resets like a transfer (seed, emit nothing) to avoid double-counting at a genuine turnaround. This keeps the teleport guard (`> 2000 m`) intact for the out-and-back snapping-flip case (§2.1 teleport row) — a flip is a reset, not a reverse-emit.

**Landmine**: The telemetry **zero-tick fraction stays ~70%** even after this fix — it reflects MARTA's 30 s feed cadence, not density. Verify with **tones/tick avg**, not zero-ticks (§4.1 ⚠). Out-and-back shapes that snap-flip between overlapping legs must still hit the teleport reset (FR-007), not spuriously double-emit — the `> 2000 m` guard and the direction-flip-resets rule together handle this.

**Alternatives considered**: Per-direction shape matching (store two shapes per route, match vehicle to the nearer-heading one) — more correct but a much larger change touching the shape store, snap index, and `RouteJoinKey` scoping; deferred. Reverse-walk on the single stored shape is the minimal lever that captures the ×2 and is verifiable against the counters from D3.

---

## D5 — Replay recent, age-capped crossings on `JoinCity`

**Decision**: Cache the last N seconds of `RouteCrossingBatchEvent` records alongside the position snapshot in `LastBatchCache`, and replay them (age-adjusted) on `JoinCity`, capped by age. Currently `LastBatchCache.Set` rebuilds **only** `RouteNearestPointBatchEvent` (`:54–99`) and deliberately strips crossings; `TransitHub.JoinCity` replays only that (`:21`).

**Rationale**: The first live batch after a fast unlock has a 70% (MARTA evening) chance of carrying no tones, plus a 0–10 s publish-cadence wait (§2.2, B2). Replaying recent crossings makes fast-click TTFN converge to dwell TTFN (§4.1 fix #4, SC-004).

**Landmine (the reason crossings were stripped)**: The original "rapid pulsing" regression — replaying a burst of crossings on load fired them all at once (`TransitMap.razor.cs:119` comment, `LastBatchCacheCrossingExclusionTests`). Re-solve it: (a) **cap replayed-crossing age** (e.g. only crossings from the last one tick / a few seconds), and (b) respect the animator's current dot positions — the client already derives fire delay from `AlongDistanceM` vs. the dot's animated position (`crossingDelayMsFor`), so age-capped crossings for dots that haven't reached them yet fire correctly; drop crossings for positions the dot has already passed. The `LastBatchCacheCrossingExclusionTests` guard is rewritten from "excludes all crossings" to "includes only crossings within the age cap."

**Scoping win**: `RouteCrossingBatchEvent` is already in the MessagePack `[Union(1)]` contract (`ISignalREvent.cs`), so this is **server-only** — no 3-lane wire deploy, no client/branch coordination (`project_signalr_wire_deploy_constraint` memory).

**Alternatives considered**: A REST crossing snapshot endpoint (removed in the same original fix) — rejected, reintroduces a second code path; the SignalR replay already exists and just needs the crossing payload re-added with an age cap.

---

## D6 — Attach the unlock listener before the Tone import completes

**Decision**: In `attachUnlockGesture`, attach the native `click` listener **synchronously**, before/without awaiting `getTone()`. Do the Tone import inside the handler (or ensure it's already warmed), so the handler runs inside the browser's trusted-gesture window even on a slow connection.

**Rationale**: `attachUnlockGesture` currently `await`s `getTone()` *before* `addEventListener` (`transit-synth.js:342–354`). A user who clicks Enable before esm.sh finishes falls through to the Blazor `UnlockAsync` path, where `Tone.start()` runs outside the gesture-trust window — on iOS Safari the AudioContext stays suspended → **permanent silence** for the session (§2.5, B5).

**Implementation shape**: Register the listener first; inside the handler, `getTone().then(T => T.start())`. If Tone isn't loaded yet, the handler still executes synchronously and the `T.start()` promise resolves inside the user-activation window (Safari honors the activation for the microtask chain of a trusted click). Keep `preload`/`attachUnlockGesture` warming Tone during overlay display so the import is usually already done.

**Landmine**: Do not reintroduce the feature-040 module-instance split — `TransitSynthJsInterop.cs:23` imports the bare path and the dispatcher imports the bare sibling; both share `_unlocked`. This fix is confined to listener ordering inside the already-shared module (verified fixed, do not re-verify — discovery "Known dead ends").

**Alternatives considered**: Rely on the Blazor `UnlockAsync` fallback only — rejected, that's the exact path that fails on iOS. The gesture listener must own the unlock.

---

## D7 — Permanent `[TTFN]` probe

**Decision**: Add the ~15-line `window.TtfnProbe` module-scope object to `transit-synth.js` per §5.1, following the existing `window.MemoryProbe` idiom: mark `unlockAt` in both unlock paths; in `triggerNote`, count `droppedWhileLocked` on the locked-return, set `firstTriggerAt` after the `_audioEnabled` gate, and set `firstAudibleAt` + emit one `[TTFN]` console line (with a per-deploy `version` stamp) after the first `triggerAttackRelease`.

**Rationale**: Turns the 10–15 s anecdote into a tracked per-version metric split into supply (`unlock→trigger`) and build (`trigger→audible`) halves (FR-012), with `droppedWhileLocked > 0` labelling steady-state vs. cold-start (FR-013). Ships **with US1** so US1's forecast is immediately measurable.

**Landmine**: None structural — it's read-only instrumentation. `version` must be set per deploy (commit short-SHA) so lines are comparable.

**Alternatives considered**: The §5.2 paste-in console proxy — kept only as the *baseline-before-probe* tool (documented in quickstart), not shipped, since it underestimates by the decode+IR tail and needs a fresh page load.

---

## D8 — Musical-density health check (telemetry-only)

**Decision**: Define a monitored property over existing `PerCityCycle` telemetry: flag a city when its zero-tone-tick fraction over a rolling hour exceeds ~30% (and, post-D4, when tones/tick falls below ~half its per-city baseline). No new instrumentation beyond D3's counters — this reads data that already ships.

**Rationale**: Catches tone-supply regressions in data that already exists instead of via user anecdotes (§4.3, FR-014). Note **TTC fails this check today** (69% zero ticks from 915 vehicles) — tracked as its own issue, out of scope for this feature's fixes but surfaced by the signal.

**Implementation shape**: A documented query/threshold (mj-data-explorer or a small check), not a new service. Lowest priority (US5); ships after the fixes it monitors.

**Alternatives considered**: A live alerting service — rejected as over-scoped (YAGNI); the threshold + existing telemetry query is enough to detect regressions.

---

## D9 (deferred) — Trigger-point spacing 400 m → 200 m

**Decision**: **Out of scope** for this feature. `TriggerPointGenerator.cs:15` is 400 m while the in-file comments (`:11–13`) still analyze 200 m; halving spacing is a cheap 2× density lever but is deliberately deferred until D4 (direction) and D5 (replay) are measured — fixing suppression may make 400 m musically sufficient, and halving spacing also doubles NYMTA batch volume against the feature-040 5 MB SignalR ceiling and doubles client timer volume (discovery §4.2 #5).

**Rationale**: Re-measure before doubling density system-wide. Recorded here so it isn't silently forgotten; not in the task set.
