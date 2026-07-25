# Session Handoff — NYMTA scale-up: payload, frontend perf, crossing timing

**Branch:** `040-nymta-subway-interpolation`
**Date:** 2026-07-13
**Status:** Multiple changes landed (compile-verified, mostly UN-measured at runtime). One
active bug still being diagnosed: **checkpoint tones/pulses fire before the vehicle dot
visually arrives at the checkpoint.**

> ⚠️ Almost everything below is **compile-verified only**. The live app could not be driven
> from the session (server was intermittently 503 / SignalR crashing). A full browser
> profiling + visual pass at NYMTA scale is REQUIRED before deploying any of this.

---

## THE ACTIVE BUG (unfinished — start here)

### Symptom
Checkpoint tone **and** pulse fire on the correct route the vehicle is heading toward, but
**before the animated dot arrives at the checkpoint**. The crossing is real and correctly
routed — the problem is **timing**: detection leads the animation. Worse with NYMTA subway
data (feature 040's interpolation).

### Ruled out
- **Not** a dispatcher misrouting bug. `crossing-dispatcher.js` captures `const c =
  crossings[i]` per-iteration; each `setTimeout` fires with its own crossing. Correct.
- **Not** a phantom/bogus crossing — user confirmed it's the right route, right checkpoint,
  just early.
- **Not** a `triggerIndex→note` mapping issue — `triggerNote` is a pure function of
  `triggerIndex`/`totalTriggers`; the note matches the checkpoint the server sent.

### The mechanism (hypothesis, NOT yet instrumented/confirmed)
Two independent motion models that are supposed to line up but don't:

1. **Server detection** (`CrossingDetector.Detect`, `Worker.cs:~547`): emits a crossing the
   moment the *snapped/interpolated* along-route distance `currentDistM` passes a trigger
   point. Each crossing gets `OffsetMs = frac × effectiveSpreadMs`, where
   `frac = (tp.AlongDistanceM - windowStart) / windowSpan` — the fraction along the span the
   vehicle traveled *this tick*.
   - `spreadMs` = real elapsed time since prior observation, clamped to `[0, 8000]`
     (`Worker.cs:553-556`).
   - `effectiveSpreadMs` is then STRETCHED for big batches:
     `Min(Max(spreadMs, crossed.Count × 250), spreadMs × 2)` (`CrossingDetector.cs:93-95`).

2. **Client animation** (`vehicle-animator.js`): animates the dot from `priorNearest →
   currentNearest` over `durationMs` (= `now - prior`, i.e. the same ~10s tick). The dot
   *replays* the tick's travel over the next ~10s.

**They should align:** tone should fire when the dot reaches the checkpoint = at the same
fraction of the animation as the checkpoint's fraction along the traveled span. If
`effectiveSpreadMs == durationMs`, `frac × spreadMs` == the dot's arrival time. **They
diverge because:**
   - `effectiveSpreadMs` can be stretched up to `2× spreadMs` → offset keyed to a longer
     span than the animation (would make tones LAG, not lead — so not the lead cause alone).
   - **Most likely lead cause (feature 040):** the subway snap makes `currentDistM` (server,
     interpolated/snapped to `snapValue.Index`) jump *ahead* of the straight
     `priorNearest→currentNearest` tween the client actually animates. The server thinks the
     train passed the checkpoint (its distance estimate leapt there) while the client is
     still gliding the dot along a shorter/slower path. So detection leads arrival.
   - Also: `OnCrossingsAsync` fires at batch receipt (`TransitMap.razor.cs:505`) in parallel
     with `ProcessNearestPointBatchAsync` starting the animation (line 493) — shared start,
     but offsets computed by a different model than the animation duration.

### Chosen next step (per user): DOCUMENT, then instrument, then fix
Do **not** guess-edit the server detector. The agreed approach was **"Instrument first, then
fix"**:

1. Add temporary diagnostics logging, per crossing, BOTH models' numbers:
   - Server side (in `CrossingDetector.Detect` or the `Worker.cs` caller): `vehicleId`,
     `routeJoinKey`, `tp.Index`, `tp.AlongDistanceM`, `windowStart`, `currentDistM`,
     `windowSpan`, `frac`, `offsetMs`, `spreadMs`, `effectiveSpreadMs`.
   - Client side (in `crossing-dispatcher.js` `_fireOne` and/or the animator): the dot's
     animated fraction / distance-along-route at the moment the tone fires, and the
     vehicle's `durationMs`.
2. One test run → compare: does the server's `frac` (checkpoint position in traveled span)
   correspond to where the dot actually is at `OffsetMs`? If the dot is behind, confirm
   whether `currentDistM` ran ahead of the tween (interpolation jump) or whether
   `effectiveSpreadMs ≠ durationMs`.
3. Candidate fixes (decide AFTER instrumenting):
   - **(A) Align spread to animation:** make the client fire each tone at `frac × durationMs`
     (the dot's own arrival time) instead of the server's `OffsetMs`. Requires the server to
     send `frac` (or `AlongDistanceM` + span) instead of/alongside a pre-baked `OffsetMs`.
     Makes tone track the dot by construction. Changes the crossing record contract.
   - **(B) Detect against the tween, not the snap:** make the server compute crossings over
     the same `priorNearest→currentNearest` span the client animates, so `currentDistM`
     can't lead the dot. Touches the 040 interpolation path.
   - **(C) Cap/simplify:** remove the `effectiveSpreadMs` stretch so spread == real elapsed
     == animation duration, and see if the lead is purely the interpolation jump.

**Relevant files for the fix:**
- `src/Server/.../TransitDataWorker/Checkpoints/CrossingDetector.cs` (offset/spread math)
- `src/Server/.../TransitDataWorker/Worker.cs:~537-557` (spreadMs, currentDistM, snap index)
- `src/Client/.../wwwroot/js/crossing-dispatcher.js` (fires tones at OffsetMs)
- `src/Client/.../wwwroot/js/vehicle-animator.js` (the dot's actual motion model)
- `src/ChefKnifeStudios.MartaJazz.Shared/Events/RouteCrossingBatchEvent.cs` (record contract
  — if fix A/B changes what's sent)

---

## CHANGES ALREADY LANDED THIS SESSION (all on this branch)

### 1. SignalR payload — fix the NYMTA >1MB crash (COMPLETE, tests green)
NYMTA (~5k+ vehicles) blew past `MaximumReceiveMessageSize` (1MB) on the worker→hub hop,
dropping the whole batch. Three layered fixes:
- **Field-thinning** `RouteNearestPointRecord` (`Shared/Events/RouteNearestPointBatchEvent.cs`):
  replaced two `DateTime`s (`PriorUtcNow`/`CurrentUtcNow`) with one `int DurationMs`; coords
  rounded to **5 decimals** inline at both construction sites in `Worker.cs`
  (cache/geometry stays full precision). Client reads `r.DurationMs` directly
  (`TransitMap.razor.cs`). ~27% smaller.
- **Ceiling** raised 1MB → **5MB** (`WebAPI/Program.cs`). Load-bearing: thinning alone does
  NOT get NYMTA under 1MB (~1.3MB steady state).
- **MessagePack** replaced JSON on all three hops (worker `SignalRHubPublisher`, server
  `Program.cs`, client `SignalRNotificationService`). Polymorphic `ISignalREvent` handled by
  `[Union(0/1,...)]` + `[MessagePackObject]`/`[property: Key(n)]` on `EventEnvelope` and every
  event/record. **Key/Union ints are now a FROZEN wire contract — never reorder/reuse.**
  Packages: `MessagePack` 3.1.8 in Shared; `Microsoft.AspNetCore.SignalR.Protocols.MessagePack`
  10.0.9 in WebAPI/Worker/Client.Core.
  - Tests: `EventEnvelopeMessagePackTests.cs` (5 unit tests, round-trip contract). Full WebAPI
    suite **59/59 green**. Fixed 6 record-construction sites in 3 existing test files for the
    new shape.
  - **Cutover choice: MessagePack-only server (NO JSON fallback) + "accept the brief
    outage."** See DEPLOY CONSTRAINT below.

### 2. Frontend smoothness — choppy at NYMTA scale (COMPLETE, UNMEASURED)
Root cause: three uncoordinated `requestAnimationFrame` loops each calling `setData` on a
geojson source at 60fps, rebuilding full FeatureCollections + allocating per frame. At ~5k
vehicles + crossing bursts this stalls the main thread.
- **`vehicle-animator.js`**: (A) 15fps render gate — position math still runs every frame,
  only `setData` + feature build gated (`RENDER_INTERVAL_MS = 1000/15`). (B1) persistent
  Feature objects reused + mutated in place (`_featureById`/`_featuresArray`/
  `_featureArrayDirty`), array rebuilt only when vehicle SET changes. Kills ~5k-obj/frame GC.
- **`checkpoint-pulse.js`** + **`checkpoint-trail.js`**: same 15fps render gate. Trail's
  `_buildLineCoords` (whole-polyline walk per trail per frame) now runs at 15fps → free 4× cut.
- **Reserved levers (NOT done):** whole-tick gate (rejected — cuts only cheap math); trail
  `_buildLineCoords` algorithmic fix / concurrent-trail cap; custom WebGL layer (B2) to drop
  `setData` entirely; unifying the 3 RAF loops.

### 3. Persisting-pulse regression (FIXED — was caused by #2's throttle)
The 15fps gate introduced a bug: when the LAST pulse/trail expired on a NON-render frame, the
loop deleted it and stopped WITHOUT a final `setData([])`, freezing its last-drawn feature on
the map. **Fix:** both `checkpoint-pulse.js` and `checkpoint-trail.js` now force one empty
`setData({features:[]})` when the loop stops (`size === 0`). Vehicle animator is immune
(re-arms RAF unconditionally, never stops-on-empty).

### 4. Crossing desync → per-batch JS dispatcher (LANDED, then shaken out — see ACTIVE BUG)
Old: one fire-and-forget C# task PER crossing, each `Task.Delay(OffsetMs)` + 4 sequential
interop hops → thousands of timers + tens of thousands of marshaled calls queued on WASM's
single thread → pulse/note/trail desynced, worse with fleet size.
New: **one interop call per batch** hands the whole batch + 3 gating flags to a new
`crossing-dispatcher.js`, which owns per-crossing `setTimeout(offsetMs)` timers and fires each
crossing's pulse+trail+note TOGETHER. O(1) interop per batch.
- `Map.razor.Helper.cs`: `DispatchCrossingsAsync(crossings, flags)` (lazily imports the
  dispatcher module).
- `TransitMap.razor.cs`: `OnCrossingsAsync` now filters by route, projects to camelCase
  payload, passes flags, one call. **`FireCrossingDelayedAsync` DELETED.**
- **Behavior change (accepted):** mid-spread setting flips no longer honored (flags snapshot
  at receipt, not re-checked at fire time).

#### Two bugs found & fixed during shakeout of #4:
- **Doubled import path:** dispatcher did `import('./_content/.../transit-synth.js')` which
  resolved RELATIVE to its own module URL → doubled path → 404 → silent total failure.
  **Fixed** to sibling `import('./transit-synth.js')`.
- **Module-instance split (audio silent):** `TransitSynthJsInterop` imported transit-synth
  with a `?g=<guid>` cache-buster (unique URL = separate module instance with its own
  `_unlocked` state). The dispatcher's bare-URL import was a DIFFERENT instance whose
  `triggerNote` early-returned on `!_unlocked`. **Fixed** by dropping the guid from
  `TransitSynthJsInterop.cs` so both import the identical URL → same instance → shared unlock.
  (Sibling `./transit-synth.js` from the dispatcher resolves to the SAME absolute URL as C#'s
  `./_content/.../transit-synth.js`, so they now share one instance.)

### ⚠️ TEMP DIAGNOSTICS STILL IN THE CODE — must be removed
`crossing-dispatcher.js` has `console.log('[CrossingDispatcher] ...')` blocks marked
`TEMP DIAGNOSTIC (feature 040)` in `dispatchCrossings` and `_fireOne`. **Strip these before
merge** once the active bug is resolved.

---

## DEPLOY CONSTRAINT (critical — do not merge/deploy without reading)
Wire-format changes (MessagePack, record reshape) span **3 CI lanes**:
- `server.yml` (push `main`) → one container image with BOTH WebAPI hub AND worker →
  server-side hops cut over ATOMICALLY. ✅
- `client.yml` (push `main`) → Blazor WASM to **transitjazz.com**.
- `client-marta.yml` (push **`deploy/marta-jazz`**) → same client to **martajazz.com**,
  sharing the ONE server.

Consequences:
1. Server (MessagePack-only, no JSON fallback) + client are separate lanes on `main`, no
   ordering. During deploy, already-loaded JSON clients are REJECTED at negotiation until
   they refresh → brief outage (user accepted this).
2. **martajazz.com ships from `deploy/marta-jazz`.** A wire change landed only on `main`
   leaves martajazz.com's client on the old format PERMANENTLY (no refresh fixes it). **These
   changes MUST land on BOTH `main` AND `deploy/marta-jazz`.**

Also saved to memory: `project_signalr_wire_deploy_constraint`, `project_040_nymta_payload_reduction`.

---

## VERIFICATION STATUS SUMMARY
| Change | Compiles | Unit tests | Run at scale |
|---|---|---|---|
| Payload thinning + 5MB + MessagePack | ✅ | ✅ 59/59 | ❌ |
| Frontend 15fps throttles + Feature reuse | ✅ (JS, no build) | — | ❌ |
| Persisting-pulse fix | ✅ | — | partial (user saw pulses fire) |
| Crossing dispatcher | ✅ | — | partial (fires, but timing bug) |
| **Crossing timing (tone leads dot)** | — | — | **ACTIVE BUG, unfixed** |

**Before deploy:** browser Performance-panel pass at NYMTA scale during a crossing burst.
Several bugs this session ONLY surfaced at runtime; assume more will.
