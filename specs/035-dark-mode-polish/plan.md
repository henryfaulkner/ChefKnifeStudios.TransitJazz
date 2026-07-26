# Implementation Plan: 035 — Dark Mode Polish

**Branch**: `main` | **Date**: 2026-07-03 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/035-dark-mode-polish/spec.md`

## Summary

Fix six issues introduced or exposed by the 034 dark-mode FAB: Audio FAB position collision, DarkMode FAB icon semantics, and four components that don't respond to the dark mode toggle (`AudioUnlockOverlay`, `InfoFab`/InfoOverlay, `TransitRunningLabel`, `RouteFilters`). All changes are frontend-only CSS and Blazor component additions. No new services, interop, or event types — the existing `ThemeChangedEventArgs` event bus and `SettingsService` are the only mechanisms used.

## Technical Context

**Language/Version**: C# / .NET 10, Blazor WASM  
**Primary Dependencies**: MatBlazor (MDC theme), `IEventNotificationService`, `ISettingsService`, CSS scoped stylesheets  
**Storage**: N/A (reads from existing `SettingsService` local storage)  
**Testing**: Manual browser QA (T6 in tasks)  
**Target Platform**: Blazor WASM (Azure Static Web Apps), mobile-primary viewport  
**Project Type**: Blazor WASM frontend (RCL component library + WebApp host)  
**Performance Goals**: No perceptible render delay on dark mode toggle  
**Constraints**: Frontend-only; no server/worker/shared changes; no new event types; no new JS interop  
**Scale/Scope**: 6 targeted component edits + 3 FAB position adjustments

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Decoupled Cloud Architecture | ✅ Pass | Frontend-only; no backend changes |
| II. No Frontend Secrets | ✅ Pass | No credentials involved |
| III. Two-Pass Pipeline | ✅ Pass | No server changes |
| VII. OpenStreetMap Cartography | ✅ Pass | Basemap swap path unchanged |
| VIII. Generative Transit Music | ✅ Pass | Audio not affected |
| IX. Persistent Multi-Selection | ✅ Pass | RouteFilters selection behavior unchanged |
| XI. Snappy, Reversible Overlays | ✅ Pass | No timing changes |
| XII. Settings-Driven Presentation | ✅ Pass | Dark mode reads from `SettingsService` |
| Localization | ✅ Pass | No new strings required; all affected components already localized |

No constitution violations.

## Project Structure

### Documentation (this feature)

```text
specs/035-dark-mode-polish/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (minimal — no new data entities)
├── quickstart.md        # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (files touched)

```text
src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/
├── Components/
│   ├── FABs/
│   │   ├── AudioFab.razor.css          # no change (stays at right:124px)
│   │   ├── InfoFab.razor.css           # 224px → 174px
│   │   ├── InfoFab.razor               # + ThemeChangedEventArgs subscription + _isDark field
│   │   ├── MapStyleFab.razor.css       # 124px → 224px
│   │   └── DarkModeFab.razor           # flip GetIcon() ternary
│   ├── AudioUnlockOverlay.razor        # + ThemeChangedEventArgs subscription + dark CSS class
│   ├── RouteFilters.razor              # + conditional dark class on root div
│   ├── RouteFilters.razor.css          # + .route-filters--dark overrides
│   └── TransitRunningLabel.razor       # + ThemeChangedEventArgs subscription + dark CSS class
│
└── (no new files)
```

## The Dark Mode Subscription Pattern

All four components that need to respond to dark mode use the same pattern:

```csharp
// inject
@inject IEventNotificationService EventNotificationService
@inject ISettingsService SettingsService
@implements IDisposable

// field
bool _isDark;

// OnInitialized — honor persisted setting from first paint
protected override void OnInitialized()
{
    _isDark = SettingsService.GetSettings().IsDarkModeEnabled;
    EventNotificationService.EventReceived += HandleEvent;
}

// handler
void HandleEvent(object sender, IEventArgs e)
{
    if (e is ThemeChangedEventArgs theme)
    {
        _isDark = theme.IsDarkMode;
        InvokeAsync(StateHasChanged);
    }
}

// dispose
public void Dispose() => EventNotificationService.EventReceived -= HandleEvent;
```

The `_isDark` field drives a CSS class conditional on the root element:
```razor
<div class="component-root @(_isDark ? "component-root--dark" : "")">
```

Dark override rules are colocated in the component's stylesheet or inline `<style>` block.

## Dark Color Values

Taken from `ColorConstants.Dark` (already in the codebase):

| Token | Value | Use |
|---|---|---|
| Background | `#1A1C1E` | Overlay + panel backgrounds in dark mode |
| OnSurface | `#E2E2E6` | Primary text in dark mode |
| OnSurfaceVariant | `#C1C7CE` | Secondary / muted text in dark mode |

## Files Touched — Summary

| File | Change |
|---|---|
| `Components/FABs/DarkModeFab.razor` | Flip `GetIcon()` ternary |
| `Components/FABs/InfoFab.razor.css` | `right: 224px → 174px` |
| `Components/FABs/InfoFab.razor` | `+_isDark` field + `ThemeChangedEventArgs` sub + dark class on overlay |
| `Components/FABs/MapStyleFab.razor.css` | `right: 124px → 224px` |
| `Components/AudioUnlockOverlay.razor` | `+_isDark` field + event sub + dark CSS class + dark override rules |
| `Components/RouteFilters.razor` | `+ route-filters--dark` conditional class on root |
| `Components/RouteFilters.razor.cs` | `+_isDark` field + `ThemeChangedEventArgs` sub |
| `Components/RouteFilters.razor.css` | `+ .route-filters--dark` color overrides |
| `Components/TransitRunningLabel.razor` | `+_isDark` field + event sub + dark CSS class + dark color overrides |

No new files. `AudioFab.razor.css` is unchanged (stays at 124px).

## Risks

- `InfoFab.razor` currently has no `@inject` directives and no `IDisposable`. Adding them to a previously stateless component is low-risk but deserves a build check.
- `AudioUnlockOverlay.razor` has inline styles; moving to a class toggle avoids touching the existing style rules (pure addition, not replacement).
- FAB position reordering: swapping Info (224→174) and MapStyle (124→224) changes which FAB is visually "further left." Verify no overlap at all breakpoints.
