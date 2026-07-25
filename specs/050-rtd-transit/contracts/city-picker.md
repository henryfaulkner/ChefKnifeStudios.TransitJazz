# Contract: RTD City Picker Entry

The Denver entry in `Client/…Client.Shared/Components/FABs/CityFab.razor`.

## Menu button

Add one `MatListItem` + `MatButton` alongside the existing six, following the same pattern:

```razor
<MatListItem>
    <MatButton Label="Denver, CO" Mini="true" @onclick="HandleRtdClicked" Disabled="@(CurrentCity == CityNames.Rtd)" />
</MatListItem>
```

## Hash handler

Add, mirroring `HandleSeptaClicked`:

```csharp
async Task HandleRtdClicked()
{
    await JS.InvokeVoidAsync("eval", $"location.hash='rtd';location.reload()");
}
```

## Contract rules

| Rule | Detail |
|------|--------|
| Hash value | `'rtd'` — MUST equal `CityNames.Rtd` so `NavigationManager.ResolveCity()` resolves it. |
| Disabled binding | `CurrentCity == CityNames.Rtd` — the active city's button is disabled, matching siblings. |
| Label | `"Denver, CO"` inline, to match the existing inline labels (same localization caveat as 043-toronto-ttc-transit / 048-septa-transit — tracked debt, not this feature's job to fix). |
| Reload | Sets `location.hash` then `location.reload()` so the app re-bootstraps for the selected city — identical to every existing handler. |

## Accept / reject vectors

| Scenario | Expected |
|----------|----------|
| User opens the city FAB menu | Seven entries visible: Atlanta, Boston, New York, Washington DC, Toronto, Philadelphia, **Denver**. |
| User clicks Denver | URL hash becomes `#rtd`, page reloads, map switches to Denver and shows RTD buses/light rail/commuter rail. |
| User is already on Denver | Denver button is disabled. |
| Hash set to a value other than `rtd` (e.g. `Rtd` or `RTD`) | REJECT — must be lowercase `rtd` to match `CityNames.Rtd`. |
