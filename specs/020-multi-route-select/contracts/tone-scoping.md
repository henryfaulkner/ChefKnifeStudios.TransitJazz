# Contract: Tone Scoping Gate

**Feature**: 020-multi-route-select | Surface: `TransitMap.OnCrossingsAsync` (WebApp) | Reads:
`IRouteFilterViewModel.SelectedRouteIds`

## Rule

A crossing produces a tone iff:

```
_audioEnabled                                   // existing mute setting — checked FIRST (dominant)
&& ( SelectedRouteIds.Count == 0                // empty selection = unscoped → all routes sound
     || SelectedRouteIds.Contains(crossing.RouteId) )
```

## Placement (within `OnCrossingsAsync`)

```
if (!_audioEnabled) return;                      // EXISTING — keep first so mute always wins (FR-009)
var selected = RouteFilterViewModel.SelectedRouteIds;   // injected VM (already injected in TransitMap)
foreach (var crossing in crossings)
{
    if (selected.Count > 0 && !selected.Contains(crossing.RouteId))
        continue;                                // non-selected route → silent
    await TransitSynth.TriggerNoteAsync(crossing.RouteId, crossing.VehicleId);
    // (existing try/catch around the trigger preserved)
}
```

## Guarantees

| # | Condition | Tone? |
|---|-----------|-------|
| 1 | audio muted (`_audioEnabled == false`) | NO — regardless of selection |
| 2 | selection empty, audio on | YES for every crossing (unscoped) |
| 3 | crossing.RouteId ∈ selection, audio on | YES |
| 4 | crossing.RouteId ∉ selection (selection non-empty), audio on | NO |

- **Principle VIII intact**: tone *generation* (deterministic per-route instrument/key/notes) is unchanged;
  this gate only **suppresses emission** for non-selected routes. No tone is authored or altered.
- **Mute dominance (FR-009)**: the `_audioEnabled` early-return precedes the selection check, so mute can
  never be overridden by a selection.
- `RouteId` on the crossing (`CrossingEventDto.RouteId`) is `route_short_name`, matching `SelectedRouteIds`
  (Principle VI) — direct `Contains` compare, ordinal.

## Non-goals

- Does NOT change which vehicles are animated/plotted on the map — the batch handler still renders all
  allowed routes; only audio emission is scoped.
- Does NOT alter held-note (`triggerAttack`/`triggerRelease`) generation logic; if held notes are emitted
  via the same crossing/segment path, the same selection gate applies at the emission boundary. (Confirm at
  implementation: if held segments are driven elsewhere than `OnCrossingsAsync`, apply the identical
  `selected.Count == 0 || selected.Contains(routeId)` gate there too.)
