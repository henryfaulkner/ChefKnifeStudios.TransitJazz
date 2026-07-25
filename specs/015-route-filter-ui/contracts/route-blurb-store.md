# Contract: Route blurb store + RouteBlurbBar

C#-side presentation contract for the bottom blurb bar and its content source.

## `IRouteBlurbStore`

`Client.Shared/Data/RouteBlurbStore.cs`. Registered singleton in `Program.cs`.

```csharp
public interface IRouteBlurbStore
{
    /// Always returns a non-null RouteBlurb with non-empty display text.
    /// Authored entry if present; otherwise a placeholder (IsPlaceholder = true)
    /// whose text names the route.
    RouteBlurb GetForRoute(string routeId);
}
```

**Contract guarantees**:
- Never returns null.
- Returned `ToneDescription` and `Significance` are never both empty — the placeholder fills them.
- For an unauthored route, `IsPlaceholder == true` and the text includes `routeId`.
- For an authored route, `IsPlaceholder == false`.

**Placeholder construction**: built from `IStringLocalizer<RouteFilterResources>["RouteBlurbPlaceholder"]`
formatted with `routeId`. (English-only resource in this feature; Spanish deferred.)

| Input | Authored? | Output |
|-------|-----------|--------|
| `"5"` (authored) | yes | `RouteBlurb("5", "<tone>", "<fact>", IsPlaceholder:false)` |
| `"110"` (not authored) | no | `RouteBlurb("110", placeholder text incl. "110", placeholder text, IsPlaceholder:true)` |
| `""` / unknown | no | placeholder (defensive); never throws |

> Authored dictionary MAY be empty at ship — every route returns a placeholder. Valid state.

## `RouteBlurbBar` component

`Client.Shared/Components/RouteBlurbBar.razor(.cs/.css)`.

**Inputs**: injects `IRouteFilterViewModel` (focus state) and `IRouteBlurbStore` (content).

**Behavior**:
- Subscribes to `IRouteFilterViewModel.PropertyChanged`; on `RouteItems`/`HasSelection` change,
  recomputes from `SelectedRouteId`:
  - `SelectedRouteId is null` → bar hidden (not rendered / `display:none`); no blurb.
  - `SelectedRouteId is R` → bar visible; content = `store.GetForRoute(R)`.
- When focus moves R → S, the component updates its bound `RouteBlurb` in place (same element stays
  mounted) so the bar does not close+reopen (FR-008).
- Implements `IDisposable` to unsubscribe (mirrors `RouteFilters.razor.cs`).

**Visual / motion contract** (`.razor.css`, binds Principle XI + UX Standards "Bottom Blurb Bar"):
- Full-width, anchored to the bottom of the map container, overlaid (`position:absolute; left:0; right:0; bottom:0; z-index` above map, below modals).
- Semi-transparent dark background (e.g. `rgba(0,0,0,0.65)`), light text.
- **In**: fade/slide-in transition completing within **100ms**.
- **Out**: hidden immediately on deselect — **no exit animation** (toggle a `display`/render gate, not a timed-out transition).
- Shows `ToneDescription` and `Significance`; MAY visually distinguish `IsPlaceholder`.

## Accept / reject vectors

| Scenario | State | Expected |
|----------|-------|----------|
| No focus | `SelectedRouteId == null` | bar not visible; no content |
| Focus unauthored route | `SelectedRouteId == "110"` | bar visible within 100ms; placeholder text naming "110" |
| Focus authored route | `SelectedRouteId == "5"` | bar visible; authored tone + fact |
| Direct R→S | `"110"` → `"5"` | bar stays mounted, content swaps to S; no close/reopen flicker |
| Unfocus | `"5"` → `null` | bar disappears immediately, no exit animation |
| Rapid sweep | many changes then `null` | bar ends hidden; last-focused content only while focused |
