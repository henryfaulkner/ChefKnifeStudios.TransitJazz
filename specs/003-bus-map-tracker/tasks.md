# Tasks: Real-Time Bus Map Tracker

**Feature Branch**: `003-bus-map-tracker`  
**Plan**: [plan.md](plan.md)  
**Created**: 2026-05-05

---

## Phase 1 — JavaScript: Replace Jobsite Layer with Bus-Positions Layer

> File: `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/azure-maps-interop.js`

- [x] **T-01** — Verify `/images/map-pins/stop-pin-green.png` exists in `wwwroot/images/map-pins/`. Confirm the path matches what will be used in the sprite registration call.

- [x] **T-02** — Remove all jobsite pin state constants from the top of the file: `defaultPinState`, `hoverPinState`, `activePinState`, `fallbackPinIcon`, `transitPinDefaultIconPath`, `transitPinDefaultIcon`, `transitPinHoverIconPath`, `transitPinHoverIcon`, `transitPinActiveIconPath`, `transitPinActiveIcon`.

- [x] **T-03** — Remove `OvercastMap.centerJobsitePin`, `OvercastMap.plotFeatures`, and `OvercastMap.toggleJobSiteMarkerActiveState` from the `window.OvercastMap` object.

- [x] **T-04** — Add `_busPopup: null`, `_showBusTooltip`, and `_hideBusTooltip` to the `window.OvercastMap` object (plan §1.4).

- [x] **T-05** — Add `OvercastMap.upsertBusMarker(containerDivId, vehicleId, latitude, longitude)` to the `window.OvercastMap` object (plan §1.3). Must: retain the null/NaN guard with `console.warn` as a JS-side safety net (primary validation is in C# at T-22), look up shape by `vehicleId` in `OvercastMap.shapes`, call `setCoordinates` on update, create `atlas.Shape(atlas.data.Feature(...))` on insert and add to the `"bus-positions"` data source.

- [x] **T-06** — Delete `atlas.Map.prototype.initDataSourceForTransitPins` entirely and replace with `atlas.Map.prototype.initDataSourceForBusPositions` (plan §1.2). Must: create `"bus-positions"` data source, register `"bus-pin"` sprite from `/images/map-pins/stop-pin-green.png`, create `"bus-positions-layer"` SymbolLayer with icon/text options from plan, attach `mouseover`/`mouseout` events for tooltip, attach `dataremoved` event to reset `OvercastMap.shapes`.

- [x] **T-07** — In `OvercastMap.createMap`, update the `ready` event handler: replace the call to `map.initDataSourceForTransitPins(containerDivId)` with `map.initDataSourceForBusPositions(containerDivId)`.

- [ ] **T-08** — Smoke-test in browser: navigate to `/maps` (existing `AzureMapsTest` page), confirm the map still initializes without JS errors in the console. No markers will appear (that page no longer plots anything) but the map surface must load.

---

## Phase 2 — Shared Component: Update `Map.razor.Helper.cs`

> File: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor.Helper.cs`

- [x] **T-09** — Delete `PlotJobSitesAsync(object? mapFeatureCollection, bool centerMap)` from `Map.razor.Helper.cs`.

- [x] **T-10** — Delete `CenterJobsitePinAsync(int jobsiteId)` from `Map.razor.Helper.cs`.

- [x] **T-11** — Add `UpsertBusMarkerAsync(string vehicleId, float latitude, float longitude)` to `Map.razor.Helper.cs` (plan §2.2). Must call `JsRuntime.InvokeVoidAsync("OvercastMap.upsertBusMarker", ElementId, vehicleId, latitude, longitude)` wrapped in try/catch.

---

## Phase 3 — Cleanup: `AzureMapsTest.razor.cs`

> File: `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/AzureMapsTest.razor.cs`

- [x] **T-12** — Remove the `SampleDataHelper` static class and `JobsiteData` record from `AzureMapsTest.razor.cs` — both are dead code now that `PlotJobSitesAsync` no longer exists on the `Map` component.

- [x] **T-13** — Update `AzureMapsTest.MapOnReadyAsync` to an empty implementation (the method must still exist to satisfy the `OnMapReady` callback on the `Map` component in `AzureMapsTest.razor`).

- [x] **T-14** — Run `dotnet build` on `ChefKnifeStudios.TransitJazz.Client.WebApp`. Confirm zero errors before proceeding to Phase 4. ✅ 0 errors, 0 warnings.

---

## Phase 4 — New Blazor Page: `TransitMap.razor`

> New file: `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor`

- [x] **T-15** — Create `TransitMap.razor` with route `@page "/transit-map"` and `@using ChefKnifeStudios.TransitJazz.Client.Shared.Components`. Template must include: outer `<div class="transit-map-container">`, a connection status `<div>` with `class="connection-status connection-status--@_connectionCssClass"` displaying `@_connectionLabel`, and a `<Map>` component bound to `CameraOptions="DefaultCameraOptions"` and `OnMapReady="OnMapReadyAsync"` (plan §3.1).

---

## Phase 5 — New Blazor Code-Behind: `TransitMap.razor.cs`

> New file: `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`

- [x] **T-16** — Create `TransitMap.razor.cs` with `partial class TransitMap : ComponentBase, IDisposable` in namespace `ChefKnifeStudios.TransitJazz.Client.WebApp.Pages`.

- [x] **T-17** — Add injected properties: `[Inject] ISignalRNotificationService NotificationService` and `[Inject] ILogger<TransitMap> Logger`.

- [x] **T-18** — Add private fields: `Map? _map`, `bool _mapReady`, `string _connectionLabel = "Connecting…"`, `string _connectionCssClass = "connecting"`.

- [x] **T-19** — Add `static CameraOptions DefaultCameraOptions` returning `new() { Center = new Position(33.749, -84.388), Zoom = 10 }`.

- [x] **T-20** — Implement `OnInitializedAsync`: call `NotificationService.InitAsync()` in a try/catch. On success set `_connectionLabel = "Connected"` and `_connectionCssClass = "connected"` and subscribe `NotificationService.NotificationReceived += HandleBatchAsync`. On exception log with `Logger.LogError` and set `_connectionLabel = "Disconnected"` / `_connectionCssClass = "disconnected"`.

- [x] **T-21** — Implement `OnMapReadyAsync(Map map)`: assign `_map = map` and set `_mapReady = true`.

- [x] **T-22** — Implement `HandleBatchAsync(List<EventEnvelope> batch)`: guard on `!_mapReady || _map is null`, iterate envelopes, pattern-match `envelope.Payload is not VehiclePositionUpdatedEvent evt`, guard `evt.Position is null`, guard `float.IsNaN(evt.Position.Latitude) || float.IsNaN(evt.Position.Longitude)` — log a debug-level warning and `continue` for any invalid coordinate (all coordinate validation happens here in C#, not deferred to JS), call `await _map.UpsertBusMarkerAsync(evt.Vehicle.Id, evt.Position.Latitude, evt.Position.Longitude)`, finish with `await InvokeAsync(StateHasChanged)`.

- [x] **T-23** — Implement `Dispose()`: unsubscribe `NotificationService.NotificationReceived -= HandleBatchAsync`.

---

## Phase 6 — Verification

- [x] **T-24** — Run `dotnet build` on the full solution (`src/ChefKnifeStudios.TransitJazz.sln`). Confirm zero errors and zero warnings. ✅ Client project: 0 errors, 0 warnings. Full solution MSB3027 errors are file-lock noise from running AppHost, not compiler errors.

- [ ] **T-25** — Start the AppHost. Navigate to `/transit-map`. Confirm: map renders centered on Atlanta, connection status badge shows "Connected", bus markers appear within 10 seconds of connection (SC-001, SC-002, SC-004).

- [ ] **T-26** — With markers visible, wait for the next SignalR batch for an already-plotted vehicle. Confirm the marker moves rather than duplicating — open browser devtools and check that `OvercastMap.shapes` key count does not grow unboundedly across batches (SC-003).

- [ ] **T-27** — Hover over a bus marker. Confirm the tooltip popup appears showing at minimum `Vehicle: {vehicleId}` (User Story 3 / P3 acceptance).

- [ ] **T-28** — Navigate away from `/transit-map` to another page and back. Confirm: no JS errors, connection status resets to "Connecting…" then "Connected", no duplicate markers from prior session (SC-005).

- [ ] **T-29** — Navigate to `/signalr` and `/maps` directly. Confirm both routes still resolve (SC-007).

- [ ] **T-30** — Open browser console during a full session and confirm no uncaught JS exceptions and no Blazor `ObjectDisposedException` or `JSDisconnectedException` errors appear during normal use.

---

## Task Summary

| Phase | Tasks | Covers |
|-------|-------|--------|
| 1 — JS layer rewrite | T-01 – T-08 | FR-005, FR-006, FR-012, FR-013, FR-014, FR-015 |
| 2 — Map component cleanup | T-09 – T-11 | FR-009, FR-010, FR-011 |
| 3 — AzureMapsTest cleanup | T-12 – T-14 | FR-002 (compile health) |
| 4 — TransitMap template | T-15 | FR-001, FR-003, FR-018, FR-019 |
| 5 — TransitMap code-behind | T-16 – T-23 | FR-004, FR-007, FR-008, FR-009, FR-010, FR-011, FR-016, FR-017 |
| 6 — Verification | T-24 – T-30 | SC-001 – SC-007 |

**Total tasks: 30 | Completed: 23 | Remaining: T-08, T-25 – T-30 (browser verification)**
