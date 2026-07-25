# Quickstart: Map Style Toggle

Manual verification (no automated client UI harness exists). Build first, then walk the scenarios. Maps to the
spec's user stories, FRs, and success criteria.

## Build

```powershell
dotnet build ChefKnifeStudios.TransitJazz.sln
```

Expect: solution builds with no new warnings/errors.

## Prereqs

- `MapTiler:StyleUrls` with `LightOff` and `LightOn` present in **both** `appsettings.json` and
  `appsettings.Development.json` (CFG-1).
- Run the WebApp (dev). Open browser devtools (Network + Console) for the no-refetch check.

## Scenario 1 — LightOff default (US1 / FR-001, FR-010 / SC-001)

1. Clear local storage (DevTools → Application → Local Storage → delete key `Setting`).
2. Reload the app.
3. **Expect**: the map renders in the **LightOff** basemap; routes, buses, and (per checkpoint setting)
   checkpoints are visible over it. Local storage now has `Setting` with `IsStreetMapEnabled: false`.

## Scenario 2 — Hot-switch LightOff ↔ LightOn (US2 / FR-003–FR-006 / SC-002, SC-003, SC-004)

1. Tap the gear FAB (bottom-right); the settings blade slides in.
2. **Expect**: a labeled **Street map** checkbox appears alongside Audio and Checkpoint toggles (FR-003).
3. Toggle Street map **on**.
4. **Expect**: the basemap switches to **LightOn immediately, no page reload** (FR-005). All route lines,
   buses, and visible checkpoints remain on the map (FR-006).
5. In DevTools **Network**: confirm **no** route-shapes API call and **no** new tile-data refetch of domain
   data triggered by the toggle (Principle VII / SC-004). Console shows no errors.
6. Toggle Street map **off** → basemap returns to **LightOff** immediately; data still present.

## Scenario 3 — Checkpoint visibility preserved across swap (FR-007 / IO-2)

1. Open the blade, turn **Checkpoints off** (trigger-points hidden).
2. Toggle **Street map on** (swap basemap).
3. **Expect**: after the swap, checkpoints are **still hidden** (the re-added trigger-points layer kept its
   `visibility: none`). Turn checkpoints back on → they reappear on the new basemap.

## Scenario 4 — Persistence across reload (US3 / FR-008, FR-009 / SC-005)

1. Toggle Street map **on**, then reload the app.
2. **Expect**: the map paints in **LightOn from first render** (no flash of LightOff), and the blade's Street
   map checkbox shows **on**.
3. Toggle **off**, reload → map paints LightOff from first render; checkbox shows off.

## Scenario 5 — Rapid toggling converges (edge case / SC-006)

1. Open the blade and toggle Street map on/off several times quickly.
2. **Expect**: the visible basemap matches the **final** toggle state; no torn/blank map; local storage
   `IsStreetMapEnabled` matches the final state; data layers intact.

## Scenario 6 — Toggle before map ready (edge case / EV-2)

1. Reload and immediately open the blade and toggle before the map finishes loading (best-effort timing).
2. **Expect**: no error/blank map; once loaded the map is in a consistent style. (The handler no-ops while
   `_map` is null; the initial load already reflects the persisted value.)

## Scenario 7 — Missing config entry (edge case / FR-013 / CFG-3)

1. Temporarily remove `MapTiler:StyleUrls:LightOn` from the dev config and restart.
2. Toggle Street map on.
3. **Expect**: the map stays on its current valid basemap (falls back per the chain; never blanks). Restore
   config afterward.

## Pass criteria

All seven scenarios behave as described; `dotnet build` clean; no new console errors; no domain-data refetch
on swap.
