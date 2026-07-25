# Quickstart — Route Audio Checkpoints

**Feature**: `008-route-audio-checkpoints`
**Purpose**: Manual verification protocol for the POC. Use this as the implementation acceptance gate — every section corresponds to one or more Success Criteria from `spec.md`.

---

## Prerequisites

- Branch `008-route-audio-checkpoints` checked out and built clean (zero new warnings).
- `wwwroot/checkpoints.json` exists with at least three checkpoints across at least two active routes. See `contracts/checkpoints-json.md` for the schema.
- A device with audio output (headphones or speakers) and an unmuted browser tab.
- Live MARTA data is reachable (the AppHost orchestrates the WebAPI + Worker stack; vehicles will start arriving within ~10 s of the worker connecting).

---

## Start the stack

```powershell
dotnet run --project src/ChefKnifeStudios.TransitJazz.AppHost
```

Wait for the Aspire dashboard to come up. Confirm:

- `Server.WebAPI` is running.
- `Server.TransitDataWorker` is running and logging GTFS-RT polls.
- `Client.WebApp` is reachable on its assigned localhost port (the Aspire dashboard shows the URL).

Open the WebApp URL in a Chromium or Firefox browser. Navigate to `/transit-map`.

---

## Test 1 — Audio fires on a visible crossing (SC-001)

1. Click anywhere on the page once to satisfy the browser autoplay gesture requirement.
2. Locate one of the checkpoint markers (small amber dot on a route line — distinct from green vehicle dots).
3. Wait for a vehicle to animate along the route toward that checkpoint.
4. **Expected**: As the vehicle visually reaches the checkpoint, you hear a short pitched note (~200 ms). Within ~600 ms after the note, the marker pulses (radius grows then returns).
5. **Expected**: Browser console shows `[CheckpointAudio] fired vehicleId=<id> checkpointId=<id> midi=<n>` once.

Pass condition: ≥ 9 out of 10 observed crossings during a 30-minute session produce an audible note within 2 s of the visible crossing.

---

## Test 2 — Cooldown holds for an oscillating vehicle (SC-002, FR-003, FR-011)

1. Identify a checkpoint where a vehicle has recently stopped, stalled, or oscillated (look for buses that aren't moving much on the live map).
2. Watch and listen for 10 minutes near that checkpoint.
3. **Expected**: For each `(vehicle, checkpoint)` pair, no more than one fire occurs per cooldown window (10 s).
4. Verify in console: count `[CheckpointAudio] fired` log lines for the pair against wall time.

Pass condition: zero observations of two fires within 10 s for the same vehicle/checkpoint pair.

---

## Test 3 — Pre-gesture autoplay handling (SC-005, FR-008)

1. Hard-reload the page (`Ctrl+Shift+R`). **Do not click anywhere**.
2. Wait for a vehicle to traverse a checkpoint (may take up to 60 s).
3. **Expected**: No browser console errors. The console DOES log `[CheckpointAudio] fired (audio suppressed: pre-gesture) vehicleId=<id> checkpointId=<id>`. The marker pulse animation still plays.
4. Now click anywhere on the page.
5. **Expected**: On the next crossing, audio plays normally (per Test 1) and the console log no longer carries the suppression suffix.

Pass condition: zero unhandled errors in the console before or during the first gesture.

---

## Test 4 — No-regression sanity (SC-003)

1. Stop the app. Switch to the `main` branch (or whichever branch is the immediate predecessor of `008-route-audio-checkpoints`). Rebuild and start fresh.
2. Open DevTools → Performance. Click "record". Navigate to `/transit-map`. Stop recording when the first vehicle appears.
3. Note the time from `navigate` to first vehicle marker render.
4. Stop the app. Switch back to `008-route-audio-checkpoints`. Rebuild and start fresh.
5. Repeat the measurement.

Pass condition: the time-to-first-vehicle on `008-route-audio-checkpoints` is within ±10 % of the predecessor branch.

---

## Test 5 — Edit the file and reload (SC-004)

1. With the app running, edit `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/checkpoints.json`. Move one checkpoint's coordinates ~500 m along its route.
2. Save the file. No rebuild of any `.cs` file is required — the static-file server (`dotnet watch` or Aspire's dev proxy) serves `wwwroot` files directly from disk. Do a hard-reload in the browser (`Ctrl+Shift+R`) to bypass the browser cache.
3. **Expected**: The marker for the edited checkpoint moves to the new location. Vehicles passing the old location no longer fire that checkpoint. Vehicles passing the new location now fire it.

Pass condition: total elapsed time from "open the file" to "verify on the map" is under 5 minutes for someone familiar with the project.

**Verified**: Live-reload of `wwwroot/checkpoints.json` does NOT require recompiling any `.cs` file. The Blazor WASM dev server (Aspire-hosted) serves static `wwwroot` files directly from the filesystem. A hard-reload is all that's needed.

---

## Test 6 — Spec edge cases (smoke)

| Edge case | Verification |
|-----------|--------------|
| Checkpoint defined off the route line | Add a deliberately-far-from-route entry to `checkpoints.json`. Reload. Expected: warning logged; checkpoint either snaps to a nearby vertex (50–500 m) or is rejected (> 500 m). No runtime errors. |
| Vehicle teleports past a checkpoint | Wait for a vehicle that experiences a stale-data jump (visible as an abrupt position warp in the animator). Expected: any checkpoints between the prior and current snap fire once each. |
| Two checkpoints close on one route | With two checkpoints near each other authored, watch a vehicle pass both within one tick. Expected: both fire, in the order the vehicle crosses them. |
| Vehicle reverses near a checkpoint | Watch a vehicle that backtracks (rare but happens with GPS noise). Expected: a second fire is suppressed within the cooldown window. |
| Tab muted | Mute the tab via the browser's tab UI. Watch a crossing. Expected: no audio (browser-level mute), but the marker still pulses; no console errors. |
| Route with no checkpoints | Pick a route with zero entries in `checkpoints.json`. Verify the map behaviour on that route is identical to before this feature. |

---

## Test 7 — DI and build hygiene

1. `dotnet build src/ChefKnifeStudios.TransitJazz.sln` produces zero new warnings vs. the pre-feature baseline.
2. The `Client.Shared` RCL bundles the new `checkpoint-audio.js` under `_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/`. Verify via DevTools → Network on `/transit-map` after the first checkpoint fires (the module is lazy-loaded, so it appears only on the first call).
3. `Client.WebApp` serves `/checkpoints.json` (200 OK, `application/json`). Confirm via DevTools → Network on page load.

---

## Sign-off checklist

Run-through complete when every box below is checked.

- [ ] Test 1 passes (audio fires on visible crossings, ≥ 9/10)
- [ ] Test 2 passes (cooldown holds)
- [ ] Test 3 passes (pre-gesture: no errors, suppressed audio, pulse still runs)
- [ ] Test 4 passes (no regression in time-to-first-vehicle)
- [ ] Test 5 passes (edit + reload roundtrip < 5 min)
- [ ] Test 6 spot-checks pass (each edge case observed at least once or reasoned-about)
- [ ] Test 7 passes (build clean, asset paths correct)

If every box is checked, the POC has met all Success Criteria in `spec.md` and is ready to be demoed.
