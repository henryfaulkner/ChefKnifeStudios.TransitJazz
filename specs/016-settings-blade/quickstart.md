# Quickstart: Settings Blade

Manual verification plan (no automated client UI harness exists in this repo). Build, then walk the scenarios.
Maps to the spec's user stories (US1–US3), functional requirements (FR-###), and success criteria (SC-###).

## Build

```powershell
dotnet build src/ChefKnifeStudios.TransitJazz.sln
```

Run the app via the existing AppHost / WebApp launch (same as features 014/015). Open the map page.

## Scenario 1 — Open & dismiss the drawer (US1; FR-001,002,004,005,006; SC-001,SC-005)

1. Confirm a **gear FAB** is visible bottom-right on the map page.
2. Click it → the **settings drawer slides in from the right in ≤100ms**. ✅ FR-002, Principle XI.
3. Click the drawer's **✕** → it disappears **immediately** (no exit animation). ✅ FR-004.
4. Click the gear again to open; click **anywhere outside** the drawer → it closes immediately. ✅ FR-005.
5. Click the gear again to open; **re-click the gear** → it closes. ✅ Principle XII (re-click closes).
6. Open, then click **inside** the drawer immediately → it stays open (the opening click does not bounce it
   shut). ✅ FR-006 (min-open guard).

## Scenario 2 — Settings render as labeled toggles (US1; FR-003,FR-014; SC-002)

1. Open the drawer. Confirm exactly **three** toggles, in order, with **localized** labels:
   - Audio
   - Street map
   - Checkpoints
2. Confirm each toggle's checked state matches its stored value (all **on** on a fresh browser). ✅ FR-008.
3. Confirm there is **no** dark-mode toggle and **no** language selector (deferred). ✅ scope.

## Scenario 3 — Persistence across reload (US2; FR-007,FR-008; SC-003)

1. Toggle **Checkpoints** off. Close the drawer.
2. **Reload** the page. Reopen the drawer → Checkpoints is still **off**. ✅ FR-007.
3. Inspect local storage: key **`Setting`** holds the JSON blob with `"AreCheckpointsVisible":false`. ✅.
4. (Fresh browser / cleared storage) first open seeds defaults and they read back identically. ✅ FR-008.

## Scenario 4 — Audio toggle effect (US1; FR-009/effect; constitution XII Audio)

1. With audio on and buses moving, confirm crossing/held notes play as buses pass checkpoints.
2. Toggle **Audio** off → synth playback stops (no new notes). Toggle on → playback resumes.
3. Toggle off, reload → audio remains muted (persisted). ✅.

## Scenario 5 — GIS basemap toggle, layers persist (US-effect; Principle VII)

1. With **Street map** on, confirm the streets basemap renders beneath the route/bus/checkpoint layers.
2. Toggle **Street map** off → basemap becomes a **blank dark canvas**, but **route polylines, bus markers,
   and checkpoints remain rendered and in place** (no flicker of lost data, no network re-fetch). ✅ VII.
3. Toggle back on → streets return; data layers unchanged throughout.
4. If a route was focused (feature 015 highlight/blur), confirm focus state survives or re-applies after the
   swap.

## Scenario 6 — Checkpoint visibility toggle (US-effect; Principle VII)

1. With **Checkpoints** on, confirm checkpoint markers are visible along routes.
2. Toggle **Checkpoints** off → markers hide instantly; routes/buses unaffected; no re-fetch.
3. Toggle on → markers reappear.

## Scenario 7 — No leaks across navigation (US1; FR-012; SC-006)

1. Open/close the drawer several times; navigate away from the map page and back.
2. Confirm a single outside-click does not trigger multiple closes, and toggling a setting once does not fire
   its effect multiple times (no accumulated `document` click listeners or duplicate bus subscriptions). ✅
   FR-012/SC-006.
3. (Dev check) The `BladeContainer` removes its outside-click listener on `Dispose`; `SettingsBlade` and
   `TransitMap` unsubscribe from the bus on `Dispose`.

## Localization check (Principle XII)

- All four added strings (`SettingsTitle`, `SettingAudioEnabled`, `SettingStreetsBasemap`,
  `SettingCheckpointsVisible`) come from `RouteFilterResources.resx` via
  `IStringLocalizer<RouteFilterResources>` — no hardcoded labels in `.razor`/`.cs`. ✅.

## Done when

- All 7 scenarios pass, `dotnet build` is clean, and the Constitution Check in `plan.md` still holds (XII
  partial — Language deferred — is the only tracked deviation).
