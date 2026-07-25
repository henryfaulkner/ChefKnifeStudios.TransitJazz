# Quickstart: Checkpoint Flash on Bus Pass & Bus-Visibility Toggle

Manual verification for feature 021. Frontend-only; run the app and observe. No automated UI tests exist in this project (consistent with 015–020).

## Prerequisites
- Run the solution (Aspire AppHost or the WebApp + WebAPI + Worker) so live `RouteNearestPointBatchEvent` batches flow and buses animate along routes.
- In Settings, turn **Checkpoints** ON for the pulse tests (the resting dots must be visible for pulses to show — FR-008).

## A. Checkpoint pulse on pass (P1)

1. Watch a route with an active, moving bus. As the bus reaches a checkpoint dot, confirm an **expanding ring** appears at that checkpoint, grows outward, fades, and disappears (~0.6s). → FR-001/FR-002, SC-001.
2. Confirm the ring's **color matches that route's line color**. Compare against another route's checkpoint — colors differ per route. → FR-003, SC-002.
3. Watch two routes whose buses pass checkpoints near-simultaneously: both pulse **independently**, each in its own color. → FR-013, SC-006.
4. After any pulse, the resting checkpoint dot looks exactly as before (no dot left enlarged/brightened). Observe 10+ minutes of live traffic: **no stuck checkpoints**. → FR-007, SC-003.
5. Pass a route that has no defined color (if any): its pulse uses the default amber (`#facc15`) rather than not pulsing. → FR-004.
6. Turn **Checkpoints** OFF: passes produce **no pulse** (nothing visible). Turn back ON mid-traffic: dots reappear at rest; no replay/orphan animation. → FR-008.
7. With **Audio** muted, confirm checkpoints **still pulse** on passes. → "always pulse" decision; pulse is audio-independent.
8. Activate a **route filter** (select one route): only the **selected** route's checkpoints pulse; non-selected routes do not. Clear selection: all pulse again. → Principle IX consistency.
9. Toggle the **Street map** (GIS) setting while traffic flows: after the basemap swaps, pulses **continue** with correct per-route colors. → FR-012.

## B. Bus-visibility toggle (P2)

1. **Fresh load** (clear local storage / first run): the **Buses** toggle is **OFF** and no bus markers are drawn; routes and checkpoints are visible. → FR-009a, SC-004.
2. Toggle **Buses ON**: bus markers appear **immediately**, no reload. → FR-009b.
3. Toggle **Buses OFF**: markers hide **immediately**, no reload. → FR-009b.
4. Set **Buses ON**, **reload** the app: buses are visible from first render. Set OFF, reload: hidden from first render. → FR-009c, SC-004a.
5. With **Buses OFF**, confirm a bus passing a checkpoint **still pulses** the checkpoint (motion still drives pulses though the bus is undrawn). → FR-010.
6. Toggle the **Street map** setting: bus visibility matches the current **Buses** setting after the swap (not forced back on). → FR-011.

## Pass criteria
All checks in A and B behave as described; no checkpoint is left in a non-resting state; no console errors from `pulseCheckpoint` / `SetVehiclesVisibleAsync`.
