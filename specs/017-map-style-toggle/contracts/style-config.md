# Contract: MapTiler Style Configuration

The two basemap presentations are sourced from the project's `MapTiler:StyleUrls` config object, read via
`IConfiguration`. No new config POCO; this matches the existing ad-hoc reads in `Map.GetMapSettings`.

## Required config shape (both appsettings files)

`appsettings.Development.json` already contains this block. `appsettings.json` (production) currently has only
a flat `MapTiler:StyleUrl` and MUST gain the `StyleUrls` block:

```json
"MapTiler": {
  "ApiKey": "<public, origin-restricted key>",
  "StyleUrl": "<legacy flat fallback — may remain>",
  "StyleUrls": {
    "LightOn":  "https://api.maptiler.com/maps/<lighton-id>/style.json?key=<key>",
    "LightOff": "https://api.maptiler.com/maps/<lightoff-id>/style.json?key=<key>",
    "DarkOn":   "https://api.maptiler.com/maps/<darkon-id>/style.json?key=<key>",
    "DarkOff":  "https://api.maptiler.com/maps/<darkoff-id>/style.json?key=<key>"
  }
}
```

Production-key/ID values are filled by the implementer from the MapTiler console; the same public key already
embedded in the bundle is used (Principle II — origin-restricted, not a secret). `DarkOn`/`DarkOff` are
present for parity but unused by this feature.

## Key → state mapping (binding)

| `Settings.IsStreetMapEnabled` | Config key read | Presentation |
|-------------------------------|-----------------|--------------|
| `false` (default) | `MapTiler:StyleUrls:LightOff` | LightOff (app default) |
| `true` | `MapTiler:StyleUrls:LightOn` | LightOn |

## Fallback chain (binding, FR-013)

For both initial load and toggle resolution:

1. `MapTiler:StyleUrls:{LightOn|LightOff}` per the setting.
2. If absent/empty → `MapTiler:StyleUrl` (legacy flat).
3. If still absent/empty → empty string → caller leaves the **current** basemap untouched (initial load:
   relies on whatever the map already has; never forces a blank style).

## Behavioral requirements

| ID | Requirement |
|----|-------------|
| CFG-1 | Both `appsettings.json` and `appsettings.Development.json` define `MapTiler:StyleUrls:LightOff` and `:LightOn`. |
| CFG-2 | With no saved preference, resolution yields `LightOff` (FR-001). |
| CFG-3 | A missing requested entry falls back per the chain and never blanks the map (FR-013). |
| CFG-4 | The `key` query-param embeds the existing public, origin-restricted MapTiler key only (Principle II). |
