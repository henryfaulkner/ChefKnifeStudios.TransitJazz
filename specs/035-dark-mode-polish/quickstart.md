# 035 — Quickstart

## What this feature fixes

6 issues from 034 dark-mode FAB:

1. **Audio FAB hidden** — collision with MapStyle at right:124px. Fix: swap Info (224→174) and MapStyle (124→224); Audio stays at 124px.
2. **DarkMode FAB icon backwards** — sun shows in dark, moon in light. Fix: flip ternary in `DarkModeFab.razor.GetIcon()`.
3. **AudioUnlockOverlay** — white overlay on dark map. Fix: `ThemeChangedEventArgs` subscription + dark CSS class.
4. **InfoOverlay** — white panel on dark map. Fix: `ThemeChangedEventArgs` subscription in `InfoFab.razor` + dark CSS class.
5. **TransitRunningLabel** — dark text on dark map. Fix: subscription + dark class in `TransitRunningLabel.razor`.
6. **RouteFilters** — grey neutral labels unchanged in dark. Fix: subscription in `RouteFilters.razor.cs` + dark CSS overrides.

## Dark mode subscription — copy-paste pattern

Add this to any component that needs to react to dark mode:

```razor
@implements IDisposable
@inject IEventNotificationService EventNotificationService
@inject ISettingsService SettingsService

@code {
    bool _isDark;

    protected override void OnInitialized()
    {
        _isDark = SettingsService.GetSettings().IsDarkModeEnabled;
        EventNotificationService.EventReceived += HandleEvent;
    }

    void HandleEvent(object sender, IEventArgs e)
    {
        if (e is ThemeChangedEventArgs t)
        {
            _isDark = t.IsDarkMode;
            InvokeAsync(StateHasChanged);
        }
    }

    public void Dispose() => EventNotificationService.EventReceived -= HandleEvent;
}
```

## Dark color reference

From `ColorConstants.Dark` (already in codebase):
- Background: `#1A1C1E`
- Primary text: `rgba(226, 226, 230, 0.9)` (~OnSurface `#E2E2E6`)
- Secondary text / muted: `rgba(193, 199, 206, 0.7)` (~OnSurfaceVariant `#C1C7CE`)
- Button border dark: `rgba(255, 255, 255, 0.4)`

## Verify

1. Enable dark mode (tap DarkMode FAB)
2. Check: icon is moon, all 5 FABs visible with no overlap
3. Open AudioUnlockOverlay (reload without completing audio unlock) → dark background
4. Tap Info FAB → dark overlay
5. Check TransitRunningLabel → light text on dark map
6. Open route filter panel → dark section labels
7. Toggle streetmap in dark mode → correct DarkOn/DarkOff URL resolves (from 034)
8. Reload with dark persisted → all surfaces dark from first paint, no flash
