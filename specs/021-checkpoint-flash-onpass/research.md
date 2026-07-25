# Research: Checkpoint Flash on Bus Pass & Bus-Visibility Toggle

All open questions were resolved during specification/clarification and planning. This document records the decisions and their rationale. No `NEEDS CLARIFICATION` markers remain.

## R1. Pulse trigger source — reuse existing crossing detection

- **Decision**: Consume the existing per-crossing callback `TransitMap.OnCrossingsAsync(CrossingEventDto[])`, where `CrossingEventDto = (VehicleId, RouteId, TriggerIndex, TotalTriggers)`. This is produced by `checkpoint-tracker.js` (`onTick` → `invokeMethodAsync('OnCrossingsAsync', batch)`), the same path that drives the audio crossing note.
- **Rationale**: FR-005 mandates reuse, not re-computation. The tracker already projects vehicle along-distance, compares against trigger points, and applies a `COOLDOWN_MS = 2000` per checkpoint plus teleport reset (TELEPORT_DIST_M = 2000). Reusing it gives FR-006 anti-flicker for free.
- **Alternatives considered**: A separate proximity pass in the pulse module — rejected (duplicate logic, violates FR-005, risk of divergence from audio timing).

## R2. Rendering approach — dedicated overlay layer vs. animate-in-place

- **Decision**: A dedicated `checkpoint-pulse` GeoJSON source + `checkpoint-pulse-layer` (circle) added **above** the shared `trigger-points-layer`. Each active pulse is one ephemeral Point feature; a single `requestAnimationFrame` loop advances `circle-radius` (grow) and `circle-opacity` (fade) and removes finished features.
- **Rationale**: The resting checkpoint dots all live in ONE shared `trigger-points-layer` (single source, one paint config, features keyed by `{routeId, triggerIndex}`). Animating a single feature inside that shared layer requires per-feature `feature-state` + data-driven expressions for radius/opacity/color and risks coupling the pulse to the resting-dot styling. A separate overlay is fully decoupled: resting dots never mutate (so FR-007 "settle back" is trivially satisfied — the pulse just disappears), concurrent pulses in different route colors are natural (FR-013/FR-003), and basemap-swap recovery is a clean "re-add empty layer + reset state" (FR-012). User confirmed this approach.
- **Alternatives considered**: Animate the existing dot in place via `setFeatureState` per frame — rejected as fiddlier and more error-prone for isolation/concurrency, though it avoids one extra layer.

## R3. Pulse visual form — expanding ring that fades

- **Decision**: A route-colored circle starting at roughly the resting-dot radius (~4px) that grows outward (e.g. to ~22–28px) while `circle-opacity` fades from ~0.6 to 0, over a short duration (~600ms), eased out. Implemented as a filled translucent circle (and/or stroke ring); exact radius/opacity/easing are tuning constants in `checkpoint-pulse.js`.
- **Rationale**: A radar-like "ping" reads unambiguously as motion passing through the checkpoint and is visible even against busy route lines. User confirmed.
- **Alternatives considered**: Quick grow-and-shrink "heartbeat" of the dot itself — rejected by user in favor of the expanding ring.

## R4. Pulse vs. audio coupling — always pulse, but selection-scoped

- **Decision**: The pulse fires on every pass **regardless of the audio mute setting** (`IsAudioEnabled`), subject only to checkpoints being visible. It still honors the **route-selection filter** exactly as the audio path does (only selected routes pulse when a selection is active).
- **Rationale**: User chose "always pulse (independent of audio)" — the visual is its own channel. Honoring the selection filter keeps pulses consistent with Principle IX (emphasize selected, de-emphasize others). Today `OnCrossingsAsync` early-returns when `!_audioEnabled`; the method will be split so the audio branch keeps that guard while the pulse branch does not.
- **Alternatives considered**: Tie pulses to audio-enabled — rejected by user.

## R5. Bus-visibility as a settings toggle (default off)

- **Decision**: Add `IsBusesVisible` (bool, default `false`) to `Settings`, decorated `[Description("SettingBusesVisible")]`. The reflection-driven `SettingsBlade` renders it as a checkbox automatically. Effect propagates via a new `BusVisibilitySettingChangedEventArgs` on the existing `IEventNotificationService`, consumed by `TransitMap`, which calls the existing `Map.SetVehiclesVisibleAsync(visible)`.
- **Rationale**: Mirrors the established 016/017 pattern exactly (audio/checkpoint/street-map are all booleans on `Settings` with `[Description]` resx keys and matching effect events). Default-off satisfies the clarified intent (FR-009a) that pulsing checkpoints carry motion on first view. Persistence and first-render honoring come from `ISettingsService` + reading the setting in `OnAfterRenderAsync` (FR-009c).
- **Alternatives considered**: A fixed hidden behavior (no toggle) — rejected per clarification. A standalone non-settings UI control — rejected (inconsistent with constitution's settings-driven presentation, Principle XII).

## R6. Visibility gating & basemap-swap resilience

- **Decision**: The pulse layer's visibility tracks checkpoint visibility (pulses suppressed when checkpoints are hidden — FR-008); turning checkpoints off clears active pulses. On `setMapStyle`'s `style.load`, re-add the empty `checkpoint-pulse` source/layer and reset the pulse module's active-pulse map (FR-012), mirroring how the `vehicles` layer is already restored.
- **Rationale**: Keeps the pulse consistent with the resting checkpoint dots and avoids orphaned animations across the GIS toggle. Reuses the existing `setMapStyle` snapshot/restore seam.

## R7. Localization

- **Decision**: Add a single resx entry `SettingBusesVisible` = `"Buses"` to `RouteFilterResources.resx` (EN only this iteration). No inline copy.
- **Rationale**: Principle XII / Localization mandates `.resx`-sourced strings. `.es` is deferred consistent with 015/016/017.
