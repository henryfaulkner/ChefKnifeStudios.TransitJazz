# 035 — Data Model

No new data entities. This feature reads one existing field:

| Field | Location | Use |
|---|---|---|
| `Settings.IsDarkModeEnabled` | `Models/Settings.cs` (added in 034) | Read by each component in `OnInitialized` to seed `_isDark` |

The `ThemeChangedEventArgs.IsDarkMode` bool (existing) is the runtime update channel.

No schema changes. No new storage keys. No new event types.
