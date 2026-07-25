# Contract: `BackfillTextureFab` + persistence honoring

## Component: `Components/FABs/BackfillTextureFab.razor` (+ `.razor.css`)

Structured like `CityFab` (a `MatFAB` opening a `MatMenu` list), wired like `AudioFab`
(read persisted setting → on select, persist + apply). **No event bus.**

```razor
@inject ISettingsService SettingsService
@inject ITransitSynthJsInterop TransitSynth
@inject IStringLocalizer<RouteFilterResources> L

<div class="backfill-texture-fab-container">
    <MatFAB Icon="graphic_eq" Mini="true" OnClick="OpenMenu" @ref="_button" />
    <MatMenu @ref="_menu">
        <MatList>
            <MatListItem>
                <MatButton Label="@L[\"BackfillNoise\"]" Mini="true"
                           @onclick="() => Select(BackfillTexture.Noise)"
                           Disabled="@(_current == BackfillTexture.Noise)" />
            </MatListItem>
            <MatListItem>
                <MatButton Label="@L[\"BackfillPercussion\"]" Mini="true"
                           @onclick="() => Select(BackfillTexture.Percussion)"
                           Disabled="@(_current == BackfillTexture.Percussion)" />
            </MatListItem>
        </MatList>
    </MatMenu>
</div>
```

```csharp
BackfillTexture _current;
MatFAB _button;
BaseMatMenu _menu;

protected override void OnInitialized() =>
    _current = SettingsService.GetSettings().BackfillTexture;

void OpenMenu(MouseEventArgs e) => _menu.OpenAsync(_button.Ref);

async Task Select(BackfillTexture mode)
{
    SettingsService.SetSettingValue(nameof(Settings.BackfillTexture), mode);
    _current = mode;
    await TransitSynth.SetBackfillTextureAsync(mode.ToString().ToLowerInvariant());
}
```

**Requirements**:
- **FAB-1**: reads the persisted `BackfillTexture` in `OnInitialized`; the active
  option renders `Disabled` (a re-select is a no-op — FR-011).
- **FAB-2**: on select → persist via `SetSettingValue` **then** push to JS via
  `SetBackfillTextureAsync` (order: persist first so a mid-swap reload is consistent).
- **FAB-3**: **no** `IEventNotificationService` post — nothing else consumes the choice
  (YAGNI). Add an event-args type only if a second consumer appears.
- **FAB-4**: labels come from `IStringLocalizer<RouteFilterResources>` — never inline
  (Principle XII).
- **FAB-5** (Principle XIII): any color-bearing rule in `.razor.css` ships light + dark
  renderings in the same change; mirror the sibling FABs' CSS. MatFAB/MatMenu inherit
  `MatThemeProvider` theming.

## Mount: `Layout/MainLayout.razor`

Add `<BackfillTextureFab />` inside the `<MatThemeProvider>` block alongside the
existing `<AudioFab/>`, `<MapStyleFab/>`, `<DarkModeFab/>`, `<InfoFab/>`, `<CityFab/>`,
`<SettingsFab/>`.

- **MOUNT-1**: mounted once, within the theme provider (so it themes correctly).

## Init honoring: `Pages/TransitMap.razor.cs`

Beside the existing `SetAudioEnabledAsync` push (~line 110, inside the block that reads
`SettingsService.GetSettings()`):

```csharp
var settings = SettingsService.GetSettings();
_audioEnabled = settings.IsAudioEnabled;
// ...
_ = TransitSynth.SetAudioEnabledAsync(_audioEnabled);
_ = TransitSynth.SetBackfillTextureAsync(settings.BackfillTexture.ToString().ToLowerInvariant());
```

**Requirements**:
- **INIT-1** (FR-006): the persisted texture is pushed to JS on startup so the saved
  choice is heard from the first unlock.
- **INIT-2**: ordering relative to unlock/warm is NOT fragile — `setBackfillTexture` is
  safe before the master bus exists (records the flag; honored on build). Fire-and-forget
  (`_ =`) like the sibling call; MUST NOT block init.

## Resources: `Resources/RouteFilterResources.resx` (EN only)

Add keys (EN values; `.es` deferred per 015/016/017):

| Key | EN value (suggested) |
|---|---|
| `BackfillNoise` | `Ambient noise` |
| `BackfillPercussion` | `Lo-fi percussion` |

- **RES-1**: keys live in the single canonical `RouteFilterResources.resx` (Principle
  XII); no second resource file.
