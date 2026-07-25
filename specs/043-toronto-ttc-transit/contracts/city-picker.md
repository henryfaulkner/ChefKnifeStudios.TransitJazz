# Contract: TTC City Picker Entry

The Toronto entry in `Client/…Client.Shared/Components/FABs/CityFab.razor`.

## Menu button

Add one `MatListItem` + `MatButton` alongside the existing four, following the same pattern:

```razor
<MatListItem>
    <MatButton Label="Toronto, ON" Mini="true" @onclick="HandleTtcClicked" Disabled="@(CurrentCity == CityNames.Ttc)" />
</MatListItem>
```

## Hash handler

Add, mirroring `HandleMbtaClicked`:

```csharp
async Task HandleTtcClicked()
{
    await JS.InvokeVoidAsync("eval", $"location.hash='ttc';location.reload()");
}
```

## Contract rules

| Rule | Detail |
|------|--------|
| Hash value | `'ttc'` — MUST equal `CityNames.Ttc` so `NavigationManager.ResolveCity()` resolves it. |
| Disabled binding | `CurrentCity == CityNames.Ttc` — the active city's button is disabled, matching siblings. |
| Label | `"Toronto, ON"` inline, to match the four existing inline labels (see plan localization caveat / research R5). If the strict-XII path is chosen instead, all five labels move to `RouteFilterResources.resx` via `IStringLocalizer` in one pass. |
| Reload | Sets `location.hash` then `location.reload()` so the app re-bootstraps for the selected city — identical to every existing handler. |

## Accept / reject vectors

| Scenario | Expected |
|----------|----------|
| User opens the city FAB menu | Five entries visible: Atlanta, Boston, New York, Washington DC, **Toronto**. |
| User clicks Toronto | URL hash becomes `#ttc`, page reloads, map switches to Toronto and shows TTC surface vehicles. |
| User is already on Toronto | Toronto button is disabled. |
| Hash set to a value other than `ttc` (e.g. `Ttc`) | REJECT — must be lowercase `ttc` to match `CityNames.Ttc`. |
