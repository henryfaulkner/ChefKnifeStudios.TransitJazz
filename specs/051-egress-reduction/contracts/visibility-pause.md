# Contract: Hidden-Tab Pause / Resume (Phase 2)

Binds spec FR-007, FR-008, FR-009 / US3. Additive to the hub (old clients unaffected); client-only behavior change.

## C1. Hub: `LeaveCity`

- New `TransitHub.LeaveCity(string city)` → `Groups.RemoveFromGroupAsync(Context.ConnectionId, city)`. New const `HubMethods.LeaveCity = "LeaveCity"` — **unversioned, and stays that way through Phase 3**: wire-slimming C5 renames only `JoinCity`, so `LeaveCity` MUST NOT pick up a `V2` suffix. Frozen by test P3-U6.
- No cache interaction, no broadcast, idempotent (removing a non-member is a no-op).
- `JoinCity` is UNCHANGED in this phase (still replays `LastBatchCache.Current(city)` to the caller) — resume catch-up reuses it verbatim.

## C2. Client interop: `page-visibility.js` + `IPageVisibilityJsInterop`

- Lazy-loaded RCL ES module with cache-bust GUID; `IAsyncDisposable`; callback into .NET via `DotNetObjectReference` on `visibilitychange`, reporting current `document.hidden`. Follows the `outside-click.js`/`IOutsideClickJsInterop` idiom exactly (module shape, error handling, disposal).
- Fires the current state, not the transition — .NET side re-derives desired state from `(hidden, audioEnabled)` each event (no toggle counting).

## C3. Pause gate (client policy)

```
desiredDelivery = !(document.hidden && !settings.IsAudioEnabled)
```

- Inputs: visibility events (C2) + `SettingsService.GetSettings().IsAudioEnabled` snapshot + live `AudioSettingChangedEventArgs` from the existing event bus.
- `ISignalRNotificationService` gains `PauseAsync` (invoke `LeaveCity`; connection stays open) and `ResumeAsync` (invoke `JoinCity`; replay arrives via the normal `ReceiveBatch` handler). The service's `Reconnected` auto-rejoin MUST consult the gate — a reconnect while paused must NOT rejoin the group.

### Behavior vectors
| Given | When | Then |
|---|---|---|
| Audio muted, tab visible, joined | Tab hidden | `LeaveCity` sent; no further batches delivered; zero live-update egress for this session |
| Paused (hidden+muted) | Tab visible | `JoinCity` sent; snapshot replay renders vehicles at current positions; stale vehicles idle (no motion replay); next live batch ≤1 publish interval later |
| Audio playing, tab visible, joined | Tab hidden | Nothing sent; batches and audio continue |
| Hidden, audio playing | User mutes (settings still reachable? no — tab hidden; covers programmatic/mid-transition mute) | `LeaveCity` on the mute event |
| Hidden, paused | User unmutes | `JoinCity` + replay |
| Paused | SignalR connection drops and reconnects | Client does NOT rejoin group until visible-or-unmuted |
| Rapid hide/show ×N | After burst settles | Exactly the final state's membership; no duplicate joins, no leaked handlers, at most one in-flight hub call at a time |

## C4. Non-goals
- No settings-blade toggle, no user-facing copy (nothing to localize), no server idle detection, no slow-cadence group (R3(b) deferred), no change to the 10s publish cadence.
