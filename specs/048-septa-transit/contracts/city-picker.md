# Contract: SEPTA City Picker Entry

The Philadelphia entry in `Client/…Client.Shared/Components/FABs/CityFab.razor`.

## Menu button

Add one `MatListItem` + `MatButton` alongside the existing five, following the same pattern:

```razor
<MatListItem>
    <MatButton Label="Philadelphia, PA" Mini="true" @onclick="HandleSeptaClicked" Disabled="@(CurrentCity == CityNames.Septa)" />
</MatListItem>
```

## Hash handler

Add, mirroring `HandleTtcClicked`:

```csharp
async Task HandleSeptaClicked()
{
    await JS.InvokeVoidAsync("eval", $"location.hash='septa';location.reload()");
}
```

## Contract rules

| Rule | Detail |
|------|--------|
| Hash value | `'septa'` — MUST equal `CityNames.Septa` so `NavigationManager.ResolveCity()` resolves it. |
| Disabled binding | `CurrentCity == CityNames.Septa` — the active city's button is disabled, matching siblings. |
| Label | `"Philadelphia, PA"` inline, to match the existing inline labels (same localization caveat as 043-toronto-ttc-transit — tracked debt, not this feature's job to fix). |
| Reload | Sets `location.hash` then `location.reload()` so the app re-bootstraps for the selected city — identical to every existing handler. |

## Accept / reject vectors

| Scenario | Expected |
|----------|----------|
| User opens the city FAB menu | Six entries visible: Atlanta, Boston, New York, Washington DC, Toronto, **Philadelphia**. |
| User clicks Philadelphia | URL hash becomes `#septa`, page reloads, map switches to Philadelphia and shows SEPTA surface vehicles. |
| User is already on Philadelphia | Philadelphia button is disabled. |
| Hash set to a value other than `septa` (e.g. `Septa` or `SEPTA`) | REJECT — must be lowercase `septa` to match `CityNames.Septa`. |
