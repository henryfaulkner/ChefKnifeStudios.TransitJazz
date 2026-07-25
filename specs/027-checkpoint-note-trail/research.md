# Phase 0 Research: Checkpoint Crossing Trail

All unknowns from the Technical Context are resolved below. Each item follows Decision / Rationale / Alternatives.

## R1. Where does the trail attach to the existing crossing pipeline?

**Decision**: Attach in `TransitMap.OnCrossingsAsync` (in `Client.WebApp/Pages/TransitMap.razor.cs`), immediately adjacent to the existing `PulseCheckpointAsync` call, inside the **same `if (_checkpointsVisible && _map is not null)` block**. The audio path (`if (_audioEnabled) … TriggerNoteAsync`) stays a separate, independent block.

**Rationale**: The crossing detection already runs end-to-end: `checkpoint-tracker.js` → `OnCrossingsAsync(CrossingEventDto[])`. That method already separates the *visual* gate (`_checkpointsVisible`, which drives the pulse) from the *audio* gate (`_audioEnabled`). Per the resolved spec clarification, the trail is a visual event tied to the crossing and must fire even when audio is muted/locked — so it belongs with the pulse (visibility-gated), not with the note (audio-gated). This is the minimal, lowest-risk integration point and reuses the existing route-filter scoping (`effectiveIds`) already applied at the top of the loop.

**Alternatives considered**:
- *Fire the trail from inside `transit-synth.js` next to `triggerAttackRelease`* — rejected: that path is gated behind `_unlocked` and `_audioEnabled`, so the trail would not fire when muted/locked (violates FR-001).
- *New JSInvokable / new event-bus message* — rejected: unnecessary; `OnCrossingsAsync` already carries `routeId`, `vehicleId`, `triggerIndex`, `totalTriggers` and runs on the visibility-gated path.

## R2. How is the trail layer implemented on the map?

**Decision**: A new ES module `checkpoint-trail.js`, a structural clone of `checkpoint-pulse.js`: one GeoJSON source (`crossing-trail`) + one MapLibre `line` layer (`crossing-trail-layer`), a module-level `Map` of active trails keyed `"{vehicleId}::{triggerIndex}::{startTimeMs}"`, and a single shared `requestAnimationFrame` loop that rebuilds the FeatureCollection each tick via one `source.setData(...)`. Public API mirrors the pulse module: `ensureLayer(map)`, `start(map, routeId, vehicleId, triggerIndex, anchorCoord, color, speedMps, durationSec)`, `reset(map)`, `setVisible(map, visible)`. Lazily imported in `map-interop.js` via the existing `_getCheckpointTrail()` idiom (copy of `_getCheckpointPulse()`).

**Rationale**: `checkpoint-pulse.js` already proves this exact pattern works for a transient, route-colored, RAF-animated overlay that respects checkpoint visibility and survives style swaps. Cloning it keeps the trail consistent with established code, keeps it to one `setData` per frame (60 fps budget, SC-002), and gives stacking for free (multiple active entries in the map render as multiple line features; a later crossing for the same bus is a new keyed entry rendered in the same layer — FR-007). MapLibre draws later features in a source above earlier ones, satisfying "supersedes by being on top."

**Alternatives considered**:
- *Reuse the pulse module/source* — rejected: pulse is a `circle` geometry with its own paint; a `line` trail needs its own source/layer and its own per-feature growth math. Mixing them complicates both.
- *One layer per trail* — rejected: layer churn is expensive and unnecessary; one source with N features is the documented MapLibre approach (and the vehicle animator's approach).

## R3. How does the head advance forward along the route?

**Decision**: Each RAF tick computes elapsed fraction `t = clamp((now - startTimeMs) / (durationSec*1000), 0, 1)`. Current trail length `L(t) = finalLengthM * t`. The head position is found by walking the route polyline forward from the anchor's along-route distance by `L(t)` metres, using `ChefMapAnimator.routeGeometry[routeId]` (`coords` + `cumDist`) — the same cumulative-distance arrays the animator already builds. The rendered LineString = the polyline vertices between the anchor distance and the head distance, with both endpoints interpolated within their segments. When `t >= 1`, the trail entry is deleted (immediate removal, FR-004 / SC-002).

**Rationale**: Reuses geometry already loaded for every route (`ChefMapAnimator.loadRouteGeometry` is called in `addAllRoutes`), so no new fetch/projection. Walking `cumDist` forward is O(segments traversed) and bounded by `MAX_LEN_M` (600 m) regardless of route length. The anchor's along-route distance is derived from the trigger point's `alongDistanceM` property, which `addTriggerPointMarkers` already stores on each trigger feature — so the tail is exactly the projected checkpoint coordinate (FR-002), no re-projection needed.

**Alternatives considered**:
- *Straight-line head extrapolation by bearing* — rejected: FR-002 requires the head to follow the route, not a straight line; the route polyline is available, so use it.
- *Re-project the checkpoint coordinate at trail time* — rejected: the projected along-route distance is already precomputed in `tp.alongDistanceM`; re-projecting wastes work and risks drift from the pulse's anchor.

## R4. Final length, speed, and the tuning constants

**Decision**: Centralize all five constants at the top of `checkpoint-trail.js`:

| Constant | Value | Use |
|---|---|---|
| `MIN_SPEED` | 2.0 | Floor applied to the speed input so a stopped bus still yields a visible mark |
| `MAX_SPEED` | 30.0 | Ceiling clamp on the speed input |
| `LENGTH_SCALE` | 1.0 | Multiplier on the computed length |
| `MAX_LEN_M` | 600 | Hard cap on final length |
| `TRAIL_WIDTH` | 12 | `line-width` in px (matches bus dot diameter) |

Final length: `finalLengthM = min(MAX_LEN_M, clamp(speedMps, MIN_SPEED, MAX_SPEED) * durationSec * LENGTH_SCALE)`.

**Rationale**: Matches the spec's tuning table verbatim and FR-003/FR-010. Clamping speed to `[MIN_SPEED, MAX_SPEED]` both guarantees a non-zero mark for stopped buses (SC-004) and bounds runaway lengths before the `MAX_LEN_M` cap. Constants live in one place (FR-010) so tuning is a one-line edit.

**Alternatives considered**:
- *Derive width from the live bus-dot radius at runtime* — rejected: the bus dot radius is a fixed paint value; a hard `TRAIL_WIDTH = 12` constant is simpler and is exactly the spec requirement (SC-007). (Bus dot is rendered with `circle-radius` ~6 → 12px diameter; 12px line width matches.)

## R5. Bus speed source (must work when audio is muted/locked)

**Decision**: Use `ChefMapAnimator.vehicles[vehicleId].empiricalSpeed` (metres/second), falling back to `.speed` (GTFS-RT field), then to 0 (the `MIN_SPEED` floor then applies). The C# side reads it via a tiny interop getter, or the JS `start()` reads it directly from `ChefMapAnimator` given the `vehicleId` — preferred, since `checkpoint-trail.js` already runs in JS next to the animator.

**Rationale**: `computeEmpiricalSpeed` already maintains a stable, route-aware speed per vehicle, updated every batch and independent of audio. Reading it inside the trail module avoids a round-trip and keeps the C# call signature small. This is the same speed the extrapolator trusts, so the trail's length reads as "how fast this bus is going."

**Alternatives considered**:
- *Pass speed from C#* — rejected as the primary path: C# does not currently hold per-vehicle empirical speed; plumbing it up is more code than reading `ChefMapAnimator.vehicles[vehicleId]` in JS. (C# still passes `vehicleId` so the module can look it up.)

## R6. Note duration in seconds, available regardless of audio state

**Decision**: Extract a pure helper in `transit-synth.js`: `export function durationSecondsFor(vehicleId)`. It performs the **same** deterministic selection used today — `durations[djb2(String(vehicleId)) % durations.length]` — but returns the value in **seconds** by mapping the Tone.js note string at the fixed default tempo. The default Tone Transport tempo is 120 BPM (unchanged in this app), so quarter `4n` = 0.5 s, eighth `8n` = 0.25 s, dotted-eighth `8n.` = 0.375 s. `triggerNote` is refactored to call the same selection so the audible note and the trail always agree on duration. The helper does **not** require `_unlocked` and does **not** touch the AudioContext, so the trail can read a correct duration while muted or locked.

**Rationale**: FR-001/FR-002 require the trail to grow over the *note's* duration even when no sound plays. Today that duration only exists transiently inside `triggerNote` behind the `_unlocked` gate. Extracting a pure, audio-independent selector is the cleanest way to (a) keep audible and visual durations identical and (b) make duration available on the muted/locked path. Because the palette durations and tempo are constants, the seconds mapping is a fixed lookup — no Tone.js call needed.

**Alternatives considered**:
- *Call `Tone.Time(str).toSeconds()`* — rejected: requires Tone loaded/awaited and is overkill for three constant note values; a literal lookup (`{'8n':0.25,'8n.':0.375,'4n':0.5}`) is deterministic and synchronous, and avoids importing Tone on the muted path.
- *Hardcode a single fixed trail duration* — rejected: breaks the "trail lasts as long as the note" contract and decouples it from the per-vehicle deterministic duration (FR-002, AC#4).

## R7. Visibility gating and immediate clear

**Decision**: The trail reuses the existing checkpoint-visibility plumbing. `start()` is only ever called from the `_checkpointsVisible`-gated block (FR-006 suppression). For the "clear active trails on toggle off" requirement, `checkpoint-trail.setVisible(map, false)` calls `reset(map)` (cancels RAF, clears active map, empties the source) — identical to `checkpoint-pulse.setVisible`. The existing `Map.SetCheckpointVisibilityAsync` path is extended to also drive the trail's `setVisible`, so one toggle controls pulse + trail together.

**Rationale**: Mirrors the pulse module exactly (`checkpoint-pulse.setVisible` already `reset()`s when turning off). One visibility source of truth, zero new settings (Principle XII, Non-Requirements).

**Alternatives considered**:
- *Separate trail visibility flag* — rejected: spec says trails are gated by checkpoint pulse visibility, no new control (Non-Requirements).

## R8. Surviving a basemap style swap (Principle VII)

**Decision**: Register the `crossing-trail` source + `crossing-trail-layer` in the `setMapStyle` restore handler (both the primary `style.load` restore and the timed-fallback restore), using `checkpoint-trail.ensureLayer(map)` after the style reloads — exactly as `_getCheckpointPulse().then(p => p.ensureLayer(map))` is called on initial map load. Active trails do **not** need to be preserved across a swap (they are sub-second ephemera); only the empty source/layer must be re-created so the next crossing renders.

**Rationale**: Principle VII mandates data layers persist (or are re-added without re-fetch) across basemap swaps. The trail carries no fetched data — re-adding an empty source/layer is sufficient and matches how the pulse layer is handled (re-`ensureLayer` on load).

**Alternatives considered**:
- *Let the trail layer silently disappear after a swap* — rejected: violates Principle VII (data/overlay layers must survive the swap); the next crossing would no-op because the source is gone.

## Cross-cutting confirmations

- **Color (FR-005)**: reuse `ChefMap._routeColorsByRouteId[routeId] || '#facc15'` — the same warm-yellow fallback the pulse already uses. No new color logic.
- **Anchor coordinate (FR-002)**: reuse `ChefMap._triggerPointFeatures[routeId]` to find the trigger feature by `triggerIndex`; its `geometry.coordinates` is the tail, its `properties.alongDistanceM` is the along-route start distance. Identical lookup to `pulseCheckpoint`.
- **No localization impact (Principle XII)**: the feature adds no user-facing strings, so no `RouteFilterResources.resx` change is required.
