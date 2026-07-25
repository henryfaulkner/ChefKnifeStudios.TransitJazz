# Contract: JS ↔ C# Interop Surface

**Feature**: 009-transit-soundscape
**Date**: 2026-05-22

This feature exposes **no REST endpoints**, **no SignalR events**, and **no static JSON or other public file contracts**. The only contract surface added by this feature is the JavaScript ↔ C# interop boundary between the new `Client.Shared` JsInterop classes and their backing ES modules, plus a single `[JSInvokable]` callback from JS into the consuming page.

This document specifies that boundary so the JS and C# sides can be implemented independently and tested against the same contract.

---

## 1. `ICheckpointTrackerJsInterop` (C#) ↔ `checkpoint-tracker.js` (JS)

### Methods (C# → JS)

#### `Task ConfigureRouteAsync(string routeId, TriggerPoint[] triggerPoints, DotNetObjectReference<object> dotNetRef)`

Push a route's generated trigger points into the tracker and register the page's dotNetRef so the tracker can dispatch crossing batches back.

| Parameter | JS receives | Notes |
|-----------|-------------|-------|
| `routeId` | `string` | The route's `routeShortName`. Idempotent: re-calling for the same route replaces its trigger-point set. |
| `triggerPoints` | `Array<{ index: number, alongDistanceM: number }>` | Sorted ascending by `index`. May be empty (silent route — short polyline). |
| `dotNetRef` | `DotNetObject` | The first call captures this; subsequent calls for additional routes do not need to re-supply it (but may). |

**Idempotency**: yes. Safe to call multiple times per route. Multiple calls with the same `routeId` overwrite the prior trigger-point set and reset any per-route bookkeeping (but **do not** reset per-vehicle `lastTriggeredIndex` — that resets only on vehicle route-change or teleport).

**JS-side behavior**:
- Stores `routeTriggerPoints[routeId] = triggerPoints` (a `Map`).
- Stores the global `_dotNetRef = dotNetRef` on first non-null receipt.
- Hooks into `ChefMapAnimator` exactly once (the first time `configureRoute` is called for any route) by replacing or wrapping `ChefMapAnimator.tick` to emit position events. (Implementation detail: the tracker installs a single `_tickHook` that runs at the end of `tick()`.)

#### `Task ClearAsync()`

Clear all tracker state. Used only on page disposal.

**Idempotency**: yes.

**JS-side behavior**:
- Empties `routeTriggerPoints` and per-vehicle state.
- Releases the `_dotNetRef` (the C# side disposes the underlying `DotNetObjectReference`).
- Detaches the tick hook from `ChefMapAnimator`.

---

### Callbacks (JS → C#)

#### `[JSInvokable] Task OnCrossingsAsync(CrossingEventDto[] crossings)`

Implemented on `TransitMap.razor.cs`. Invoked at most once per animator tick, only when at least one crossing occurred. Batches all crossings detected in the tick.

| Field | JS sends | C# receives | Notes |
|-------|----------|-------------|-------|
| `vehicleId` | `string` | `string` | The vehicle that crossed. |
| `routeId` | `string` | `string` | Route short name. |
| `triggerIndex` | `number` (integer) | `int` | The polyline-vertex index of the fired trigger. |

**Ordering**: crossings within a single batch are ordered by `(routeId, vehicleId, triggerIndex)`. The consuming page iterates in order and calls `ITransitSynthJsInterop.TriggerNoteAsync` for each.

**Backpressure**: none. If a tick produces 50 crossings (extreme edge case), all 50 are dispatched in one batch. The synth's per-instrument polyphony cap prevents audio overload.

**Error contract**: the page handler MUST NOT throw. If `TriggerNoteAsync` throws (e.g., Tone.js not yet initialized), the page swallows the exception with a `console.warn` and continues.

---

## 2. `ITransitSynthJsInterop` (C#) ↔ `transit-synth.js` (JS)

### Methods (C# → JS)

#### `Task UnlockAsync()`

Resolve the browser autoplay restriction by starting the Tone.js audio context. MUST be called from a code path that originated in a user-gesture event (e.g., a click handler), otherwise the browser rejects the unlock.

**Idempotency**: yes. Calling after unlock is a no-op.

**JS-side behavior**:
- Lazy-imports Tone.js if not yet loaded.
- `await Tone.start()`.
- Sets internal `_unlocked = true`.
- Until this returns successfully, `triggerNote(...)` is a silent no-op (it must not throw and must not buffer).

#### `Task<bool> IsUnlockedAsync()`

Returns `true` if the audio context is running. Lets the page hide its "click to enable audio" overlay.

#### `Task TriggerNoteAsync(string routeId, string vehicleId)`

Play one note for `vehicleId` on the instrument assigned to `routeId`.

**Idempotency**: no — each call produces an audible attack. (The crossing-detection algorithm is responsible for not over-triggering; the synth is dumb.)

**JS-side behavior**:
- If `!_unlocked`, return immediately.
- `instrument = instrumentFor(routeId)` (lazy-build first time).
- `pitch = pitchFor(vehicleId)` (lazy-derive first time).
- `instrument.triggerAttackRelease(Tone.Frequency(pitch, 'midi').toFrequency(), '8n')`.

#### `Task DisposeAsync()` (via `IAsyncDisposable`)

Tear down the audio context and instruments. Called when the page is disposed.

---

## 3. Animator integration (JS-only, no C# contract)

`vehicle-animator.js` is edited to emit per-tick position events that `checkpoint-tracker.js` consumes. This is a JS-internal contract, not a JsInterop boundary, but it's documented here for completeness.

After the existing `setData(...)` call in `tick()`, emit:

```js
if (window.CheckpointTracker?.onTick) {
    window.CheckpointTracker.onTick(positionEvents);
}
```

where `positionEvents` is an array of `{ vehicleId, routeId, currIndex }` objects, one per vehicle whose `currentPos` advanced this tick (vehicles in `idle` phase or whose position is unchanged from the prior tick are omitted to keep the array small).

`currIndex` is computed inside the animator using its existing `findNearestIndex(routeData.coords, state.currentPos)`. The tracker therefore does not need to do its own nearest-index search — it consumes the animator's authoritative value.

**Contract guarantees** the animator gives the tracker:
- `currIndex` corresponds to the animator's `routeData.coords` array length at the time of emission. (The tracker MUST tolerate the route's trigger-point set being absent or shorter — silently skip those vehicles.)
- `routeId` is the route the animator is currently animating the vehicle along (matches `state.routeId` after any route-transfer teleport).
- The event is emitted at most once per vehicle per tick.

---

## Out-of-scope (explicitly NOT in this contract)

- No new SignalR event types.
- No new REST endpoints.
- No new shared-project types.
- No CSS / DOM contracts beyond the "click to enable audio" hint overlay, which is a single transient element with no styling contract.
- No public JS API on `window.*` beyond `window.CheckpointTracker` and `window.TransitSynth` namespaces — and even those are implementation details, not promises.
