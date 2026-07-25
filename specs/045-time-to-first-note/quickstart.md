# Quickstart: Time-to-First-Note

Per-user-story verification. Ship in priority order; **check each fix's forecast against a recorded measurement before stacking the next** (FR-015, discovery §4.1).

## 0. Baseline the CURRENT prod build first (no redeploy)

Before shipping anything, capture the starting number so every fix is comparable.

1. Open the deployed site (`#marta`), open DevTools console, paste the §5.2 proxy (from `docs/TIME_TO_FIRST_NOTE_DISCOVERY_DOCUMENT.md`) **before** clicking Enable.
2. Run both scenarios ≥10× each: **fast-click** (Enable the instant it renders) and **dwell** (wait ≥30 s, then Enable). Record median + p90.
3. Also record telemetry baseline: `query_telemetry` dataset `telemetry`, `event_type='PerCityCycle' AND city_name='marta'` for the day — compute tones/tick avg and zero-tick fraction (§7 Step 1; read the JSON `-Encoding UTF8`, split on `│`).
4. Write both into the §5.4 benchmark log table.

---

## US1 + US5-probe (P1, client) — immediate audible feedback + measurement

**Ship together.** Files: `transit-synth.js` (+ optionally `TransitSynthJsInterop.cs`).

Verify:
1. **Audible at unlock (SC-001)**: fresh session, audio enabled, click Enable → soft ambient bed audible **within 1 s**, before any transit note. Confirm `window.TransitSynth`… (the noise node) — the bed is running.
2. **Mute respected (FR-002)**: set Audio muted in Settings, reload, unlock → **silence** until you re-enable audio; then the bed starts.
3. **First note instant (SC-005)**: after unlock, when the first crossing fires, the `[TTFN]` line shows `trigger→audible ≈ 0 ms` (samplers were warmed).
4. **RAM flat (FR-004)**: `window.MemoryProbe` stays in the 3-slot footprint — NOT the 1.2–1.7 GB band. Confirm only the 3 prod slots warmed (not 6).
5. **Probe (FR-012/013)**: `[TTFN] v=… unlock→trigger=… trigger→audible=… total=… droppedWhileLocked=…` prints once on first note; `droppedWhileLocked>0` ⇒ dwell, `0` ⇒ cold-start.
6. **Overlay still snappy (Principle XI)**: warming is fire-and-forget; the overlay dismisses instantly on Enable.

Forecast to hit: `trigger→audible → ~0 ms`; audible output at t=0 regardless of TTFN.

---

## US2 (P1, worker) — counters FIRST, then reverse-direction fix

**Two deploys.** Files: `CrossingDetector.cs`, `Worker.cs`, `Logging/TelemetryEvent.cs`, `tools/telemetry-mcp/.../validate.go` + `validate_test.go`, `TelemetryEventSchemaTests.cs`, `CrossingDetectorTests.cs`.

### Deploy 2a — suppression counters only

1. Add the four `crossings_suppressed_*` columns end-to-end (see `contracts/telemetry-schema.md`). Run `go test ./...` in `tools/telemetry-mcp` and the Worker.Tests — all green.
2. Deploy; after a few minutes query `PerCityCycle` for marta. Verify the **invariant (SC-007)**: `first_seen + delta_leq0 + teleport + transfer + emitting-vehicles == vehicles that ran detection` — no unexplained remainder.
3. Read which path dominates (expect `delta_leq0` to be huge, carrying the reverse-direction vehicles pre-fix). This is the attribution that justifies 2b.

### Deploy 2b — reverse-direction emission

1. Implement the direction state + reverse-window emission (`contracts/crossing-suppression-counters.md`, data-model §2). `CrossingDetectorTests` green (forward regression, reverse emits descending, delta==0, teleport, turnarounds).
2. Deploy; query `PerCityCycle` marta over a comparable evening window.
3. **Verify with tones/tick avg (SC-002), NOT zero-tick fraction** — zero-tick stays ~70% (feed cadence). Expect tones/tick ~1.14 → **~2.3** (≥2×). `delta_leq0` count should drop by roughly the reverse-fleet share.
4. Dwell-scenario `[TTFN]` `unlock→trigger` median should fall toward <5 s (SC-003) as density rises.

Forecast to hit: emitting fleet ≈ ×2, tones/tick ≥2×; if it misses, re-diagnose before US3.

---

## US3 (P2, server) — replay recent crossings on join

**Server-only** (no wire/client deploy). Files: `ILastBatchCache.cs`, `TransitHub.cs`, `LastBatchCacheCrossingExclusionTests.cs` (rewritten).

Verify:
1. `LastBatchCacheCrossingExclusionTests` rewritten to the age-cap guarantee (within cap included, older excluded, empty → no crossing envelope, ordering preserved) — green.
2. **Fast-click converges (SC-004)**: re-run the §5.3 fast-click vs. dwell protocol. Fast-click median should now be within a small margin of dwell median (previously worse by 5–15 s).
3. **No rapid pulse (FR-009)**: ear check on load — recent crossings play spread against the animated dots, not a burst. Confirm the age cap drops crossings whose dot already passed.

Forecast to hit: cold-start penalty ~5–15 s → ~0; fast-click TTFN converges to dwell.

---

## US4 (P2, client) — robust unlock gesture

Files: `transit-synth.js` (`attachUnlockGesture` ordering).

Verify:
1. **Slow-load click (SC-006)**: throttle network (DevTools "Slow 3G"), reload, click Enable the instant it renders (before esm.sh Tone finishes). Audio MUST still unlock and become audible — no permanently silent session.
2. **iOS Safari**: same test on a real iPhone / iOS simulator — AudioContext reaches `running`, subsequent notes audible.
3. **No 040 regression**: confirm `_unlocked` still propagates to the dispatcher (notes fire) — bare-path import unchanged.

Forecast to hit: 0 permanent-silence failures across the trial.

---

## US5-health (P3) — density health check

1. Over a rolling hour of `PerCityCycle` per city, compute zero-tone-tick fraction; confirm the **>30%** threshold flags an unhealthy city.
2. Confirm **TTC flags today** (69% / 915 vehicles) — expected; tracked separately, not fixed here.

---

## Regression landmines checklist (discovery §4.2 — check in review)

- [ ] Warming limited to 3 fixed prod slots (RAM regression).
- [ ] Noise bed + warming gated on `_audioEnabled` (mute wins).
- [ ] New telemetry columns added to `validate.go` allow-list + tests (frozen contract).
- [ ] Replay age-capped + respects dot positions (rapid-pulsing regression).
- [ ] Reverse fix verified by tones/tick, not zero-ticks; teleport guard intact for out-and-back.
- [ ] No feature-040 module-instance split reintroduced (bare-path import).
