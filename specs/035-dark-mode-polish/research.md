# 035 — Research

## Dark mode propagation pattern (existing)

**Decision**: Reuse the `ThemeChangedEventArgs` event bus already wired for `MainLayout.HandleThemeChange`. Components that need to react subscribe to `IEventNotificationService.EventReceived`, cast to `ThemeChangedEventArgs`, and re-render.

**Rationale**: This is the established pattern from feature 034. `MainLayout` already subscribes and swaps `MatTheme`. Adding subscriptions in `InfoFab`, `RouteFilters.razor.cs` (which already has a `.cs` code-behind), and `TransitRunningLabel` is zero-ceremony: `IDisposable`, subscribe in `OnInitialized`, unsubscribe in `Dispose`, call `InvokeAsync(StateHasChanged)`.

**Alternatives considered**: Cascading value from `MainLayout` — rejected because it would couple children deeply to layout hierarchy; the event bus is already the project's cross-component coupling mechanism.

---

## AudioUnlockOverlay — inline styles

**Decision**: The `AudioUnlockOverlay` component has its styles written inline in the `.razor` file (no `.razor.css` counterpart exists). The dark mode override is added as conditional CSS inside the same component, gated on a C# boolean field that subscribes to `ThemeChangedEventArgs`. Colors are swapped by applying a `--dark` modifier class on the root element.

**Rationale**: Keeping everything in the `.razor` file matches the existing convention for this component; adding a `.razor.css` scoped file would require migrating the existing inline `<style>` block simultaneously.

**Initial dark setting**: On first render the component must check `SettingsService.GetSettings().IsDarkModeEnabled` in `OnInitialized` rather than defaulting to light — the persisted setting must be honored from first paint (FR-011).

**Dark colors**: Background `#1A1C1E` (`ColorConstants.Dark.Background`), text `rgba(230,226,230,0.9)` (~`ColorConstants.Dark.OnSurface`), button border `rgba(255,255,255,0.4)`, button text `rgba(255,255,255,0.9)`.

---

## InfoFab / InfoOverlay — dark mode

**Decision**: `InfoFab.razor` has no code-behind; it is a single `.razor` file with a small `@code { }` block. Styles live in `InfoFab.razor.css`. A `ThemeChangedEventArgs` subscription in the `@code` block (with `IDisposable` + `[Inject]`) toggles a boolean field; the overlay root div gets a conditional `--dark` CSS class; `InfoFab.razor.css` carries the dark override rules.

**Initial dark setting**: Same as AudioUnlockOverlay — check `SettingsService.GetSettings()` in `OnInitialized`.

**Dark colors**: Mirror `AudioUnlockOverlay`'s dark palette (background `#1A1C1E`, text `rgba(230,226,230,0.85)`, button border/text updated for dark).

---

## RouteFilters — dark mode

**Decision**: `RouteFilters.razor.cs` already exists and can hold the `ThemeChangedEventArgs` subscription. The `.razor` template conditionally adds a `route-filters--dark` class on the root element; `RouteFilters.razor.css` adds the override rules.

**What needs overriding**: The section-label and bus-count use `color: #888` — these need to lighten in dark mode (e.g. `#aaa` or `rgba(255,255,255,0.6)`). The panel itself has no background color set in CSS (transparent), so the MatAccordion MDC theme background propagates automatically once `MatTheme` is swapped by `MainLayout`. Only the hardcoded neutral grey overrides are needed here.

**Initial dark setting**: Read from `SettingsService.GetSettings()` in `OnInitialized` of `RouteFilters.razor.cs`.

---

## TransitRunningLabel — dark mode

**Decision**: Inline styles in the `.razor` file. A `ThemeChangedEventArgs` subscription in the `@code` block toggles a boolean field; root div conditionally gets `transit-running-label--dark`; the dark rules override `color: #171717` → `color: rgba(230,226,230,0.9)`.

**Initial dark setting**: Read from `SettingsService.GetSettings()` in `OnInitialized`.

**Icon dots**: Rail (`#1a237e`) and bus (`darkgreen`) dots are legible on dark background — no change needed.

---

## Audio FAB position conflict — root cause and fix

**Current state after 034**:

| FAB | CSS `right` after 034 |
|---|---|
| City | 24px |
| DarkMode (new, 034) | 74px |
| MapStyle | 124px ← COLLISION |
| Audio | 124px ← COLLISION |
| Info | 224px |

**User intent**: "move [Audio] left btw info and darkmode" — insert Audio in the slot between DarkMode and Info.

**Desired order** (right → left, i.e. increasing `right` value):

| FAB | right |
|---|---|
| City | 24px (unchanged) |
| DarkMode | 74px (unchanged) |
| Audio | 124px (stays, was already here pre-034) |
| Info | 174px (shift: 224 → 174) |
| MapStyle | 224px (shift: 124 → 224) |

**Files changed**:
- `AudioFab.razor.css`: no change (stays at 124px)
- `InfoFab.razor.css`: 224px → 174px
- `MapStyleFab.razor.css`: 124px → 224px

---

## DarkMode FAB icon semantics

**Current state**: DarkModeFab `GetIcon()` returns `"light_mode"` when dark is active and `"dark_mode"` when light is active. This is backwards from the sun/moon convention.

**Desired**: When **light mode** is active → show **sun** (Material icon `"light_mode"`). When **dark mode** is active → show **moon** (Material icon `"dark_mode"`).

**Fix**: Flip the ternary in `GetIcon()`:
- Was: `_settings.IsDarkModeEnabled ? "light_mode" : "dark_mode"`
- Should be: `_settings.IsDarkModeEnabled ? "dark_mode" : "light_mode"`

Wait — re-reading the user request: "When light mode is enabled, show sun icon. When dark mode is enabled, show moon icon." The Material icon named `"light_mode"` IS the sun icon; `"dark_mode"` IS the moon icon. So:
- Light mode active → show sun → `"light_mode"` icon
- Dark mode active → show moon → `"dark_mode"` icon

Current code: `_settings.IsDarkModeEnabled ? "light_mode" : "dark_mode"` gives moon when light and sun when dark — correct icon semantics are the opposite. **Fix**: swap to `_settings.IsDarkModeEnabled ? "dark_mode" : "light_mode"`.
