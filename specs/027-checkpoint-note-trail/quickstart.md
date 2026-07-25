# Quickstart: Checkpoint Crossing Trail

Manual verification for feature 027. The project has no automated JS/Blazor UI test harness (consistent with 016/017/021), so acceptance is verified by running the app and observing the map.

## Prerequisites

- Run the app via the Aspire AppHost (or the WebApp + WebAPI + Worker), so live bus positions stream and buses actually cross checkpoints.
- Have at least one route with active buses visible on the map.
- Ensure checkpoint pulses are **visible** (the existing checkpoint-visibility setting ON) for the trail-positive cases.

## Build / run

```powershell
dotnet build ChefKnifeStudios.TransitJazz.sln
# then launch the AppHost (or WebApp) as usual for this project
```

JS modules are static assets in `Client.Shared/wwwroot/js`; a rebuild + hard refresh (the modules are cache-busted on import) picks up `checkpoint-trail.js` and the edited `map-interop.js` / `transit-synth.js`.

## Acceptance walkthrough

Map each step to the spec's Acceptance Criteria (AC) / Success Criteria (SC).

### 1. Trail appears on crossing (AC#1, SC-001)
- With checkpoints visible and audio unlocked, watch a bus approach and cross a checkpoint.
- **Expect**: a route-colored line appears anchored at the checkpoint and grows forward along the route, alongside the existing pulse ring + dot.

### 2. Head grows forward along the route (AC#2)
- Observe a single crossing closely.
- **Expect**: the tail stays pinned at the checkpoint; the head advances **along the route polyline** (not a straight line), reaching full length as the note ends.

### 3. Faster bus → longer trail (AC#3, SC-003)
- Compare a fast-moving bus vs. a slow one crossing checkpoints (notes are equal-ish in duration; duration is per-vehicle deterministic).
- **Expect**: the faster bus's final trail is visibly longer; no trail exceeds ~600 m (`MAX_LEN_M`).

### 4. Disappears immediately on note end (AC#4, SC-002)
- Watch the end of a trail's life.
- **Expect**: the trail vanishes completely within a frame of the note ending — no fade-out, no lingering segment.

### 5. Hidden checkpoints → no trail (AC#5, SC-005)
- Turn checkpoint visibility OFF. Let buses cross checkpoints.
- **Expect**: no trails (and no pulses) appear.

### 6. Clear active trails on toggle off (FR-006, SC-005)
- With a trail actively growing, toggle checkpoint visibility OFF.
- **Expect**: the active trail disappears immediately.

### 7. Muted audio still shows the trail (AC#6 corrected, FR-001)
- Keep checkpoints visible. **Mute** audio (settings) — or test before unlocking audio.
- Let a bus cross a checkpoint.
- **Expect**: the trail still grows and the pulse still fires; **no note** plays. (This is the resolved clarification: the trail is a visual event tied to the crossing, not to whether sound is heard.)

### 8. Two routes, simultaneous crossings (AC#7, SC-006)
- Find a moment where buses on two different routes cross checkpoints together.
- **Expect**: two trails, each in its own route's color, growing independently with no flicker or color bleed.

### 9. Width matches the bus dot (AC#8, SC-007)
- Compare the trail's line thickness to a bus marker's diameter side-by-side.
- **Expect**: the 12px trail width visually matches the bus dot.

### 10. Survives a basemap swap (Principle VII)
- Toggle the GIS basemap setting (streets ↔ blank dark) after at least one crossing.
- Then let another bus cross a checkpoint.
- **Expect**: routes/buses/checkpoints persist across the swap, and the **next crossing still renders a trail** (the empty trail layer was re-added on `style.load`).

## Regression checks

- Existing checkpoint **pulse** still fires exactly as before (unchanged module).
- Audible **notes** still play with the same per-vehicle duration (the synth now selects duration via the shared `durationSecondsFor` logic — confirm pitch/instrument/cadence are unchanged).
- 60 fps map animation is preserved during dense crossings (no jank from trail ticks).

## Where to look if something's off

| Symptom | Likely cause |
|---|---|
| No trail, but pulse works | `startCrossingTrail` not wired in `OnCrossingsAsync`, or `ChefMapAnimator.routeGeometry[routeId]` missing |
| Trail straight, not along route | head walk not using `routeGeometry.cumDist`; verify R3 logic |
| Trail never disappears | `t>=1` delete not removing the entry; or `durationSec` is 0/NaN |
| Wrong color / always yellow | `_routeColorsByRouteId[routeId]` empty → falling back to `#facc15` |
| Trail vanishes after GIS toggle and never returns | trail `ensureLayer` not added to `setMapStyle` restore blocks |
| Trail missing when muted | duration or trail path incorrectly gated behind `_audioEnabled`/`_unlocked` |
