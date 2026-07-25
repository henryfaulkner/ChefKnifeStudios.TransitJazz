# SettingsBlade — Full Implementation Design Document

> **Purpose of this document.** This is a self-contained, context-free specification for re-implementing
> the **SettingsBlade** feature (a slide-out settings panel) and **every dependency it touches**, exactly
> as implemented in the PokerAttack Blazor WebAssembly application. An agent with no prior knowledge of
> this codebase should be able to recreate the feature in another project by following this document
> verbatim. Where a dependency exists only to support the blade, its complete source is reproduced here.
> Where a dependency is larger than the blade needs, only the relevant members are reproduced and the
> rest is explicitly called out as out-of-scope.

---

## 1. Feature overview

The **SettingsBlade** is an off-canvas panel ("blade") that slides in from the right edge of the screen.
It is triggered by a floating action button (FAB), renders a checkbox per boolean application setting
(discovered via reflection), persists changes to browser local storage, and — for the dark-mode setting —
applies a theme change immediately and broadcasts a notification so other components (the layout) can
re-theme themselves. When the user is on a gameplay screen, the blade also shows a **LEAVE GAME** button.

Behavioral summary:

1. A `SettingsFab` (gear icon) posts a `BladeEventArgs { Type = Settings }` onto a global pub/sub bus
   (`IEventNotificationService`).
2. The `SettingsBlade` subscribes to that bus. On a `Settings` event it opens its inner `BladeContainer`;
   on any other `BladeEventArgs` it closes it.
3. The blade reflects over the `Settings` model's `bool` properties and renders a `MatCheckbox` per
   property, labeling each with its `[Description]` attribute.
4. Toggling a checkbox calls `ISettingsService.SetSettingValue(...)`, which persists to local storage.
5. If the toggled property is `IsDarkModeEnabled`, the blade also (a) calls a JS interop method to swap the
   `dark` CSS class on `<body>`, and (b) posts a `ThemeChangedEventArgs` so `MainLayout` swaps its
   `MatTheme`.
6. The `BladeContainer` wires an "outside click" listener (via JS interop) so clicking off the blade
   closes it, with a 300 ms minimum-open guard to avoid the opening click immediately closing it.

---

## 2. Solution / project layout

The feature spans three projects. Namespaces matter because cross-project `@using`/`using` statements
reference them directly.

| Project | Role | Relevant namespaces |
|---|---|---|
| `*.Client.Core` | Framework-agnostic client services (DI-registered primitives). | `...Client.Core.Services` |
| `*.Client.Shared` | Razor component library: components, view models, models, services, JS interop, constants, extensions, event args. | `...Client.Shared.*` |
| `*.Client.WebApp` | The Blazor WASM host: `Program.cs` (DI composition root), `MainLayout`, CSS, `wwwroot`. | `...Client.WebApp.*` |

Replace `ChefKnifeStudios.PokerAttack` with your own root namespace throughout; the relative structure is
what matters.

### 2.1 File inventory (everything this feature requires)

```
Client.Core/
  Services/
    EventNotificationService.cs          (§5)   — pub/sub bus + IEventArgs marker

Client.Shared/
  Components/
    Blades/
      BladeContainer.razor               (§6)   — generic slide-out container (markup)
      BladeContainer.razor.cs            (§6)   — container code-behind
      BladeContainer.razor.css           (§6)   — container scoped styles
      SettingsBlade.razor                (§7)   — the settings panel (markup)
      SettingsBlade.razor.cs             (§7)   — settings panel code-behind
      SettingsBlade.razor.css            (§7)   — settings panel scoped styles
    FABs/
      FabList.razor                      (§8)   — FAB stack container (markup)
      FabList.razor.cs                   (§8)   — FAB stack code-behind (enums + position logic)
      FabList.razor.css                  (§8)   — FAB stack scoped styles
      SettingsFab.razor                  (§8)   — gear FAB that opens the settings blade
      HelpFab.razor                      (§8)   — sibling FAB (optional; shown for completeness)
    Layout? -> see Client.WebApp/Layout/MainLayout.razor
  EventArgs/
    BladeEventArgs.cs                    (§5)   — blade open/close event payload
    ThemeChangedEventArgs.cs             (§5)   — theme-change broadcast payload
  Models/
    Settings.cs                          (§4)   — the settings model (reflected over)
  Services/
    SettingsService.cs                   (§4)   — persistence of Settings to local storage
    JsInterop/
      CommonJsInterop.cs                 (§9)   — JS interop wrapper (theme + outside-click used)
  Constants/
    LocalStorageConstants.cs             (§4)   — local storage keys
  Extensions/
    NavigationManagerExtensions.cs       (§10)  — IsOnGameplay / NavigateToLobby helpers
  ViewModels/
    ApplicationViewModel.cs              (§10)  — provides Player.Id for LeaveGame (interface only needed)
  wwwroot/
    scripts/commonJsInterop.js           (§9)   — JS module (setTheme + outside-click functions)

Client.WebApp/
  Program.cs                             (§11)  — DI registrations
  Layout/MainLayout.razor                (§12)  — hosts <SettingsBlade/>, reacts to ThemeChangedEventArgs
  wwwroot/css/variables.css              (§13)  — theme CSS custom properties + .dark class + utilities
```

---

## 3. Dependency graph

```
SettingsFab ──posts BladeEventArgs(Settings)──▶ IEventNotificationService ◀──subscribes── SettingsBlade
                                                          │                                     │
                                                          │                                     ├─ ISettingsService ─▶ Settings (model) ─▶ ISyncLocalStorageService
                                                          │                                     ├─ ICommonJsInterop ─▶ commonJsInterop.js (setTheme)
                                                          │                                     ├─ NavigationManagerExtensions (IsOnGameplay / NavigateToLobby)
                                                          │                                     ├─ IGameplayEndpointsService (LeaveGameAsync) [gameplay only]
                                                          │                                     ├─ IApplicationViewModel (Player.Id)          [gameplay only]
                                                          │                                     └─ BladeContainer (inner) ─▶ ICommonJsInterop (add/removeOutsideClickListener)
                                                          │
   MainLayout ◀──posts ThemeChangedEventArgs── SettingsBlade (on dark-mode toggle)
```

### Third-party NuGet packages (versions as used)

| Package | Version | Used for |
|---|---|---|
| `MatBlazor` | 2.10.0 | `MatCheckbox`, `MatButton`, `MatFAB`, `MatIconButton`, `MatH4`, `MatThemeProvider`, `MatTheme`. |
| `Blazored.LocalStorage` | 4.5.0 | `ISyncLocalStorageService` for settings persistence. |
| `CommunityToolkit.Mvvm` | 8.4.0 | `[ObservableProperty]` source generators on the `Settings` model. |
| `Microsoft.AspNetCore.Components.Web` | 10.0.0 | Blazor component base + `KeyboardEventArgs`. |

> MatBlazor is a Material Design component library for Blazor. If you swap it out, you must replace
> `MatCheckbox` (a bool-valued checkbox with `Value`/`ValueChanged`), `MatButton`, `MatFAB`/`MatIconButton`
> (icon buttons by Material icon name), `MatH4`, and `MatThemeProvider`/`MatTheme` with equivalents.

---

## 4. The settings model & persistence layer

### 4.1 `Settings` model — `Client.Shared/Models/Settings.cs`

The blade reflects over this type's public properties. Each property must expose a `[Description]` so the
blade can render a human-readable label. Booleans become checkboxes. Uses CommunityToolkit.Mvvm's
`[ObservableProperty]` to generate the public property from the private backing field; the
`[property: Description(...)]` attribute is forwarded onto the generated property (this is required — a
bare `[Description]` would land on the field, not the generated property, and reflection reads the
property).

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Models;

public partial class Settings : ObservableObject
{
    [ObservableProperty]
    [property: Description("Audio Enabled")]
    bool _isAudioEnabled = true;

    [ObservableProperty]
    [property: Description("Always Show App Tour")]
    bool _isAlwaysShowAppTour = false;

    [ObservableProperty]
    [property: Description("Enable Dark Mode")]
    bool _isDarkModeEnabled = false;
}
```

The generated public properties are `IsAudioEnabled`, `IsAlwaysShowAppTour`, `IsDarkModeEnabled`. The blade
references `nameof(Settings.IsDarkModeEnabled)` for its special-case theme handling.

### 4.2 Local storage keys — `Client.Shared/Constants/LocalStorageConstants.cs`

Only `SettingsKey` is needed by the blade; the rest are shown to match the source exactly. Note the value
is the string `"Setting"` (singular), not `"Settings"` — preserve this if you want storage compatibility.

```csharp
namespace ChefKnifeStudios.PokerAttack.Client.Shared.Constants;

public static class LocalStorageConstants
{
    public const string PlayerNameKey = "PlayerName";
    public const string SettingsKey = "Setting";
    public const string HasSeenTourKey = "HasSeenTour";
    public const string SoloGameStateKey = "SoloGameState";
    public const string SoloGameResultKey = "SoloGameResult";
}
```

### 4.3 `SettingsService` — `Client.Shared/Services/SettingsService.cs`

Wraps `Blazored.LocalStorage`'s **synchronous** local storage service. `GetSettings` lazily seeds defaults
on first read. `SetSettingValue<T>` sets a property by name via reflection then persists the whole object.

```csharp
using Blazored.LocalStorage;
using ChefKnifeStudios.PokerAttack.Client.Shared.Constants;
using ChefKnifeStudios.PokerAttack.Client.Shared.Models;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services;

public interface ISettingsService
{
    Settings GetSettings();
    void SaveSettings(Settings settings);
    T? GetSettingValue<T>(string propertyName);
    void SetSettingValue<T>(string propertyName, T value);
}

public class SettingsService : ISettingsService
{
    readonly ISyncLocalStorageService _localStorageService;

    public SettingsService(ISyncLocalStorageService localStorageService)
    {
        _localStorageService = localStorageService;
    }

    public Settings GetSettings()
    {
        var storedSettings = _localStorageService.GetItem<Settings>(LocalStorageConstants.SettingsKey);
        if (storedSettings is not null)
        {
            return storedSettings;
        }

        var defaultSettings = new Settings();
        SaveSettings(defaultSettings);
        return defaultSettings;
    }

    public void SaveSettings(Settings settings)
    {
        _localStorageService.SetItem(LocalStorageConstants.SettingsKey, settings);
    }

    public T? GetSettingValue<T>(string propertyName)
    {
        var settings = GetSettings();
        var property = typeof(Settings).GetProperty(propertyName);
        if (property is null)
        {
            return default;
        }

        var value = property.GetValue(settings);
        if (value is T typedValue)
        {
            return typedValue;
        }

        return default;
    }

    public void SetSettingValue<T>(string propertyName, T value)
    {
        var settings = GetSettings();
        var property = typeof(Settings).GetProperty(propertyName);
        if (property is not null)
        {
            property.SetValue(settings, value);
            SaveSettings(settings);
        }
    }
}
```

> **`ISyncLocalStorageService`** comes from `Blazored.LocalStorage`. Register it via
> `builder.Services.AddBlazoredLocalStorage();` (see §11). Both the async and sync interfaces are
> registered by that single call.

---

## 5. Event bus & event payloads

### 5.1 `IEventNotificationService` — `Client.Core/Services/EventNotificationService.cs`

A minimal in-memory pub/sub. `IEventArgs` is an empty marker interface; all event payloads implement it.
Handlers are async (`EventReceivedEventHandler` returns `Task`). Registered as a **singleton** so all
components share one bus.

```csharp
namespace ChefKnifeStudios.PokerAttack.Client.Core.Services;

public delegate Task EventReceivedEventHandler(
    object sender, IEventArgs e);

public interface IEventNotificationService
{
    event EventReceivedEventHandler? EventReceived;
    void PostEvent(object sender, IEventArgs args);
}

public interface IEventArgs
{
}

public class EventNotificationService : IEventNotificationService
{
    public event EventReceivedEventHandler? EventReceived;

    public void PostEvent(object sender, IEventArgs args)
    {
        EventReceived?.Invoke(sender, args);
    }
}
```

> **Threading / re-entrancy note.** `PostEvent` invokes the multicast delegate synchronously; each handler
> returns a `Task` but `PostEvent` does not await them (fire-and-forget). Subscribers that touch component
> state should marshal to the renderer (`InvokeAsync(StateHasChanged)`) — see MainLayout in §12. Always
> unsubscribe in `Dispose` to avoid leaks (every subscriber below does).

### 5.2 `BladeEventArgs` — `Client.Shared/EventArgs/BladeEventArgs.cs`

Drives blade open/close. `Type == Settings` opens the settings blade; any other `Type` (i.e. `Close`)
closes it. `Data` is an optional generic payload slot (unused by the settings blade).

```csharp
using ChefKnifeStudios.PokerAttack.Client.Core.Services;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;

public class BladeEventArgs : IEventArgs
{
    public enum Types
    {
        Close,
        Settings,
    }

    public required Types Type { get; init; }

    public object? Data { get; init; }
}
```

### 5.3 `ThemeChangedEventArgs` — `Client.Shared/EventArgs/ThemeChangedEventArgs.cs`

Broadcast when dark mode is toggled so the layout can swap its `MatTheme`.

```csharp
using ChefKnifeStudios.PokerAttack.Client.Core.Services;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;

public class ThemeChangedEventArgs : IEventArgs
{
    public required bool IsDarkMode { get; init; }
}
```

> The codebase namespace `...Client.Shared.EventArgs` intentionally shadows `System.EventArgs`. Inside
> these files and any consumer, references to the framework type are fully qualified as
> `System.EventArgs`. Keep that convention or rename the namespace.

---

## 6. `BladeContainer` — the reusable slide-out shell

`SettingsBlade` does not implement its own slide/close mechanics; it composes a generic `BladeContainer`.
The container owns: the open/closed visual state, the close (✕) button, the outside-click-to-close
listener, and a minimum-open-duration guard.

### 6.1 `BladeContainer.razor` (markup) — `Client.Shared/Components/Blades/BladeContainer.razor`

```razor
<div id="@_elementId" class="blade-container @( _isOpen ? "open" : "" )">
    <MatIconButton 
        Icon="close"
        @onclick="HandleClosePressed" 
        Class="cross"
    />

    <div class="content-container">
        @ContentFragment
    </div>
</div>
```

### 6.2 `BladeContainer.razor.cs` (code-behind)

Key points:
- `ContentFragment` is a **required** `RenderFragment` — the caller projects content into the blade.
- `KeepOpen` (optional) makes `Close()` a no-op (a pinned blade).
- `Open()`/`Close()` are **public** so a parent (the settings blade) can drive them directly.
- `Open()` registers a document-level outside-click listener via `ICommonJsInterop`; clicking outside the
  blade's DOM element fires `HandleClosePressed`, which posts a `BladeEventArgs(Close)` back onto the bus.
  (It deliberately posts an event rather than closing directly so the close path is uniform.)
- `MinOpenDurationMs = 300` guards against the same click that opened the blade immediately closing it
  (the opening click bubbles to `document` after the blade renders).
- `_elementId` regenerates a GUID-based id; see the note below.

```csharp
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Blades;

public partial class BladeContainer : ComponentBase, IDisposable
{
    [Parameter] public required RenderFragment ContentFragment { get; set; }
    [Parameter] public bool KeepOpen { get; set; }

    [Inject] IEventNotificationService EventNotificationService { get; set; } = null!;
    [Inject] ICommonJsInterop CommonJsInteropService { get; set; } = null!;

    bool _isOpen = false;
    string _elementId => $"blade-{new Guid().ToString()}";
    DateTime _lastOpenedUtc;
    const int MinOpenDurationMs = 300; 

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (!firstRender) return;
    }

    public void Dispose()
    {
        _ = CommonJsInteropService.RemoveOutsideClickListenerAsync(_elementId);
    }

    public void Open()
    {
        _lastOpenedUtc = DateTime.UtcNow;
        _isOpen = true;
        _ = CommonJsInteropService.AddOutsideClickListenerAsync(_elementId, HandleClosePressed);
        StateHasChanged();
    }

    public void Close()
    {
        if (KeepOpen) return;
        // Prevent close if not enough time has passed since open
        if ((DateTime.UtcNow - _lastOpenedUtc).TotalMilliseconds < MinOpenDurationMs)
            return;

        _isOpen = false;
        _ = CommonJsInteropService.RemoveOutsideClickListenerAsync(_elementId);
        StateHasChanged();
    }

    void HandleClosePressed()
    {
        EventNotificationService.PostEvent(
            this,
            new BladeEventArgs()
            { 
                Type = BladeEventArgs.Types.Close,
            }
        );
    }
}
```

> **⚠️ Known quirk to reproduce faithfully (or fix).** `_elementId` is an **expression-bodied property**
> using `new Guid()` (the *default/empty* GUID, i.e. all zeros — `00000000-0000-0000-0000-000000000000`),
> evaluated every access. This means: (a) the id is the same every time (`"blade-00000000-..."`), and
> (b) it is **not** `Guid.NewGuid()`. With a single blade on the page this works (the markup `id`, the
> add-listener call, and the remove-listener call all compute the same constant string). If you place
> **multiple** `BladeContainer`s on one page they will collide on `id`. To make it robust, change to a
> cached unique id:
>
> ```csharp
> readonly string _elementId = $"blade-{Guid.NewGuid()}";
> ```
>
> The original source is preserved above to match exactly; prefer the fix in a new project.

### 6.3 `BladeContainer.razor.css` (scoped styles)

The container is `position: absolute`, pinned to the top-right, full viewport height, and translated
off-screen by default. The `.open` class slides it in (`translateX(0)`) with a shadow. Scrollbars are
hidden cross-browser. The `::deep .cross` selector reaches the MatBlazor close button rendered inside.

```css
.blade-container {
    position: absolute;
    top: 0;
    right: 0;
    max-width: 500px;
    width: 80vw;
    height: 100dvh;
    background-color: var(--clr-surface2, #f2ecee);
    z-index: 2;
    transform: translateX(100%); /* Start off-screen */
    transition: transform 0.3s ease-in-out;
    overflow-y: scroll;
    -webkit-overflow-scrolling: touch;
    -ms-overflow-style: none; /* Internet Explorer 10+ */
    scrollbar-width: none; /* Firefox, Safari 18.2+, Chromium 121+ */
}

.blade-container::-webkit-scrollbar {
    display: none; /* Older Safari and Chromium */
}

.blade-container.open {
    transform: translateX(0); 
    box-shadow: -2px 0px 8px rgba(0, 0, 0, 0.2);
}

.blade-container .content-container {
    flex: 1 1 auto;
    overflow-y: auto;
    padding: 24px;
    display: flex;
    flex-direction: column;
    box-sizing: border-box;
    min-height: 0;
    height: 100%;
}

.blade-container ::deep .cross {
    position: absolute;
    top: 10px;
    right: 10px;
    cursor: pointer;
    color: var(--clr-1p, #6442d6);
    font-size: 1.25rem;
    border: none;
    z-index: 3;
}
```

> The 300 ms slide transition in CSS must stay ≥ `MinOpenDurationMs` conceptually-aligned; they are
> independent values but both 300 by design.

---

## 7. `SettingsBlade` — the settings panel itself

### 7.1 `SettingsBlade.razor` (markup) — `Client.Shared/Components/Blades/SettingsBlade.razor`

Reflection drives the checkbox list. For each public property on `Settings`, it reads the `[Description]`
(falling back to the property name) and binds a `MatCheckbox` to the boolean value. The `LEAVE GAME`
button renders only when the URL indicates a gameplay screen.

```razor
@using System.Reflection
@using System.ComponentModel
@using ChefKnifeStudios.PokerAttack.Client.Shared.Models

<div class="settings-blade">
    <BladeContainer @ref="_bladeContainer">
        <ContentFragment>
            <MatH4 class="clr-on-1t-container" Style="margin: 0 0 16px;">
                Settings
            </MatH4>

            <div class="blade-content">
                <div class="settings-list">
                    @{
                        var settings = SettingsService.GetSettings();
                        var type = typeof(Settings);
                        var properties = type.GetProperties();
                    }
                    @foreach (var property in properties)
                    {
                        var description = property.GetCustomAttribute<DescriptionAttribute>()?.Description ?? property.Name;
                        <MatCheckbox
                            TValue="bool"
                            Value="@(property.GetValue(settings) as bool? ?? false)"
                            ValueChanged="(e) => HandleSettingPressed(property.Name, e)"
                        >
                            <span class="setting-label mat-body1">
                                @description
                            </span>
                        </MatCheckbox>
                    }
                </div>

                @if (NavigationManager.IsOnGameplay())
                {
                    <MatButton 
                        Id="leaveGameBtn"
                        Class="leave-game-btn"
                        OnClick="HandleLeaveGamePressed"
                        Unelevated="true"
                    >
                        LEAVE GAME
                    </MatButton>
                }
            </div>
        </ContentFragment>
    </BladeContainer>
</div>
```

> **Reflection caveat.** `type.GetProperties()` returns **all** public instance properties — including
> those inherited from `ObservableObject` (e.g. none are public there, but `INotifyPropertyChanged` events
> are not properties, so in practice you get exactly the three settings). If you add non-bool settings,
> the cast `property.GetValue(settings) as bool?` yields `null` → `false` and the checkbox would
> misrepresent them. Keep `Settings` boolean-only, or filter to `property.PropertyType == typeof(bool)`.

### 7.2 `SettingsBlade.razor.cs` (code-behind)

Responsibilities:
- Subscribe to the event bus in `OnInitialized`; unsubscribe in `Dispose`.
- On a `BladeEventArgs(Settings)` → open the inner container; on any other `BladeEventArgs` → close it.
- `HandleSettingPressed`: persist the value; if it was the dark-mode property, apply the theme via JS and
  broadcast `ThemeChangedEventArgs`.
- `HandleLeaveGamePressed`: for multiplayer, call the API to leave; then navigate to the lobby. (Solo just
  navigates.)
- `GameId` is pulled from the query string (`[SupplyParameterFromQuery]`) for the multiplayer leave call.

```csharp
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;
using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Blades;

public partial class SettingsBlade : ComponentBase, IDisposable
{
    [SupplyParameterFromQuery] public string? GameId { get; set; }

    [Inject] ILogger<SettingsBlade> Logger { get; set; } = null!;
    [Inject] IEventNotificationService EventNotificationService { get; set; } = null!;
    [Inject] IApplicationViewModel ApplicationViewModel { get; set; } = null!;
    [Inject] ISettingsService SettingsService { get; set; } = null!;
    [Inject] NavigationManager NavigationManager { get; set; } = null!;
    [Inject] IGameplayEndpointsService GameplayEndpointsService { get; set; } = null!;
    [Inject] ICommonJsInterop CommonJsInterop { get; set; } = null!;

    BladeContainer? _bladeContainer;

    protected override void OnInitialized()
    {
        EventNotificationService.EventReceived += HandleEventReceived;
        base.OnInitialized();
    }

    public void Dispose()
    {
        EventNotificationService.EventReceived -= HandleEventReceived;
        GC.SuppressFinalize(this);
    }

    async Task HandleEventReceived(object sender, IEventArgs e)
    {
        switch (e)
        {
            case BladeEventArgs { Type: BladeEventArgs.Types.Settings, }:
                _bladeContainer?.Open();
                break;
            case BladeEventArgs { Type: not BladeEventArgs.Types.Settings, }:
                _bladeContainer?.Close();
                break;
            default:
                Logger.LogWarning("Event handler's switch statement fell through.");
                break;
        }
        await Task.CompletedTask;
    }

    async void HandleSettingPressed(string propertyName, bool val)
    {
        SettingsService.SetSettingValue(propertyName, val);

        // Apply theme immediately when dark mode setting changes
        if (propertyName == nameof(Settings.IsDarkModeEnabled))
        {
            var themeName = val ? "dark" : "light";
            await CommonJsInterop.SetThemeAsync(themeName);

            // Notify other components (e.g., MainLayout) to update their theme
            EventNotificationService.PostEvent(this, new ThemeChangedEventArgs { IsDarkMode = val });
        }
    }

    async Task HandleLeaveGamePressed()
    {
        try
        {
            if (NavigationManager.IsOnMultiGameplay())
            {
                // Multiplayer - call API to leave game
                if (GameId is { Length: > 0 })
                {
                    await GameplayEndpointsService.LeaveGameAsync(GameId, ApplicationViewModel.Player.Id);
                }
                else
                {
                    Logger.LogWarning("Player unable to leave multiplayer game because their GameId was null or empty.");
                }
            }
            // For both multi and solo, navigate to lobby
            NavigationManager.NavigateToLobby();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred.");
        }
    }
}
```

> **Notes & faithful-reproduction caveats.**
> - `HandleSettingPressed` is `async void` — acceptable here because it is a UI event handler, but it means
>   exceptions in the theme path are unobserved. Consider `async Task` + `EventCallback` if you harden it.
> - The default `switch` arm is unreachable given the two `BladeEventArgs` patterns cover the enum, and the
>   handler also receives **non**-blade events (e.g. `ThemeChangedEventArgs` posted by this very component).
>   Such an event matches neither `case` and hits `default` → logs a warning. **This is a real noise
>   source**: every theme toggle posts a `ThemeChangedEventArgs` that this handler then warns about. To
>   suppress, add `case not BladeEventArgs: break;` (or `default: break;`) instead of logging, or filter to
>   `if (e is not BladeEventArgs) return;` at the top.
> - The blade is rendered once, globally, by `MainLayout` (§12) — it is **not** placed per-page.

### 7.3 `SettingsBlade.razor.css` (scoped styles)

```css
.blade-content {
    display: flex;
    flex-direction: column;
    justify-content: space-between;
    height: 100%;
}

.settings-list {
    display: flex;
    flex-direction: column;
    gap: 8px;
    justify-content: center;
    align-items: flex-start;
}

::deep .leave-game-btn {
    max-width: 450px;
    height: 50px;
}
```

---

## 8. FABs — the triggers

### 8.1 `SettingsFab.razor` — `Client.Shared/Components/FABs/SettingsFab.razor`

A single Material FAB (gear icon). Clicking it posts the open event. This is the **only** required trigger
for the blade.

```razor
@using ChefKnifeStudios.PokerAttack.Client.Core.Services
@using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs
@inject IEventNotificationService NotificationService

<MatFAB
    Id="settingsFab"
    Icon="settings"
    OnClick="HandlePressed"
/>

@code {
    void HandlePressed()
    {
        NotificationService.PostEvent(
            this,
            new BladeEventArgs() { Type = BladeEventArgs.Types.Settings, }
        );
    }
}
```

### 8.2 `FabList` — positioning container (optional but recommended)

`FabList` is a layout wrapper that stacks FABs in a chosen screen corner/direction, with mobile overrides.
It is how `SettingsFab` is actually placed on pages (`<FabList Fabs="[ FabList.FABs.Settings, FabList.FABs.Help ]" />`).
If you only need the settings trigger you may drop `FabList` and `HelpFab` and place `<SettingsFab/>`
directly — but the original uses `FabList`, reproduced here in full.

**`FabList.razor`:**

```razor
@using ChefKnifeStudios.PokerAttack.Client.Shared.Components.Blades

<div class="fab-list @GetPositionClass() @GetDirectionClass() @GetMobilePositionClass() @GetMobileDirectionClass()">
    @foreach (var fab in Fabs)
    {
        switch (fab)
        {
            case FABs.Settings:
                {
                    <SettingsFab />
                }
                break;
            case FABs.Help:
                {
                    <HelpFab />
                }
                break;
            default:
                break;
        }
    }
</div>
```

**`FabList.razor.cs`:**

```csharp
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.FABs;

public partial class FabList : ComponentBase
{
    [Parameter] public FABs[] Fabs { get; set; } = [];
    [Parameter] public Corner Position { get; set; } = Corner.BottomRight;
    [Parameter] public StackDirection Direction { get; set; } = StackDirection.Vertical;
    [Parameter] public Corner? MobilePosition { get; set; }
    [Parameter] public StackDirection? MobileDirection { get; set; }

    public enum FABs
    {
        Settings,
        Help,
    }

    public enum Corner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    public enum StackDirection
    {
        Vertical,
        Horizontal,
    }

    string GetPositionClass() => Position switch
    {
        Corner.TopLeft => "fab-list--top-left",
        Corner.TopRight => "fab-list--top-right",
        Corner.BottomLeft => "fab-list--bottom-left",
        _ => "fab-list--bottom-right"
    };

    string GetDirectionClass() => Direction switch
    {
        StackDirection.Horizontal => "fab-list--horizontal",
        _ => "fab-list--vertical"
    };

    string GetMobilePositionClass() => MobilePosition switch
    {
        Corner.TopLeft => "fab-list--mobile-top-left",
        Corner.TopRight => "fab-list--mobile-top-right",
        Corner.BottomLeft => "fab-list--mobile-bottom-left",
        Corner.BottomRight => "fab-list--mobile-bottom-right",
        _ => ""
    };

    string GetMobileDirectionClass() => MobileDirection switch
    {
        StackDirection.Horizontal => "fab-list--mobile-horizontal",
        StackDirection.Vertical => "fab-list--mobile-vertical",
        _ => ""
    };
}
```

**`FabList.razor.css`:**

```css
.fab-list {
    position: fixed;
    z-index: 1;
    display: flex;
    gap: 1rem;
}

/* Corner positions */
.fab-list--bottom-right { right: 1rem; bottom: 1rem; }
.fab-list--bottom-left  { left: 1rem;  bottom: 1rem; }
.fab-list--top-right    { right: 1rem; top: 1rem; }
.fab-list--top-left     { left: 1rem;  top: 1rem; }

/* Stack directions - bottom corners stack upward, right corners stack leftward */
.fab-list--vertical   { flex-direction: column-reverse; }
.fab-list--horizontal { flex-direction: row-reverse; }

/* Top corners stack downward */
.fab-list--top-left.fab-list--vertical,
.fab-list--top-right.fab-list--vertical { flex-direction: column; }

/* Left corners stack rightward */
.fab-list--top-left.fab-list--horizontal,
.fab-list--bottom-left.fab-list--horizontal { flex-direction: row; }

/* Mobile overrides (max-width: 768px) */
@media (max-width: 768px) {
    .fab-list--mobile-bottom-right { right: 1rem; bottom: 1rem; left: auto; top: auto; }
    .fab-list--mobile-bottom-left  { left: 1rem;  bottom: 1rem; right: auto; top: auto; }
    .fab-list--mobile-top-right    { right: 1rem; top: 1rem; left: auto; bottom: auto; }
    .fab-list--mobile-top-left     { left: 1rem;  top: 1rem; right: auto; bottom: auto; }

    .fab-list--mobile-vertical   { flex-direction: column-reverse; }
    .fab-list--mobile-horizontal { flex-direction: row-reverse; }

    .fab-list--mobile-top-left.fab-list--mobile-vertical,
    .fab-list--mobile-top-right.fab-list--mobile-vertical { flex-direction: column; }

    .fab-list--mobile-top-left.fab-list--mobile-horizontal,
    .fab-list--mobile-bottom-left.fab-list--mobile-horizontal { flex-direction: row; }
}

/* Override child FAB positioning since parent handles it */
::deep .app-fab--absolute { position: static; }
```

### 8.3 `HelpFab.razor` (sibling — optional, only if you keep `FabList`'s `Help` case)

`HelpFab` is unrelated to the blade but is referenced by `FabList`. Include it if you keep the `Help`
enum/case; otherwise remove both the enum value and the `case FABs.Help` arm. Reproduced for completeness;
it depends on `ICommonJsInterop.GetViewPortSizeAsync`, a `ScreenSizeHelper`, and a `HowToPlayModalEventArgs`
that are **out of scope** for the settings blade. If you drop `HelpFab`, also drop those.

```razor
@using ChefKnifeStudios.PokerAttack.Client.Core.Services
@using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs.ModalEvents
@using ChefKnifeStudios.PokerAttack.Client.Shared.Helpers
@using ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop
@inject IEventNotificationService EventNotificationService
@inject ICommonJsInterop CommonJsInterop
@inject NavigationManager NavigationManager

<MatFAB Id="helpFab"
    Icon="help_outline"
    OnClick="HandlePressed"
/>

@code {
    async Task HandlePressed()
    {
        var screenSize = await CommonJsInterop.GetViewPortSizeAsync();
        var breakpoint = ScreenSizeHelper.GetBreakpoint(screenSize);

        switch (breakpoint)
        {
            case ScreenSizeBreakpoint.MobilePortrait:
            case ScreenSizeBreakpoint.MobileLandscape:
            case ScreenSizeBreakpoint.Tablet:
                NavigationManager.NavigateTo("https://docs.google.com/document/d/.../edit", forceLoad: true);
                break;
            case ScreenSizeBreakpoint.Desktop:
            case ScreenSizeBreakpoint.LargeDesktop:
            default:
                EventNotificationService.PostEvent(this, new HowToPlayModalEventArgs()
                {
                    ModalAction = ModalEventArgs.ModalActions.Open,
                });
                break;
        }
    }
}
```

---

## 9. JS interop — `ICommonJsInterop` (theme + outside-click)

The blade uses exactly **three** members of `ICommonJsInterop`:
`SetThemeAsync`, `AddOutsideClickListenerAsync`, and `RemoveOutsideClickListenerAsync`. The full interface
in the source is large; below is a **trimmed** interface containing only what the blade needs, plus the
class implementation for those members, plus the JS module functions they call. If you already have a JS
interop service in your target project, just add these members; otherwise create a minimal service.

### 9.1 Minimal `ICommonJsInterop` (blade-only subset) — `Client.Shared/Services/JsInterop/CommonJsInterop.cs`

```csharp
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;

public interface ICommonJsInterop
{
    Task SetThemeAsync(string themeName);
    Task AddOutsideClickListenerAsync(string elementId, Action callback);
    Task RemoveOutsideClickListenerAsync(string elementId);
}

public class CommonJsInterop : ICommonJsInterop, IAsyncDisposable
{
    readonly Lazy<Task<IJSObjectReference>> moduleTask;
    readonly ILogger<CommonJsInterop> _logger;

    public CommonJsInterop(
        IJSRuntime jsRuntime,
        IWebAssemblyHostEnvironment environment,
        ILogger<CommonJsInterop> logger)
    {
        _logger = logger;
        string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? ".";

        // Cache-busting query string (?g=<guid>) forces a fresh module load each session.
        moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            $"./_content/{assemblyName}/scripts/commonJsInterop.js?g={Guid.NewGuid().ToString().ToLower()}")
            .AsTask());
    }

    public async ValueTask DisposeAsync()
    {
        if (moduleTask.IsValueCreated)
        {
            var module = await moduleTask.Value;
            await module.DisposeAsync();
        }
    }

    public async Task SetThemeAsync(string themeName)
    {
        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("setTheme", themeName);
        }
        catch (Exception ex) { LogError(ex); }
    }

    // Tracks each element's .NET callback + the JS listener handle so we can remove the right one later.
    readonly Dictionary<string, (Action Callback, object? Listener)> _outsideClickCallbackDict = new();

    public async Task AddOutsideClickListenerAsync(string elementId, Action callback)
    {
        try
        {
            var module = await moduleTask.Value;

            // JS attaches a document click listener and returns its handle (an opaque JS ref).
            var listener = await module.InvokeAsync<object>(
                "addOutsideClickListener",
                elementId,
                DotNetObjectReference.Create(this) // JS calls back into HandleOutsideClick on this instance
            );

            _outsideClickCallbackDict[elementId] = (callback, listener);
        }
        catch (Exception ex) { LogError(ex); }
    }

    // Invoked from JS when a click lands outside the tracked element.
    [JSInvokable]
    public void HandleOutsideClick(string elementId)
    {
        if (_outsideClickCallbackDict.TryGetValue(elementId, out var callbackData))
        {
            callbackData.Callback.Invoke();
        }
    }

    public async Task RemoveOutsideClickListenerAsync(string elementId)
    {
        try
        {
            if (_outsideClickCallbackDict.TryGetValue(elementId, out var callbackData)
                && callbackData.Listener is not null)
            {
                var module = await moduleTask.Value;
                await module.InvokeVoidAsync("removeOutsideClickListener", callbackData.Listener);
                _outsideClickCallbackDict.Remove(elementId);
            }
        }
        catch (Exception ex) { LogError(ex); }
    }

    void LogError(Exception ex)
    {
        _logger.LogError(ex, "CommonInterop encountered a JavaScript error: {errorMessage}", ex.Message);
    }
}
```

> The lazy ES-module load pattern (`Lazy<Task<IJSObjectReference>>` + dynamic `import` with a cache-busting
> `?g=<guid>`) is the project's standard JS interop idiom. `IWebAssemblyHostEnvironment` is injected to
> match the original constructor signature even though this subset doesn't use it; you may drop it if you
> don't need environment-conditional behavior.

### 9.2 JS module functions — `Client.Shared/wwwroot/scripts/commonJsInterop.js`

Only the functions the blade calls are reproduced. The file is an ES module (functions are `export`ed) and
served as a static web asset from the Razor class library (hence the `_content/<AssemblyName>/` path).

```javascript
export function addOutsideClickListener(elementId, dotNetHelper) {
    const listener = (event) => {
        const element = document.getElementById(elementId);
        if (element && !element.contains(event.target)) {
            dotNetHelper.invokeMethodAsync('HandleOutsideClick', elementId);
        }
    };

    // Attach the listener to the document
    document.addEventListener('click', listener);

    // Return the listener so it can be removed later
    return listener;
}

export function removeOutsideClickListener(listener) {
    // Remove the specific listener passed in
    document.removeEventListener('click', listener);
}

// Known theme classes - add new themes here as they're created
const THEMES = ['light', 'dark'];

export function setTheme(themeName) {
    try {
        // Remove all known theme classes
        document.body.classList.remove(...THEMES);
        // Add the new theme class (if not 'light', which is the default with no class)
        if (themeName && themeName !== 'light') {
            document.body.classList.add(themeName);
        }
    } catch (e) { }
}
```

> **How the outside-click round-trip works:** `AddOutsideClickListenerAsync` passes a
> `DotNetObjectReference.Create(this)` into JS. The JS `addOutsideClickListener` returns the actual
> listener function as an opaque handle, which .NET stores. When a document click lands outside the
> element, JS calls back `HandleOutsideClick(elementId)` on the .NET instance, which looks up and invokes
> the stored `Action` (which is `BladeContainer.HandleClosePressed`). `removeOutsideClickListener` is
> passed that same handle to `document.removeEventListener` exactly. **Important:** the `setTheme` contract
> is that `'light'` is the *absence* of a body class and `'dark'` (or any future theme) is a class on
> `<body>`. The CSS `.dark { ... }` block (§13) supplies the dark palette.

---

## 10. Navigation helpers & ApplicationViewModel (LEAVE GAME path)

These are only needed if you keep the **LEAVE GAME** button. If your target app has no gameplay concept,
delete the `@if (NavigationManager.IsOnGameplay())` block and `HandleLeaveGamePressed`, and you can skip
this entire section (and the `IApplicationViewModel` / `IGameplayEndpointsService` injections).

### 10.1 `NavigationManagerExtensions` — `Client.Shared/Extensions/NavigationManagerExtensions.cs`

Declared in namespace `Microsoft.AspNetCore.Components` so the extension methods are available wherever
`NavigationManager` is in scope without an extra `using`. The blade uses `IsOnGameplay`,
`IsOnMultiGameplay`, and `NavigateToLobby`.

```csharp
namespace Microsoft.AspNetCore.Components;

public static class NavigationManagerExtensions
{
    public static void NavigateToLobby(this NavigationManager navManager)
    {
        navManager.NavigateTo("/", replace: true);
    }

    public static void NavigateToLobbyWithMultiGameResult(this NavigationManager navManager, string gameResult)
    {
        navManager.NavigateTo($"/?multi-gameresult={gameResult}", replace: true);
    }

    public static void NavigateToLobbyWithSoloGameResult(this NavigationManager navManager)
    {
        navManager.NavigateTo("/?solo-gameresult=show", replace: true);
    }

    public static void NavigateToGameplay(this NavigationManager navManager, string gameId)
    {
        navManager.NavigateTo($"/multi-gameplay?gameid={gameId}", replace: true);
    }

    public static bool IsOnGameplay(this NavigationManager navManager)
    {
        return navManager.Uri.Contains("gameplay", StringComparison.InvariantCultureIgnoreCase);
    }

    public static bool IsOnMultiGameplay(this NavigationManager navManager)
    {
        return navManager.Uri.Contains("multi-gameplay", StringComparison.InvariantCultureIgnoreCase);
    }

    public static bool IsOnSoloGameplay(this NavigationManager navManager)
    {
        return navManager.Uri.Contains("solo-gameplay", StringComparison.InvariantCultureIgnoreCase);
    }

    public static void NavigateToSoloGameplay(this NavigationManager navManager)
    {
        navManager.NavigateTo("/solo-gameplay", replace: true);
    }
}
```

> `IsOnGameplay` simply substring-matches `"gameplay"` in the current URL. Routes are `"/multi-gameplay"`
> and `"/solo-gameplay"`. Adapt these strings to your routing scheme.

### 10.2 `IApplicationViewModel` (only `Player.Id` is used)

The blade injects `IApplicationViewModel` solely to read `ApplicationViewModel.Player.Id` for the leave-game
API call. The full view model is large and game-specific. The **minimal contract** the blade needs:

```csharp
public interface IApplicationViewModel
{
    PlayerDTO Player { get; }   // where PlayerDTO has at least: string Id { get; }
}
```

In the original, `ApplicationViewModel` is a `CommunityToolkit.Mvvm` `BaseViewModel`/`ObservableObject`
that, in its constructor, assigns `Player.Id = Guid.NewGuid().ToString()` and loads/generates a player name
from local storage. For the blade's purposes, any object exposing a stable `Player.Id` string suffices.
Register it scoped (see §11). The rest of `ApplicationViewModel` (SignalR wiring, game settings load,
browser-close registration) is **out of scope** for the settings blade.

### 10.3 `IGameplayEndpointsService.LeaveGameAsync` (multiplayer leave)

The blade calls:

```csharp
Task<Result<Discard>> LeaveGameAsync(string gameId, string playerId, CancellationToken cancellationToken = default);
```

`Result<T>` is the project's result wrapper and `Discard` is an empty "no payload" marker type. The blade
ignores the return value (it does not inspect success). The concrete HTTP implementation is out of scope;
for a port, supply any service that performs your "leave game" call with `(gameId, playerId)` and returns
something awaitable. If your app has no multiplayer, delete this injection and the `IsOnMultiGameplay`
branch.

---

## 11. Dependency injection (composition root) — `Client.WebApp/Program.cs`

Register the following for the blade to resolve. Lifetimes matter: **`IEventNotificationService` MUST be a
singleton** so the FAB, blade, and layout share one bus. `ICommonJsInterop` is a singleton (it holds the
lazy JS module + the outside-click dictionary). `ISettingsService` is transient. `IApplicationViewModel` is
scoped. Blazored local storage is registered with one call.

```csharp
// --- Event bus (MUST be singleton so all subscribers share one instance) ---
builder.Services.AddSingleton<IEventNotificationService, EventNotificationService>();

// --- JS interop (singleton: owns lazy module + outside-click registry) ---
builder.Services.AddSingleton<ICommonJsInterop, CommonJsInterop>();

// --- Settings ---
builder.Services.AddTransient<ISettingsService, SettingsService>();
builder.Services.AddBlazoredLocalStorage();   // provides ISyncLocalStorageService (and async variant)

// --- Only needed if keeping the LEAVE GAME button ---
builder.Services.AddScoped<IApplicationViewModel, ApplicationViewModel>();
builder.Services.AddTransient<IGameplayEndpointsService, GameplayEndpointsService>();

// --- MatBlazor component library + (optional) toaster ---
builder.Services.AddMatBlazor();
```

For reference, the original `Program.cs` registers these alongside many unrelated services. The blade-
relevant lines, copied verbatim from source:

```csharp
builder.Services.AddSingleton<ICommonJsInterop, CommonJsInterop>();
builder.Services.AddSingleton<IEventNotificationService, EventNotificationService>();
builder.Services.AddTransient<ISettingsService, SettingsService>();
builder.Services.AddTransient<IGameplayEndpointsService, GameplayEndpointsService>();
builder.Services.AddMatBlazor();
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddScoped<IApplicationViewModel, ApplicationViewModel>();
```

> **Blazor WASM lifetime note.** In a WASM app there is effectively one DI scope per browser session, so
> `Scoped` and `Singleton` behave similarly; the original mixes them. The hard requirement is that
> `IEventNotificationService` is a single shared instance — `Singleton` guarantees this regardless.

---

## 12. Hosting the blade — `Client.WebApp/Layout/MainLayout.razor`

The blade is placed **once** in the layout so it is available on every page. The layout also owns the
`MatThemeProvider` and reacts to `ThemeChangedEventArgs` (posted by the blade) to swap the MatBlazor theme
object. On first render it applies the persisted theme via JS (`setTheme`) so the `<body>` class matches
stored settings before the user touches anything.

```razor
@using ChefKnifeStudios.PokerAttack.Client.Core.Services
@using ChefKnifeStudios.PokerAttack.Client.Shared.Components.Blades
@using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs
@using ChefKnifeStudios.PokerAttack.Client.Shared.Services
@using ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop

@inherits LayoutComponentBase
@implements IDisposable
@inject ISettingsService SettingsService
@inject ICommonJsInterop CommonJsInterop
@inject IEventNotificationService EventNotificationService

<div class="page bgclr-surface1">
    <MatThemeProvider Theme="@_theme">
        @Body

        <SettingsBlade />
        @* <ModalController /> and <MatToastContainer /> are app-specific; omit if unused *@
    </MatThemeProvider>
</div>

@code
{
    MatTheme _theme = null!;

    static readonly MatTheme LightTheme = new()
    {
        Background  = ColorConstants.Light.Background,
        OnPrimary   = ColorConstants.Light.OnPrimary,
        OnSecondary = ColorConstants.Light.OnSecondary,
        OnSurface   = ColorConstants.Light.OnSurface,
        Primary     = ColorConstants.Light.Primary,
        Secondary   = ColorConstants.Light.Secondary,
        Surface     = ColorConstants.Light.Surface,
    };

    static readonly MatTheme DarkTheme = new()
    {
        Background  = ColorConstants.Dark.Background,
        OnPrimary   = ColorConstants.Dark.OnPrimary,
        OnSecondary = ColorConstants.Dark.OnSecondary,
        OnSurface   = ColorConstants.Dark.OnSurface,
        Primary     = ColorConstants.Dark.Primary,
        Secondary   = ColorConstants.Dark.Secondary,
        Surface     = ColorConstants.Dark.Surface,
    };

    protected override void OnInitialized()
    {
        var settings = SettingsService.GetSettings();
        _theme = settings.IsDarkModeEnabled ? DarkTheme : LightTheme;

        EventNotificationService.EventReceived += HandleEventReceived;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var settings = SettingsService.GetSettings();
            var themeName = settings.IsDarkModeEnabled ? "dark" : "light";
            await CommonJsInterop.SetThemeAsync(themeName);
        }
    }

    async Task HandleEventReceived(object sender, IEventArgs e)
    {
        if (e is ThemeChangedEventArgs themeArgs)
        {
            _theme = themeArgs.IsDarkMode ? DarkTheme : LightTheme;
            await InvokeAsync(StateHasChanged);   // marshal back to the renderer
        }
    }

    public void Dispose()
    {
        EventNotificationService.EventReceived -= HandleEventReceived;
    }
}
```

> `ColorConstants.Light.*` / `ColorConstants.Dark.*` are app-specific color constant classes feeding the
> MatBlazor `MatTheme`. You must supply your own palette (string hex values) for `Background`, `Primary`,
> `Secondary`, `Surface`, `OnPrimary`, `OnSecondary`, `OnSurface`. They are independent of the CSS custom
> properties in §13 — MatBlazor inlines its own theme styles, while §13 drives the project's own utility
> classes and the blade's `var(--clr-*)` colors. Keep both in sync for a coherent look.
>
> **Two parallel theming mechanisms** (reproduce both): (1) the `setTheme` JS call toggles a `dark` class
> on `<body>`, which activates the `.dark { --clr-*: ... }` block (§13) consumed by the blade's CSS
> `var(--clr-surface2)` / `var(--clr-1p)` etc.; (2) `MatThemeProvider`/`MatTheme` re-styles MatBlazor
> components. The dark-mode toggle path triggers **both**: JS class swap (immediate, via the blade) +
> `ThemeChangedEventArgs` → layout swaps `_theme` (re-renders MatBlazor).

### 12.1 Placing the FAB trigger

The blade has no visible trigger of its own — a `SettingsFab` (usually inside a `FabList`) must exist on
any page where the user should be able to open settings. Example, on a page:

```razor
<FabList Fabs="[ FabList.FABs.Settings, FabList.FABs.Help ]" />
```

Or, minimally, drop `<SettingsFab />` anywhere in your layout/page. Because the FAB communicates purely
through the singleton event bus, it does not need to be near the blade in the component tree.

---

## 13. Theme CSS custom properties — `Client.WebApp/wwwroot/css/variables.css`

The blade's scoped CSS reads `var(--clr-surface2)` (background), `var(--clr-1p)` (close-button color), and
the `clr-on-1t-container` utility class for the "Settings" heading. These come from CSS custom properties
defined on `:root` (light, default) and `.dark` (activated by `setTheme('dark')` adding the `dark` class to
`<body>`). Below are the **minimum** variables the blade needs, plus the utility classes it uses. (The full
file defines a complete Material 3 palette; only the blade-relevant subset is required, but reproducing the
whole palette is recommended so the rest of the UI themes correctly.)

```css
:root {
    /* Primary (close button color) */
    --clr-1p: #214E7E;
    --clr-on-1t-container: #211634;   /* "Settings" heading color via .clr-on-1t-container */

    /* Surfaces (blade background uses --clr-surface2) */
    --clr-surface1: #F5F9FF;
    --clr-surface2: #EFF5FF;
    /* ... (rest of light palette) ... */
}

/* Activated when setTheme('dark') adds `dark` class to <body> */
.dark {
    --clr-1p: #D3E3FD;
    --clr-on-1t-container: #C2E7FF;

    --clr-surface1: #131314;
    --clr-surface2: #1E1F20;
    /* ... (rest of dark palette) ... */
}

/* Utility classes used in markup */
.clr-on-1t-container { color: var(--clr-on-1t-container); }
.bgclr-surface1      { background-color: var(--clr-surface1); }
.clr-1p              { color: var(--clr-1p); }
```

> The blade's CSS uses fallbacks (`var(--clr-surface2, #f2ecee)`, `var(--clr-1p, #6442d6)`) so it renders
> acceptably even if these variables are undefined — but for correct theming, define them. Ensure
> `variables.css` is linked from your host page (`index.html` / `App.razor` head) **before** the app
> stylesheet so the variables are available globally.

---

## 14. Step-by-step reimplementation checklist

Follow in order. Each step is independently verifiable.

1. **Packages.** Add NuGet refs: `MatBlazor 2.10.0`, `Blazored.LocalStorage 4.5.0`,
   `CommunityToolkit.Mvvm 8.4.0`, `Microsoft.AspNetCore.Components.Web 10.0.0` (§3).
2. **Event bus.** Create `IEventNotificationService` + `EventNotificationService` + `IEventArgs` (§5.1).
   Register as **singleton** (§11).
3. **Event payloads.** Create `BladeEventArgs` (§5.2) and `ThemeChangedEventArgs` (§5.3). Note the
   namespace `...EventArgs` shadows `System.EventArgs`.
4. **Settings model + persistence.** Create `Settings` (§4.1), `LocalStorageConstants` (§4.2),
   `SettingsService`/`ISettingsService` (§4.3). Register `AddBlazoredLocalStorage()` and
   `ISettingsService` (§11).
5. **JS interop.** Create `ICommonJsInterop`/`CommonJsInterop` with the three blade members (§9.1) and the
   JS module `wwwroot/scripts/commonJsInterop.js` with `setTheme`, `addOutsideClickListener`,
   `removeOutsideClickListener` (§9.2). Register `ICommonJsInterop` singleton (§11). Confirm the static
   web asset path `_content/<AssemblyName>/scripts/commonJsInterop.js` resolves (RCL static assets).
6. **Theme CSS.** Add `variables.css` with `:root` + `.dark` custom properties and utility classes (§13);
   link it in the host head.
7. **BladeContainer.** Create the three `BladeContainer` files (§6). Decide whether to keep the GUID quirk
   or apply the cached-id fix.
8. **SettingsBlade.** Create the three `SettingsBlade` files (§7). If you don't need LEAVE GAME, remove the
   gameplay block, `HandleLeaveGamePressed`, and the `IApplicationViewModel`/`IGameplayEndpointsService`
   injections — and skip step 9.
9. **(Optional) LEAVE GAME deps.** Add `NavigationManagerExtensions` (§10.1), an `IApplicationViewModel`
   exposing `Player.Id` (§10.2), and an `IGameplayEndpointsService.LeaveGameAsync` (§10.3). Register them
   (§11).
10. **FAB trigger.** Create `SettingsFab` (§8.1) and, optionally, `FabList`(+`HelpFab`) (§8.2/8.3). Place a
    `SettingsFab`/`FabList` on the relevant page(s).
11. **Host the blade.** Add `<SettingsBlade />` once inside `MainLayout`, wrap content in
    `MatThemeProvider`, wire the `ThemeChangedEventArgs` handler and first-render `setTheme` (§12). Supply
    `ColorConstants` / `MatTheme` palettes.
12. **Verify** (see §15).

---

## 15. Acceptance criteria / manual test plan

1. **Open/close via FAB.** Click the settings FAB → blade slides in from the right. Click the ✕ → blade
   slides out. Click the FAB again, then click anywhere outside the blade → it closes (outside-click
   listener). The opening click itself must NOT immediately close it (300 ms guard).
2. **Checkbox rendering.** The blade shows one checkbox per `Settings` bool, each labeled with its
   `[Description]` ("Audio Enabled", "Always Show App Tour", "Enable Dark Mode").
3. **Persistence.** Toggle any checkbox, refresh the page, reopen the blade → the toggled state persists
   (local storage key `"Setting"`).
4. **Dark mode immediacy.** Toggle "Enable Dark Mode" → `<body>` gains/loses the `dark` class instantly
   (blade colors flip via `var(--clr-*)`), AND MatBlazor components re-theme (layout swaps `MatTheme` via
   `ThemeChangedEventArgs`). No refresh required.
5. **Theme on load.** With dark mode previously enabled, reload → on first render the `dark` body class and
   the `MatTheme` are both applied before interaction.
6. **LEAVE GAME visibility.** The LEAVE GAME button appears only when the URL contains `"gameplay"`. On a
   multiplayer URL with a `gameid` query param, clicking it calls the leave API then navigates to `/`. On
   solo gameplay it just navigates to `/`.
7. **No leaks.** Navigating away disposes the blade/container/layout and unsubscribes from the event bus
   and removes the JS outside-click listener (verify no duplicate handlers accumulate across navigations).

---

## 16. Summary of intentional quirks to decide on (don't reproduce blindly)

| # | Location | Quirk | Recommended action |
|---|---|---|---|
| 1 | `BladeContainer._elementId` (§6.2) | Uses `new Guid()` (empty GUID), recomputed each access → constant id; collides if >1 blade. | Cache a real `Guid.NewGuid()` once. |
| 2 | `SettingsBlade.HandleEventReceived` (§7.2) | Non-blade events (incl. its own `ThemeChangedEventArgs`) hit the `default` arm and log a warning. | Add `if (e is not BladeEventArgs) return;` or a `default: break;`. |
| 3 | `SettingsBlade.HandleSettingPressed` (§7.2) | `async void` handler; theme-path exceptions unobserved. | Acceptable for UI events; harden with `EventCallback`/`async Task` if desired. |
| 4 | `SettingsBlade` reflection (§7.1) | `GetProperties()` + `as bool?` silently renders non-bool settings as unchecked. | Filter `PropertyType == typeof(bool)` if `Settings` may gain non-bool members. |
| 5 | `LocalStorageConstants.SettingsKey` (§4.2) | Value is `"Setting"` (singular). | Keep for storage compatibility; rename only on a fresh install. |

---

*End of document.*
