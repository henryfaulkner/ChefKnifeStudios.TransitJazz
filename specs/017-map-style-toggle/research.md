# Phase 0 Research: Map Style Toggle

All Technical Context items were resolvable from the existing codebase and MapLibre/MapTiler behavior; there
are no open NEEDS CLARIFICATION items. Findings below record the decisions that shape Phase 1.

## R1. How to preserve domain GeoJSON layers across a basemap swap

**Decision**: In `ChefMap.setMapStyle`, **capture** the current custom sources (`vehicles`, `trigger-points`,
and every `route-*` source) and their layer definitions, call `map.setStyle(newUrl)`, then **re-add** the
captured sources/layers inside a one-shot `map.once('style.load', …)` handler. Re-added layers carry their
previously-read `layout.visibility`, so hidden layers stay hidden.

**Rationale**: MapLibre's `map.setStyle(url)` (the documented mechanism behind MapTiler's style switching)
**replaces the entire style document** — all sources and layers added via `addSource`/`addLayer` after the
original load are discarded. The constitution (Principle VII) forbids re-fetching domain data to recover from
this; the data already lives in the map's sources as GeoJSON, so capturing `getStyle().sources` data +
`getStyle().layers` (filtered to our custom ids) and replaying them after `style.load` restores the exact
visual state with zero network calls. This is the canonical MapLibre pattern for "switch basemap, keep my
data."

**Alternatives considered**:
- *`map.setStyle(url, { diff: true })`* — the diff algorithm can sometimes retain compatible layers, but it is
  unreliable across two unrelated MapTiler style documents and is explicitly not guaranteed to preserve custom
  user layers; rejected as fragile.
- *Re-drive the C# render path* (`RenderRoutesAsync`, checkpoint config, animator reload) after the swap —
  works and re-uses `_routeShapeCache` (no API re-fetch), but it re-runs trigger-point generation and animator
  geometry loading needlessly, and the in-flight vehicle animation state would be lost. The JS capture/restore
  is lighter and keeps vehicle features intact. Rejected as heavier; the JS approach is preferred. (The C#
  re-drive remains a documented fallback if a captured-layer edge case appears.)
- *Two stacked map instances, toggle visibility* — doubles tile usage and memory; rejected.

## R2. Where the two style URLs live and how the default is selected

**Decision**: Use the existing `MapTiler:StyleUrls` config object (`LightOn`, `LightOff`, `DarkOn`, `DarkOff`)
already present in `appsettings.Development.json`. Add the same `StyleUrls` block to the production
`appsettings.json` (which currently has only a flat `StyleUrl`). The boolean setting maps **off → LightOff**
(default) and **on → LightOn**. Dark variants are ignored by this feature (out of scope).

**Rationale**: The user explicitly asked to drive selection from the `StyleUrls` object and default to
LightOff. Reading via `IConfiguration` matches how `Map.GetMapSettings` already reads `MapTiler:ApiKey` and
`MapTiler:StyleUrl`; no new config POCO is needed. Production parity requires the block in both files.

**Alternatives considered**: Strongly-typed `MapTilerOptions` POCO — cleaner long-term but introduces a new
options class and DI registration for two strings already read ad-hoc; rejected to stay consistent with the
existing `IConfiguration.GetValue` usage in `Map.razor.cs`.

## R3. Initial-load style must honor the persisted preference

**Decision**: `Map.GetMapSettings` (the `[JSInvokable("getMapSettings")]` the JS `createMap` calls on startup)
selects the style URL by reading the persisted `Settings` via `ISettingsService`: pick
`MapTiler:StyleUrls:LightOn` when `IsStreetMapEnabled` is true, else `MapTiler:StyleUrls:LightOff`. Falls back
to the flat `MapTiler:StyleUrl` if `StyleUrls` is absent, then to the current value to avoid a blank map
(FR-013).

**Rationale**: FR-009 requires the map to render in the saved style **from first paint**, before any toggle
interaction. The initial style is chosen in `createMap`, so the selection must happen there, not only on the
later toggle event. `Map` already injects `IConfiguration`; it will also take `ISettingsService` (already a
registered service) to read the persisted bool. Defaulting to LightOff when unset satisfies FR-001/FR-010
(the existing `SettingsService.GetSettings` already seeds+persists defaults on first read).

**Alternatives considered**: Pass the resolved URL from `TransitMap` into the `Map` component as a parameter —
viable but `Map.GetMapSettings` is the established seam that already assembles the JS settings object;
extending it is the smaller change.

## R4. Naming of the boolean setting and its label

**Decision**: Field `_isStreetMapEnabled` (property `IsStreetMapEnabled`), default `false`, decorated
`[property: Description("SettingStreetMap")]`. Resx key `SettingStreetMap` → EN label e.g. "Street map".

**Rationale**: The cut 016 work referred to this as the "Street map" setting (commit `9726df0 remove "Street
map" setting`); reusing that name keeps continuity. `false` default = LightOff = the requested default.
"Enabled = LightOn (streets-on look)" reads naturally as a toggle. The label flows through the blade's
reflection render automatically via the `Description` attribute → `IStringLocalizer` lookup, so no blade markup
change is needed beyond the new switch arm in code-behind.

**Alternatives considered**: `IsLightOn` / `IsBasemapLight` — more literal but leaks the style-name into the
model; `IsStreetMapEnabled` is user-facing and matches prior naming. Accepted.

## R5. Event-args type and decoupling

**Decision**: New `GisSettingChangedEventArgs : IEventArgs { required bool IsStreetMapEnabled }`, posted by
`SettingsBlade.HandleSettingPressed` via the existing `IEventNotificationService`, consumed in
`TransitMap.HandleSettingsEventReceived`. The handler resolves the style URL from `IConfiguration` and calls
`_map.SetBasemapStyleAsync(url)`.

**Rationale**: Mirrors `AudioSettingChangedEventArgs` / `CheckpointVisibilityChangedEventArgs` exactly (same
shape, same bus, same consumer location). FR-011 (decoupled toggle) is satisfied by the same mechanism already
in use. The handler must guard `if (_map is null) return;` and run on the renderer via `InvokeAsync`, matching
the existing checkpoint arm.

**Alternatives considered**: Reusing `CheckpointVisibilityChangedEventArgs`-style naming or a generic
"setting changed" event — rejected; the codebase uses one typed event per effect, and the `TransitMap`
consumer pattern-matches on concrete types.

## R6. Edge case — toggle before map ready / rapid toggling

**Decision**: The `TransitMap` handler no-ops when `_map is null` (map not ready). For rapid toggling, each
`setStyle` call is independent; the last call wins because `style.load` re-adds layers from a fresh capture
taken at call time. The persisted setting and visible basemap converge on the final toggle (SC-006).

**Rationale**: `setStyle` is idempotent with respect to the captured data layers; an interrupted swap is
replaced by the next. The minimum-open blade guard (016) already prevents the open-tap race; nothing new is
needed here. Persisted value is written synchronously in `SetSettingValue` before the event is posted, so
storage is always consistent with the user's last action.
