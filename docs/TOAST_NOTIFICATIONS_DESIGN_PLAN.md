# Warning and error notifications: design and implementation plan

Date: 2026-10-25. Status: proposed; documentation only.

## Recommendation

Use the existing MatBlazor toast infrastructure to announce meaningful failures of the current experience. Do not toast every exception or failed request. A healthy session, including startup, audio unlock, city changes, route selections, intentional mute, backgrounding, and successful recovery, produces **zero toasts**.

Start with connection failures, unavailable route data, failed map initialization, and failed audio after the user requests it. Keep Stats failures inline. Add narrower warnings only when the application can establish the actual impact. Prefer missing a speculative warning over falsely telling someone the app is broken.

Aggregate eligible failures into one notification per burst and suppress repeated reports for the lifetime of the same incident. Three API failures must not become three toasts. Three failures already explained inline may correctly produce zero toasts.

## Scope and evidence

This inventory covers the current production browser experience: WebApp startup/layout/pages, Core HTTP and SignalR services, Shared view models/components, settings, and active JavaScript map/audio modules. It includes silent guards and swallowed failures, not just exceptions. Developer-only audio tools, unused endpoint methods, and server-only failures are explicitly excluded below. Future features need their own entries; this is not a promise to notify on arbitrary future exceptions.

Paths below are relative to `src/Client/`, using these abbreviations:

- **Core**: `ChefKnifeStudios.TransitJazz.Client.Core/`
- **Shared**: `ChefKnifeStudios.TransitJazz.Client.Shared/`
- **Web**: `ChefKnifeStudios.TransitJazz.Client.WebApp/`

Primary implementation references:

| Area | Source and relevant decisions |
| --- | --- |
| Existing presentation | [ToastService.cs](../src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Services/ToastService.cs), [Program.cs](../src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Program.cs), [MainLayout.razor](../src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Layout/MainLayout.razor) |
| Connection lifecycle | [SignalRNotificationService.cs](../src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/SignalRNotificationService.cs): InitAsync, PauseAsync, ResumeAsync, Reconnected, CloseConnection |
| App readiness and route load | [ApplicationViewModel.cs](../src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/ViewModels/ApplicationViewModel.cs): ConnectSignalRAsync, LoadRoutesAsync, RoutesLoadedTask |
| HTTP outcomes | [HttpService.cs](../src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/HttpService.cs), Core/Services/EndpointsServices/{Gtfs,Transit,Stats}EndpointsService.cs |
| Map and playback orchestration | [TransitMap.razor.cs](../src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs): EnableAudioAsync, LoadRoutesAsync, RenderRoutesAsync, ConfigureTrackerForRouteAsync, HandleVehicleBatchAsync, HandleSettingsEventReceived |
| Map JS boundary | [Map.razor.Helper.cs](../src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor.Helper.cs), Shared/wwwroot/js/{map-interop,vehicle-animator,crossing-dispatcher,checkpoint-pulse,checkpoint-trail}.js |
| Audio JS boundary | [TransitSynthJsInterop.cs](../src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Services/JsInterop/TransitSynthJsInterop.cs), Shared/wwwroot/js/transit-synth.js, Shared/Components/AudioUnlockOverlay.razor |
| Existing local feedback | [StatsOverlay.razor](../src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Stats/StatsOverlay.razor), Shared/Components/Charts/* |
| Preferences and actions | [SettingsService.cs](../src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Services/SettingsService.cs), Shared/Components/FABs/*, Shared/Components/Blades/SettingsBlade.razor.cs |

### Findings that affect the design

1. MatBlazor **2.10.0** is already referenced. `AddMatToaster`, one `MatToastContainer`, localized severity titles, and `IToastService` already exist. Error maps to `MatToastType.Danger`. Production call-site search found no current calls to ShowSuccess/ShowWarning/ShowError outside the wrapper itself. RouteFilterViewModel injects the service but does not use it.
2. `SignalRNotificationService.InitAsync` catches failures and returns normally. `ApplicationViewModel.ConnectSignalRAsync` then sets `IsConnected = true`. Missing hub configuration also exits without establishing a connection. That property cannot currently drive trustworthy notifications.
3. The hub service subscribes to Reconnected, but not Reconnecting or Closed. A successful transport reconnect is insufficient: the city group join must succeed, and intentionally paused delivery must stay paused.
4. Audio interop catches errors internally. `EnableAudioAsync` also sets `_audioUnlocked = true` in `finally`. Failed unlock can therefore look successful. The native gesture listener and Blazor click path both participate in unlocking; they need one reported outcome per user attempt.
5. Map interop frequently logs and returns success-shaped void results. JavaScript also silently returns when the map/source is missing. Asynchronous map load failures are not reliably represented by a caught create call. The style swap has a timed fallback; timer completion is not proof of successful layer restoration.
6. Generic HTTP paths collapse exceptions, including caller cancellation, into error results. Stats has a richer status/problem/retry result and explicit cancellation handling. Do not add a global HTTP-to-toast hook.
7. Stats already handles failed reads, retained stale data, retry cooldowns, invalid response shapes, cancellation, and obsolete generations. Charts have local fallback presentation. Duplicate global messages would make these flows worse.
8. Initial vehicle data comes from SignalR JoinCity replay. `GetLastBatch` and `GetRouteShape` have no current production callers found in the client. Do not create notifications for hypothetical REST snapshot failures.

## Decision rules

**Error** means a requested core capability cannot run, or a requested operation failed completely. **Warning** means an established capability is temporarily interrupted, a useful partial experience remains, or a preference applies only for this session. Severity describes user impact, not HTTP status or logger level.

An eligible notification requires all of: a current operation/city, actual failure evidence, meaningful user impact, no adequate local explanation already visible, and no existing incident that explains it. Cancellation, stale completion, intentional suspension, normal empty data, and successful fallback are not failures.

The tables label every audited decision family **Toast**, **Inline**, or **Silent**. Where several methods share a decision, they are grouped deliberately. Conditional entries must not ship until their impact can be measured; logging alone does not meet the condition.

## Notification decision inventory

### Connection and live delivery

| ID | Decision / source | Presentation and severity | Proposed message and recovery |
| --- | --- | --- | --- |
| C1 | Initial StartAsync fails; missing/invalid hub configuration; negotiation/auth/protocol failure | **Toast: Error**, once the attempt fails, not merely while connecting | “Live transit could not connect. Reload to try again.” Do not claim automatic retry unless implemented. |
| C2 | Initial JoinCity fails after transport connected | **Toast: Error**, same live-delivery incident as C1 | Same message; connected transport must not count as usable delivery. |
| C3 | Existing delivery enters Reconnecting | **Toast: Warning** only after 10 seconds continuously interrupted while visible and delivery desired | “Live updates are interrupted. Trying to reconnect.” Cancel pending toast on recovery. |
| C4 | Closed after reconnect attempts end unexpectedly | **Toast: Error**, replace/escalate C3 once | “Live updates have stopped. Reload to reconnect.” Intentional StopAsync/disposal excluded. |
| C5 | Reconnected handler cannot rejoin city, or ResumeAsync fails/skips because disconnected when delivery is desired | **Toast: Error** after a failed join attempt; disconnected state follows C3/C4 | “Live updates could not resume. Reload to reconnect.” Successful rejoin, not transport state alone, clears incident. |
| C6 | Version-gated JoinCityV2 rejected | **Toast: Error**, same connection incident, specific copy | “This version cannot connect to live updates. Reload the page. If it continues, try again later.” No deployment instructions for visitors. |
| C7 | Browser reports offline | **No standalone toast**. Corroborates failed active connection/request | When an eligible network failure also exists: “The app appears to be offline. Check your connection.” Inherit warning/error from actual impact. Online is a hint to reassess, never proof of recovery. |
| C8 | Hidden + muted AttentionGate pause; LeaveCity failure; intentional close; disposal | **Silent** | Logging only. Failure to reduce background traffic is operational, not a visitor action. |
| C9 | Hidden tab reconnects while delivery intentionally paused | **Silent** | Preserve `_desiredDelivery` semantics. No join and no notification until delivery is needed again. |
| C10 | Connected but no batches, empty replay, no moving vehicles, stale individual records, route/vehicle eviction | **Silent** | Quiet service and feed behavior are legitimate. Do not infer outage from silence or a zero count. |
| C11 | Batch decoding/processing fails globally, with valid delivery expected | **Toast: Error**, conditional on confirmed pipeline failure | “Live transit could not be displayed. Reload to try again.” Deduplicate against connection, route, or map failure that already explains it; isolated bad records remain silent. |

No stale-feed toast in the initial release. A trustworthy upstream-health contract would be a separate feature. A connected socket and absent crossings do not prove a failed transit feed.

### APIs, routes, and statistics

| ID | Decision / source | Presentation and severity | Proposed message and recovery |
| --- | --- | --- | --- |
| R1 | ApplicationViewModel.GetAllRouteShapes fails, throws, times out, or returns unusable response; no usable current-city cache | **Toast: Error** | “Routes could not load for this city. Reload to try again.” One owner in the view model; page consumers of RoutesLoadedTask must not also notify. |
| R2 | Successful route response has zero usable geometries for a configured supported city | **Toast: Error** after validation completes | “Route data is unavailable for this city. Try another city or try again later.” Do not confuse an initially empty cache with a completed empty response. |
| R3 | Some route features have missing keys, invalid geometry, or tracker setup failures, but usable routes remain | **Silent by default**; **Warning** only if an explicit selection has no usable route and no inline explanation | “The selected route could not be displayed. Try another route.” Never one toast per skipped feature. Add inline selection feedback first if practical. |
| R4 | Same route-load failure observed by TransitMap.LoadRoutesAsync/RenderRoutesAsync; empty cache because load failed | **Silent secondary report** | R1/R2 owns the incident. Do not call route readiness successful merely because an HTTP response was 200. |
| R5 | Stats initial query fails: network, timeout, 401/403, 404, 429, 5xx, malformed response or invalid selection/scope | **Inline: Error; no toast** | Existing Stats error/retry area owns feedback. Retain safe problem mapping and Retry-After. Do not suggest signing in for this public app. |
| R6 | Stats refresh fails with accepted snapshot retained | **Inline: Warning; no toast** | Preserve stale labeling and retry state. Do not present retained data as current. |
| R7 | Stats disabled, no data, partial coverage, pending data, previous-day fallback, missing reported counts | **Inline state; no toast** | These are product/data states, not necessarily faults. Feature disabled should not create a global error. |
| R8 | Stats debounce, cooldown, hidden pause, overlay close, route/scope change cancels request; obsolete generation returns | **Silent** | A late error must never leak into a later city/selection/session. |
| R9 | Chart.js import/render fails, invalid chart model, unsupported timeline; accessible table/text remains | **Inline: Warning; no toast** | Use existing chart fallback. If no usable Stats content remains, use R5 inline error. Time-zone formatting fallback also stays silent. |
| R10 | Unused GetLastBatch/GetRouteShape, generic POST/PUT/PATCH/DELETE helpers | **No new toast hooks** | If a real caller is added later, classify its user outcome there. A transport helper lacks enough context. |

For R1, classify network, authorization, throttling, server, and deserialization failures internally but use one safe user message. Respect Retry-After if retries are introduced; this plan does not add blanket API retries. An aborted request owned by navigation/disposal is silent; an independently expired deadline is a failure. Do not infer cancellation from exception text.

### Map and interaction

| ID | Decision / source | Presentation and severity | Proposed message and recovery |
| --- | --- | --- | --- |
| M1 | CreateMap fails, MapLibre unavailable, WebGL unsupported, terminal style load error, or confirmed initialization failure | **Toast: Error**, plus persistent map fallback | “The map could not start. Reload to try again.” Unsupported capability gets “This browser could not display the map. Try another browser.” Requires explicit JS readiness/failure result. |
| M2 | Map readiness has not arrived, with no terminal failure evidence | **Inline loading/delayed state; no toast on timer alone** | A 20-second visible-time watchdog may offer reload inline. Slow startup is not proof of failure. Cancel on readiness or disposal. |
| M3 | Individual tile/glyph/image fetch errors, transient missing sources during style replacement, isolated pulse/trail failure | **Silent** | Do not mirror MapLibre error events or console warnings into toasts. These can coexist with a usable map. |
| M4 | User changes style/theme; requested basemap cannot load, previous usable map restored | **Toast: Warning**, one per incident | “The map style could not change. The previous style is still shown.” Only say this after verifying restoration. If map is unusable, M1 error instead. |
| M5 | AddAllRoutes, ProcessNearestPointBatch, or style layer restoration fails persistently after readiness | **Toast: Error** if transit display unusable; **Warning** if only a secondary layer is unavailable | “Live transit could not be displayed. Reload to try again.” Require terminal failure or two failed valid batches spanning at least 10 visible seconds; resolve after verified successful rendering. No per-frame reports. |
| M6 | Hover focus/clear, centering, optional checkpoints, trails, or visibility setting fails | **Silent** for hover/background effects; **Warning** for a failed explicit setting with no local feedback | “That display setting could not be applied. Try again.” Revert the control to confirmed state. Group all affected controls into one settings incident. |
| M7 | Route geometry not yet loaded, bounded pending-batch eviction before map ready, animator lacks route/motion timing | **Silent** | Normal startup/fallback behavior. A confirmed root failure uses R1/M1, not additional buffer warnings. |
| M8 | CityFab navigation JavaScript fails while old page remains usable | **Toast: Error** for that explicit operation | “The city could not change. Reload and try again.” No toast during normal full-page city reload. |
| M9 | Unknown URL hash resolves to default city; router NotFound | **Silent / existing inline NotFound** | No toast. Never claim an unavailable requested city successfully loaded. |

### Audio and preferences

| ID | Decision / source | Presentation and severity | Proposed message and recovery |
| --- | --- | --- | --- |
| A1 | Initial autoplay lock, user has not enabled audio, muted audio, filtered-out notes, normal loading, no crossings | **Silent** | These occur in healthy sessions. Never use absence of audible notes as a failure detector. |
| A2 | Explicit enable gesture fails or context remains suspended | **Inline: Error on unlock overlay; no duplicate toast** | “Audio could not start. Tap Enable audio to try again.” Keep overlay and re-arm the native gesture path. Provide an explicit continue-without-audio option if unlock cannot succeed. |
| A3 | Already-unlocked, requested audio cannot resume after automatic resume attempt | **Toast: Warning** when another gesture can recover | “Audio is paused. Tap the audio button to resume.” This requires wiring that button to a real resume gesture; do not ship copy promising a nonexistent action. |
| A4 | Synth/Tone module or crossing dispatcher import fails and prevents requested playback after overlay dismissed | **Toast: Error** | “Audio is unavailable. Reload to try again.” Muted/pre-unlock speculative imports are silent; retain their failure for reassessment when audio is requested. |
| A5 | Instrument samples or reverb initialization fail for a currently required voice | **Warning** if some required voices work; **Error** if none work | “Some sounds could not load.” / “Audio could not load. Reload to try again.” Group by audio capability, not sample URL, instrument, vehicle, or note. Emit after 10 seconds unresolved or terminal load failure; recover before eligibility means no toast. |
| A6 | Backfill texture fails after explicit selection, main transit audio works | **Toast: Warning** | “The background sound could not change. Try again.” Revert control or show applied state. Optional warming/percussion failures not affecting requested output stay silent. |
| A7 | Mute/unmute/filter application fails and actual playback disagrees with controls | **Toast: Error** for failed audio stop; **Warning** for failed filter/change | “Audio could not be stopped. Close this tab to stop playback.” Attempt a master mute first. Filter: “The audio selection could not be applied. Try again.” Do not assume a persisted setting proves the effect occurred. |
| A8 | Individual note scheduling error, duration fallback, inactive sampler disposal, optional warmup, dev instrument toggles | **Silent** | Escalate only an established global playback failure through A4/A5. No per-note notifications or exception scraping. |
| P1 | No saved settings, version change, corrupt settings recovered with defaults | **Silent** | Defaults are adequate; a settings-reset toast is not useful enough. |
| P2 | Storage unavailable, explicit change succeeds in memory but cannot persist | **Toast: Warning**, once per storage incident | “Your changes apply to this session but could not be saved.” Requires shared in-memory fallback first; current code does not provide it. |
| P3 | Explicit setting cannot be applied even in memory | **Toast: Warning**, or A7 error if stopping audio fails | “That setting could not be applied. Try again.” Revert controls; do not emit both storage and effect messages for one click. |
| P4 | Initial default-setting write fails | **Silent** with working in-memory defaults | Current SetItem can throw outside the read catch. Fix the fallback; do not greet a new visitor with a persistence warning before they change anything. |

### Cross-cutting exclusions

| Decision / source | Presentation |
| --- | --- |
| Analytics/Umami city tracking, viewport sizing, outside-click registration, visibility listener failure | **Silent**; log and use existing fallback. If a later concrete user operation fails, its owner may notify. |
| Dispose/unsubscribe/teardown JS errors, cancelled timers, intentional connection stop | **Silent**. No notifications while leaving the page. |
| Route labels/colors/category defaults, absent blurbs, unsupported optional metadata, empty selection | **Silent**. Successful fallback is not a warning. |
| AudioTest, legacy synth, AudioPlayer helper without production playback use, DEBUG instrument tools | **No production toasts**. Keep diagnostics local to developer tools. |
| Unhandled renderer failure or WASM/bootstrap/CDN failure before a usable layout | **Persistent fallback**, not a toast. Keep `blazor-error-ui`; consider a scoped error boundary separately. A dead renderer cannot reliably render MatToast. |
| Worker feed errors, database retention, telemetry, server cache/persistence faults | **No direct visitor toasts**. Only a failed current client capability qualifies via the rows above. Never broadcast operational logs to all visitors. |
| Toast presentation itself fails | Log once, keep inline feedback; never recursively report a toast error. |

## Aggregation and incident lifecycle

Use one small application-scoped notification coordinator, with a bounded dictionary of current incidents. Keep MatBlazor as the renderer and the existing ToastService as the adapter. Do not add a notification bus, persistent notification inbox, dependency, or server endpoint.

An issue contains a fixed code, capability (`live`, `routes`, `map`, `audio`, `settings`, `navigation`), severity, city/page generation, operation generation where relevant, active/resolved state, and eligibility time. Use resource keys for copy. Never key by exception text, URL, vehicle, route, or timestamp. Fixed codes keep memory bounded.

### Proposed defaults

| Policy | Value |
| --- | --- |
| Burst collection | Fixed 2-second window beginning at first eligible issue; later issues do not extend it |
| Reconnect grace | 10 continuous visible seconds |
| Foreground return | Reassess state; allow 10 seconds for intended delivery/audio recovery before warning, unless a terminal failure is already confirmed |
| Re-arm after recovery | 30 continuous healthy seconds for the affected capability |
| Global automatic notification spacing | At least 10 seconds between presentations; fatal inability to stop audio may bypass spacing |
| Visible toasts | Maximum one; no MatBlazor backlog |
| Display duration | Warning 8 seconds, error 12 seconds, close button; persistent inline state for ongoing core failure |
| Pending records | One per fixed issue code/current scope; no chronological queue |

These are proposed UX defaults, not measured platform guarantees. Validate with the scenarios below.

### Algorithm

1. Feature owner reports an outcome. Discard cancellations, outdated generations, intentional suspension, and inline-owned issues before aggregation.
2. Merge repeated reports into the active incident. They must not restart grace periods or display timers. Resolution before presentation removes the pending candidate.
3. Collect eligible incidents for two seconds. Choose the highest severity. Same capability gets one capability-specific message. If a confirmed common cause exists, show its message and retain the child issues as suppressed evidence.
4. Do not assume three failures have the same network cause. Unrelated simultaneous eligible failures get a single summary naming at most two capabilities, for example “Routes and audio could not load. Reload to try again.” For more capabilities: “Parts of Transit Jazz could not load. Reload to try again.” Detailed local states remain available. No exception list or numeric request count.
5. Record every included/suppressed incident as announced. Later duplicate reports remain silent for the entire unresolved incident, even after dismissal. A child that outlives a resolved parent is reconsidered only if its distinct actionable impact was not already communicated.
6. A new error may replace an active warning once; never stack them. A terminal C4/C5 outcome may escalate C3 once. Same-severity changes do not repeatedly announce the same incident. For an unrelated new issue during spacing, retain current state only; at the next allowed time re-evaluate, aggregate, or discard if resolved. Do not queue stale toast objects.
7. On actual recovery, cancel pending presentations and remove an obsolete visible toast where the installed API supports it. No success toast. Keep the incident suppressed until 30 healthy seconds; flapping inside that period does not announce again. Online events, successful unrelated APIs, toast dismissal, and timer expiry do not count as recovery.
8. While hidden, update incident state but show nothing. On visibility return, present only still-relevant failures after reassessment; do not replay historical bursts. A full city reload starts a fresh scope; disposal invalidates old callbacks/timers.

**Example:** routes, a second eligible API-backed operation, and live join fail at t=0, 0.4, and 1.2 seconds. At t=2 seconds show one error summarizing the affected capabilities. Retries at t=4, 8, and 20 produce no more toasts. If all recover at t=25, clear the failure state silently; only after 30 healthy seconds can a new incident announce again. Stats errors remain inline and do not join this burst merely to increase its count.

## Implementation design and prerequisites

### Preserve architectural ownership

Core emits typed connection outcomes/state and returns HTTP outcomes; it does not depend on MatBlazor or Client.Shared. ApplicationViewModel owns route readiness and translates Core outcomes into application issues. Map/audio JS adapters expose coarse capability results or callbacks to their Shared owner. Only the coordinator calls IToastService.

Keep state scoped to the browser application and ensure startup reports survive until MainLayout registers its presenter after first render. Use the existing Blazor DI scope; avoid adding root-provider resolution or a singleton-to-scoped dependency problem. The layout marshals presentation through InvokeAsync. Revalidate queued state at host readiness instead of replaying startup errors already resolved.

### Correct readiness before adding presentation

- SignalR: expose transport and city-subscription outcomes; wire Reconnecting/Closed; handle joins inside Reconnected safely. Explicitly mark intentional shutdown and cancellation. Await and dispose replaced connections so old callbacks cannot report a new incident. Set IsConnected from confirmed desired delivery state, not normal method return. Preserve hidden/muted pause behavior.
- Routes: validate usable current-city shapes; make load completion distinguish success, unavailable, and cancellation. A success with empty/null geometry is not operational readiness. Keep one load owner. Reload is the initial retry UX; the current once-only task/init gate must be redesigned before promising an in-place Retry button.
- HTTP: propagate caller cancellation on the paths used by routes; expose bounded failure classification as needed. Never show raw `Result.Errors`, response bodies, exception messages, hostnames, or credentials.
- Audio: return a confirmed unlock result, including context state; do not dismiss in finally. Combine native gesture and Blazor handler into one attempt outcome and re-arm on retry. Observe asynchronous module/sample/reverb failures, including promises that currently reject or never settle without reaching C#. Report capability transitions, not every failed sample/note. Preserve one shared synth module instance.
- Map: expose actual readiness, fatal initialization errors, and style-restore success. A completed fallback timer or swallowed void call is not success. Make render operations observable enough for M5 without sending per-frame .NET callbacks. Do not turn all map error events into user issues.
- Settings: provide one shared in-memory settings snapshot when storage fails. Separate applying an effect from persisting it; otherwise “applies to this session” is untrue. Route FAB and settings-blade failures through the same outcome policy.

### MatBlazor presentation

Keep one MatToastContainer and the existing Warning/Danger mapping. MatBlazor documents registration, duplicate prevention, and a single application container. Use its renderer with application-owned aggregation. [Official MatBlazor Toast documentation](https://www.matblazor.com/Toast).

`PreventDuplicates = true` remains a final safeguard, not the incident policy. Before implementation, verify the pinned 2.10.0 API for maximum visible count, per-toast duration, removal/replacement, and accessibility; do not assume properties from MudBlazor or another toast library. If replacement is unavailable, implement the smallest adapter change needed, or finish the current toast before showing the freshly revalidated next one; never rely on a hidden library queue.

Use localized resources in the existing RouteFilterResources.resx. Retain plain text and severity labels. Do not add sounds, success messages, or automatic focus stealing. Keep controls reachable, honor reduced motion, and check contrast in both themes. Validate live-region announcement once per notification, keyboard dismissal, touch targets, and sufficient reading time. Ongoing failures need persistent local status so auto-dismiss is not the only explanation.

The current bottom-right position conflicts with the map/audio FAB area. Prefer top-center within mobile safe-area insets, below or clear of primary navigation. Verify it against the unlock overlay, Stats dialog, city menu, and settings blade. If a modal makes the global host inaccessible, use local feedback or defer presentation until it closes, then revalidate; do not introduce a second toast container.

## Delivery plan

1. **Outcome correctness:** fix connection readiness, route validation/cancellation, and unlock result handling. Add persistent connection/map failure text where a disappearing toast would leave an unexplained broken screen. Preserve existing Stats error behavior.
2. **Coordinator and host:** add bounded issue lifecycle, burst aggregation, severity escalation, foreground suppression, generation guards, and resource-backed copy. Integrate existing ToastService and one layout host. No success path calls.
3. **First coverage:** implement C1–C6, R1/R2, confirmed M1, and A2/A4. A2 is intentionally inline. Verify muted/background cases before broadening coverage.
4. **Conditional coverage:** add explicit map style/render results, audio voice/context health, settings persistence fallback, and failed user actions. Enable R3/M5/A5 only when their evidence conditions are implemented. Do not substitute guessed thresholds based on quiet feeds.
5. **Review and release:** run targeted policy tests and browser scenarios, check accessibility/layout, and review resulting copy with actual failure injection. Leave implementation changes uncommitted for user review.

## Acceptance scenarios

Use deterministic time for coordinator tests and bounded fakes for transport/interop outcomes. Test user-visible policy and readiness transitions, not one test per catch block. Browser checks are required for gesture trust, JS callbacks, layering, and screen-reader behavior.

| Scenario | Expected result |
| --- | --- |
| Healthy startup, unlock, route selection, theme change, city switch, Stats open/refresh/close | Zero toasts |
| Three eligible API/capability failures inside two seconds | Exactly one error, useful summary, no request counts |
| Three Stats requests fail | Zero toasts; appropriate inline state and retry policy |
| Failure resolves inside grace/burst window | Zero toasts |
| Initial hub start fails; join never completes; missing config; wire-version rejection | One error per incident; IsConnected never falsely true |
| Reconnect succeeds within 10 seconds | Zero toasts |
| Reconnect exceeds 10 seconds, then closes | One warning followed by at most one replacing error |
| Transport reconnects but JoinCity fails | Delivery remains failed; no false recovery |
| Repeated failures, manual dismissal, network flapping | No repeated announcements until confirmed recovery plus re-arm interval |
| Hidden/muted pause, reconnect while paused, later foreground resume | No historical toast replay; warn/error only if current desired delivery fails |
| City/selection changes while an old operation fails | Old result cannot notify new scope |
| Offline event alone with no observed failed capability | Zero toasts |
| Valid empty feed, no crossings, stale vehicle records, missing optional metadata | Zero toasts |
| Route response malformed or entirely unusable | One route error; page/map consumers do not duplicate it |
| Slow map start with no failure; single tile error | No toast; delayed inline state if needed |
| Fatal map init; unsuccessful style swap with verified fallback | Error for init; warning for failed style change only |
| Unlock denied on iOS/browser gesture path | Overlay stays; inline retry; successful retry gives no toast |
| Required samples fail across many notes/voices | One audio warning/error; zero per-note/per-sample messages |
| Mute requested while audio calls fail | Attempt master mute; one actionable error if audio cannot stop |
| Storage blocked at first visit, then preference changed | No startup toast; one persistence warning only after explicit change and working memory fallback |
| Chart rendering fails but data table works | Inline fallback only |
| Modal open, small screen, dark/light theme, keyboard/screen reader | One readable announcement, no trapped focus or covered essential controls |
| Layout not ready, renderer crashes, presenter throws, component disposed | No recursive notifications or stale queue; persistent fallback where rendering permits |

## Limits and decisions to verify during implementation

The inventory is grounded in source inspection, not a live browser failure-injection run. Timing defaults and proposed copy are design choices. Exact pinned MatBlazor removal/update and accessibility behavior still require verification. Feed-health detection is intentionally deferred because no client evidence currently distinguishes legitimate silence from an upstream outage. Optional partial-route and partial-audio warnings remain disabled until reliable impact signals exist.

The central acceptance criterion is useful silence: a visitor should hear about a failure once, understand what is affected and what they can do, and never be asked to interpret the application's internal errors.
