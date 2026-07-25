# Contract: Unlock-Time Audible Feedback + Warming + Robust Gesture (US1, US4 / D1, D2, D6)

Governs `Client.Shared/wwwroot/js/transit-synth.js` (and, if driven from C#, `TransitSynthJsInterop.cs`). Client-only.

## D1 — Master bus + noise bed at unlock (FR-001, FR-002)

- `unlock()` and the `attachUnlockGesture` handler MUST build the master bus (`getMasterBus(T)`) after `T.start()` succeeds, so the pink-noise bed starts.
- The bed MUST start **only if `_audioEnabled`** — identical gating to the existing `getMasterBus` (`if (_audioEnabled) noise.start()`) and `setAudioEnabled`. When the persisted setting is muted, no bed and no welcome sound plays until audio is enabled.
- `getMasterBus` stays idempotent (`if (_masterBus) return`), so the later first-note path reuses the same bus — no double build, no RAM change.
- **Result**: audible ambient output within <1 s of the unlock gesture whenever audio is enabled (SC-001), independent of when the first crossing arrives.

## D2 — Warm the prod samplers at unlock (FR-003, FR-004)

- After unlock, kick off `instrumentForSlot(i)` for exactly the slots covered by `PROD_INSTRUMENTS` (the 3 fixed voices) — NOT the full 6-voice PALETTE.
- Fetch + decode + `reverb.generate()` run pre-first-crossing so the first `triggerNote` finds a resolved cache entry → `trigger→audible ≈ 0 ms` (SC-005).
- **RAM invariant (FR-004)**: warming is bounded to the 3 fixed slots → flat cost regardless of route/vehicle count. Warming the full PALETTE or keying by routeId is FORBIDDEN (the 1.2–1.7 GB regression).
- **Evicted-before-first-note edge case (FR-003, spec edge "Prepared sound engine evicted before first note")**: a warmed slot MAY later be evicted by `EvictInactiveRouteAudioAsync` (`TransitMap.razor.cs` ~:526, after 3 absent batches) before its route's first note. This is explicitly **resolved by accepting the transparent rebuild** — `instrumentForSlot` rebuilds on the next `triggerNote`, so the first note is still correct, merely without the warm-time saving for that one quiet route (equals pre-fix behavior). No pin mechanism is required. The rebuild path MUST be code-commented so a reviewer sees the edge is handled, not overlooked.
- Warming MUST be fire-and-forget (not awaited by the unlock path) so it never delays overlay dismissal (Principle XI).

## D6 — Attach gesture listener before Tone import (FR-010, FR-011)

- `attachUnlockGesture` MUST `addEventListener('click', handler)` **synchronously**, without awaiting `getTone()` first.
- Inside the handler: `getTone().then(T => T.start()).then(() => { _unlocked = true; buildBus+warm })`. The `T.start()` call resolves within the trusted-gesture microtask chain even if Tone was still importing — so a fast click never falls back to the non-gesture Blazor `UnlockAsync` path that leaves iOS AudioContext suspended.
- MUST NOT reintroduce the feature-040 module-instance split: the module is imported by the bare path in both `TransitSynthJsInterop.cs:23` and the dispatcher; this change is listener-ordering only, inside the shared instance.
- **FR-011 running-state**: after the handler resolves, the AudioContext MUST reach `running`. This MUST be programmatically confirmed (`Tone.getContext().rawContext.state === 'running'`), not only ear-checked — the iOS failure mode FR-011 names is the context staying `suspended` while the app appears unlocked.

## D7 — TtfnProbe hooks (folded in with US1)

- Add `window.TtfnProbe` (see data-model §4). Mark `unlockAt` in BOTH unlock paths.
- In `triggerNote`: `if (!_unlocked) { _ttfn.droppedWhileLocked++; return; }`; after the `_audioEnabled` gate `firstTriggerAt ??= performance.now()`; after `triggerAttackRelease` set `firstAudibleAt` once and emit the `[TTFN]` line + `performance.mark/measure`.
- `version` set per deploy.

## Interop (if C#-driven)

If warming is initiated from C# rather than purely inside the JS unlock handler, add `WarmSamplersAsync()` to `ITransitSynthJsInterop`/`TransitSynthJsInterop` following the existing method pattern (try/catch + `LogError`), calling a new exported `warmProdSamplers()`. Preferred: keep warming inside the JS unlock handler (fewer interop hops, runs closest to the gesture) and expose nothing new on the C# interface.

## Verification

- SC-001: audible at unlock (ear + noise node `state === 'started'`), silent when muted.
- SC-005: `[TTFN]` `trigger→audible` ≈ 0 ms once warmed.
- SC-006 / FR-011: throttled-network / iOS trial — click Enable before Tone finishes → audio still becomes audible (no permanent silence) AND `Tone.getContext().rawContext.state === 'running'` is confirmed after unlock (not just an ear-check).
- FR-004: browser RAM stays in the known 3-slot footprint (MemoryProbe), not the 1.2–1.7 GB regression band.
