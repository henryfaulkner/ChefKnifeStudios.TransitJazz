# Phase 1 Data Model: Mobile Map Controls & Wider Default Zoom

This feature introduces **no new persisted entities and no new data contracts**. It changes
configuration values and interaction-handler settings on an existing in-memory map object. The only
"entities" are existing camera/interaction settings whose values or enabled-state change.

## Existing entities touched (no schema change)

### CameraOptions (`Client.Shared/Models/CameraOptions.cs`) — unchanged shape

| Field | Type | Constraint | Change in this feature |
|-------|------|-----------|------------------------|
| `Center` | `Position` | required | Unchanged (`33.749, -84.388`) |
| `Zoom` | `double` | clamps to `[1, 24]` (MIN_ZOOM/MAX_ZOOM) | **Default value supplied by caller changes `9.5 → 8.5`** (the model's clamp range is untouched; `8.5` is well within `[1,24]`) |

The default value lives in the caller, not the model:
`Client.WebApp/Pages/TransitMap.razor.cs` → `DefaultCameraOptions => new() { Center = …, Zoom = 8.5 }`.

### Map interaction settings (`map-interop.js` `createMap`) — runtime config, not a typed model

| Setting | Before | After | Requirement |
|---------|--------|-------|-------------|
| `minZoom` | `7` | `7` (unchanged) | FR-008 lower bound |
| `maxZoom` | `18` | `18` (unchanged) | FR-008 upper bound |
| `dragRotate` | `false` | `false` (unchanged) | FR-007 no rotation |
| `touchZoomRotate` (init option) | `false` | **removed** (default on) | FR-003 enable pinch |
| `touchZoomRotate.disableRotation()` | — | **called after create** | FR-007 keep north-up |
| `dragPan` | default (on) | default (on) — verified | FR-006 pan |
| `NavigationControl` (zoom-only) | absent | **added, `bottom-right`** | FR-005 on-screen zoom |

### State transitions

None. There is no stateful workflow. The only behavioral state considered is the implicit
"user has manually moved the camera" condition discussed in research Decision 4, which for the MVP is
handled by not issuing recurring auto-recenters rather than by a tracked flag.

## Validation rules (from requirements)

- Zoom presented to the user is always within `[minZoom, maxZoom]` = `[7, 18]` (FR-008). MapLibre
  enforces this natively for every zoom path (pinch, scroll, buttons), so no app-level guard is added.
- The map must never apply bearing ≠ 0 or pitch ≠ 0 (FR-007). Enforced by `dragRotate:false` +
  `touchZoomRotate.disableRotation()` + a compass-less `NavigationControl`.
