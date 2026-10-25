# Research: Useful Failure Notifications

**Date**: 2026-10-25. Based on repository source and primary references. A research agent verified installed MatBlazor 2.10.0 by decompiling the NuGet DLL and comparing upstream source; no application changes.

## 1. Readiness before notification

**Decision**: Enrich coarse production outcomes at existing ownership boundaries before presentation.

**Evidence**: SignalRNotificationService swallows configuration/start/join failures, lacks Reconnecting/Closed handling, replaces connections without awaiting stop/disposal, and rejoins through a mutable field. ApplicationViewModel then sets IsConnected=true on normal return. AttentionGate assumes initial membership and confirms a swallowed resume failure. Routes validate JoinKey only and mark empty success loaded; HttpService GET/parsing catch caller cancellation. TransitMap unlock dismisses in finally; synth wrappers swallow failures. Map calls swallow outcomes and style restoration's safety timer resolves without proof. SettingsService is transient/storage-only; initial default write can throw outside its read catch.

**Rationale**: Returned calls and saved preferences cannot prove user capability readiness. The [SignalR .NET client documentation](https://learn.microsoft.com/en-us/aspnet/core/signalr/dotnet-client?view=aspnetcore-10.0) distinguishes transport lifecycle, reconnect and initial start; current-city join confirmation is an additional application requirement.

**Alternatives considered**: Global HTTP/exception-to-toast hooks lack requested state, city generation, cancellation and local feedback context. Broad unrelated API refactoring is unnecessary; change production paths in the inventory only.

## 2. Pinned MatBlazor 2.10.0 API and lifecycle

**Decision**: Keep genuine MatToast; own aggregation, one-handle lifecycle and reading timing in application code.

| Verified behavior | Design consequence |
| --- | --- |
| Add(string, MatToastType, title, icon, Action<MatToastOptions>) returns MatToast | Adapter stores one handle; Warning/Danger retained. |
| VisibleStateDuration is milliseconds; RequireInteraction prevents auto-dismiss | Use RequireInteraction=true and app TimeProvider for 8/12 visible seconds. |
| ShowTransitionDuration/HideTransitionDuration exist | Both zero; no package progress/exit animation. |
| Remove(handle), Clear() exist; title/message immutable | Remove before a freshly revalidated replacement. |
| MaxDisplayedToasts uses Take(N) but retains added items | Configure one and never add pending presentations to renderer. |
| No Add<T>/RenderFragment/custom content; base ChildContent is not rendered | Accessible close/live region belong in app host wrapper. |
| Remove/Clear do not emit MatToast.OnClose | OnToastsUpdated and handle membership identify all removals. |
| Actual AddMatToaster registers scoped IMatToaster | Pure coordinator cannot constructor-inject it. |
| Container/item unsubscribe updates; item disposes transition timer | Application similarly detaches callbacks/timers at disposal. |
| Package timers use DateTime.Now/System.Threading.Timer | Do not test incident policy through vendor timers. |

**Rationale**: Renderer duplicate prevention/max count do not implement incident suppression, recovery or freshness. Native removal is supported, so FR-014 finish-and-revalidate fallback is not needed for this pinned version.

**Alternatives considered**: New package, invented options from another library, mutating immutable message, hidden backlog and second container rejected.

**Primary sources**: [Toast docs](https://www.matblazor.com/Toast), [IMatToaster](https://github.com/SamProf/MatBlazor/blob/master/src/MatBlazor/Components/MatToastContainer/IMatToaster.cs), [MatToaster](https://github.com/SamProf/MatBlazor/blob/master/src/MatBlazor/Services/Toast/MatToaster.cs), [container](https://github.com/SamProf/MatBlazor/blob/master/src/MatBlazor/Components/MatToastContainer/BaseMatToastContainer.cs), [options](https://github.com/SamProf/MatBlazor/blob/master/src/MatBlazor/Services/Toast/MatToastCommonOptions.cs). Master URLs support review; installed **2.10.0 DLL** supplied version-specific verification.

## 3. Accessibility requires a small host adaptation

**Decision**: One MatToastContainer, package close/progress disabled, native sibling close button and single atomic announcement region; explicit position/colors at host boundary.

**Evidence**: Pinned [markup](https://github.com/SamProf/MatBlazor/blob/master/src/MatBlazor/Components/MatToast/MatToastItem.razor) lacks a live region. Its MatIconButton close is wired via OnMouseDown with no supplied localized accessible name, so a keyboard-generated click does not reliably close. [Styles](https://github.com/SamProf/MatBlazor/blob/master/src/MatBlazor.Web/src/matToast/matToast.scss) hardcode colors, 300px size and high z-index, without safe-area/reduced-motion rules. [Item lifecycle](https://github.com/SamProf/MatBlazor/blob/master/src/MatBlazor/Components/MatToast/BaseMatToastItem.cs) updates timer state; that is not an announcement policy.

**Rationale**: A native button avoids patching vendor DOM. One pre-existing role=status/aria-atomic region is updated only after committed presentation, without nested alerts. Appearance never focuses; timeout pauses during focus/hover; local text keeps useful impact/action after dismissal. [WAI status guidance](https://www.w3.org/WAI/WCAG21/Understanding/status-messages.html) supports announcement without focus. [WAI timing guidance](https://www.w3.org/WAI/WCAG21/Understanding/timing-adjustable.html) means a fixed toast interval alone does not prove accessibility; important information remains available without that limit.

**Alternatives considered**: Assuming theme provider proves contrast, role=alert around repeated updates, unsupported ChildContent, DOM-handler replacement, or a second modal container rejected. Static appearance meets reduced-motion and immediate-exit needs simply.

## 4. Bounded policy and browser lifetimes

**Decision**: Fixed issue codes/six capabilities, scope and owner-generation tokens, one current dictionary and one next-deadline timer. No chronological queue. Singleton pure coordinator; layout registers a scoped presenter using InvokeAsync after first render.

**Rationale**: Startup begins before layout; current state survives host readiness without replay. Reconnect/foreground grace uses continuous visible desired time and resets across hide/pause. Re-arm needs actual healthy capability, not dismissal. Spacing/modal suppression retains incidents only. No singleton-to-scoped presentation dependency is introduced.

**Alternatives considered**: Per-catch timers, timestamp/route-keyed incidents, root-provider resolution, startup event replay, bus/history frameworks rejected. Existing local events may expose settings/modal state without becoming a notification bus.

## 5. Trusted gesture and asynchronous audio

**Decision**: One native gesture owns one attempt/result. Only running AudioContext confirms unlock. Re-arm failures, retain overlay, preserve single exact synth URL, observe async required support/sample/reverb outcomes at capability granularity.

**Evidence**: transit-synth has independent native/Blazor unlock paths, a removed one-shot listener, unhandled warm imports and reverb.generate rejection that can leave a sampler promise pending. Crossing dispatcher calls async triggerNote without observing rejection. Fire-and-forget resume suppresses failures.

**Rationale**: The [Web Audio resume algorithm](https://webaudio.github.io/web-audio-api/#dom-audiocontext-resume) includes start restrictions/pending behavior. Beginning a promise in a click does not prove activation survives slow import. Warm first, resume inside the native event when available, and request a fresh gesture if not ready. A5 requires an identifiable currently needed voice operation unresolved for 10 seconds or terminal failure; absence of notes never qualifies.

**Alternatives considered**: Dismiss in finally, `_unlocked` flag alone, speculative global errors, cache-busted synth copy, per-note .NET callbacks rejected.

## 6. Map and preferences evidence

**Decision**: Verify required-layer initialization/render and style restoration separately from pending loading. Apply effects before committing/saving shared confirmed preferences.

**Rationale**: [MapLibre Map API](https://maplibre.org/maplibre-gl-js/docs/API/classes/Map/) exposes lifecycle/style operations; generic resource errors can be recoverable. Current four-second restore callback cannot prove success. R3 requires explicit selected unusable route, M5 failed valid batches or terminal evidence, A5 required voices. Session-only save copy requires actual shared applied memory state. One action reports application or persistence failure, not both.

**Alternatives considered**: All map errors fatal, timer-only success/failure, warnings for unselected invalid routes and storage-only settings rejected.

## Remaining proof

No unresolved technical choices require clarification. Trusted browser gestures, asynchronous callback cleanup, screen-reader count, native close focus, reading periods, contrast, layering and safe areas require implementation/browser evidence. Timing and copy are adopted choices, not measurements. Conditional instrumentation must pass its evidence gates before warning enablement. No client contract proves upstream feed outage; feed-health detection stays excluded.
