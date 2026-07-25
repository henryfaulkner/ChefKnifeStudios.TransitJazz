# Data Model: Loading Spinner

This feature is presentational and read-only. It introduces **no** persisted or
transferred entities. It consumes one existing value.

## Consumed (read-only)

### Persisted theme preference
- **Source**: `localStorage`, key `"Setting"` (`LocalStorageConstants.SettingsKey`).
- **Shape**: JSON object serialized by Blazored.LocalStorage 4.5.0 (camelCase). The
  full object is the `Settings` model; only one field is read.
- **Field read**: `isDarkModeEnabled` (JSON boolean), matched case-insensitively.
- **Interpretation**:
  | Stored state | Resolved theme |
  |--------------|----------------|
  | `isDarkModeEnabled: true` | dark |
  | `isDarkModeEnabled: false` | light |
  | key absent / value missing | light (default) |
  | value unparseable / storage throws | light (default) |
- **Mutation**: none. This feature never writes to `localStorage`.

## Derived (transient, in-DOM only)

### `data-theme` attribute
- Set on `<html>` (`document.documentElement`) by the pre-boot script.
- Values: `"dark"` or `"light"`.
- Lifetime: set once before app render; the Blazor app's own theming takes over
  after boot (this attribute is not part of the running app's contract and may be
  ignored once the app renders its themed chrome).

## State transitions

None. The spinner has a single visible state (spinning) and is removed—not
transitioned—when Blazor replaces `#app`.
