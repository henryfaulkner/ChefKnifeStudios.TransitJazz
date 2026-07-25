# Quickstart: Mobile Map Controls & Wider Default Zoom

Manual verification steps. No automated UI test harness exists for the map interop, so these are the
acceptance gates. Run the app (AppHost / Aspire) and open the Transit map page.

## Build / run

```powershell
# From repo root
dotnet build ChefKnifeStudios.TransitJazz.sln
# Run via Aspire AppHost (or the WebApp project) and open the map page in a browser
```

## Verification matrix

| # | Requirement | Device | Steps | Expected |
|---|-------------|--------|-------|----------|
| 1 | FR-001/002 wider default | Desktop + phone viewport (DevTools device toolbar) | Hard-reload the page | Map opens centered on Atlanta at a noticeably wider extent than before (zoom 8.5 vs old 9.5); multiple routes/buses visible |
| 2 | FR-003 pinch zoom | Touch / DevTools touch emulation | Pinch two fingers apart, then together | Map zooms in, then out, centered on the pinch |
| 3 | FR-004 desktop zoom | Desktop | Scroll up/down over map; double-click | Map zooms in/out at cursor |
| 4 | FR-005 on-screen zoom | Any | Tap/click the + and − buttons | Each press changes zoom ~1 level |
| 5 | FR-005 non-occlusion (Principle X) | Desktop | Zoom in and out; observe control vs route-filter grid and gear FAB | Zoom control never overlaps the filter grid or the settings gear |
| 6 | FR-006 drag pan | Touch (1 finger) + desktop (click-drag) | Drag across the map | View follows the drag |
| 7 | FR-007 no rotation | Touch (two-finger twist) + desktop (right-drag) | Attempt to rotate | Map stays north-up and flat; no rotation/tilt |
| 8 | FR-008 zoom bounds | Any | Zoom fully in, then fully out | Stops at max (18) / min (7); no over-zoom |
| 9 | FR-009 manual precedence | Any | Pan/zoom to a custom view, then wait for several vehicle update cycles | The map stays where the user left it (no auto-recenter/fitBounds snapping back) |

## Pass criteria

All 9 rows pass. Specifically confirm the previously-broken case (#2 pinch-to-zoom) now works, since
that was disabled by the old `touchZoomRotate: false` flag.

## Rollback

All changes are confined to:
- `Client.WebApp/Pages/TransitMap.razor.cs` (one numeric default)
- `Client.Shared/wwwroot/js/map-interop.js` (`createMap` interaction options + NavigationControl)

Reverting these two files fully restores prior behavior.
