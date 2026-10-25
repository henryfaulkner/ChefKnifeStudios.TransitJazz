# Implementation Plan: Useful Failure Notifications

**Branch**: `061-enrich-failure-notifications` | **Date**: 2026-10-25 | **Spec**: [spec.md](spec.md)  
**Input**: [Warning and error notifications design](../../docs/TOAST_NOTIFICATIONS_DESIGN_PLAN.md). Planning only; no application changes, deployment, or commits.

## Summary

Correct confirmed live, route, map, audio and preference outcomes before explaining failures once. Existing owners report current-scope capability incidents to one bounded application coordinator. Retain the genuine MatBlazor 2.10.0 Toast renderer behind ToastService, with one handle, application timing and a minimal accessible layout wrapper. Stats and unlock errors remain local. Healthy paths and recovery are silent; conditional partial/render/audio warnings require implemented evidence.

## Technical Context

**Language/Version**: Existing C#/.NET 10, Razor/Blazor WebAssembly, JavaScript ES modules, CSS.  
**Primary Dependencies**: Existing MatBlazor **2.10.0**, SignalR .NET client, Ardalis.Result, Blazored.LocalStorage, CommunityToolkit.Mvvm, MapLibre GL JS 4, Tone.js 15; no new runtime package.  
**Storage**: Bounded browser-memory incidents/current outcomes; existing localStorage with shared in-memory settings fallback. No database/history/inbox.  
**Testing**: Existing net10.0 Client.Shared.Tests xUnit 2, hand-written bounded spies/stubs and controllable monotonic TimeProvider; narrow installed-MatBlazor contract and local browser system checks for gesture, real callbacks, overlays, focus, visible time and assistive behavior. [Testing plan](testing-plan.md) completes FR-022 enrichment before tasks, including T01-T09 traceability, red-green-refactor slices and B01-B23 source acceptance evidence. No new test framework/package is planned.  
**Target Platform**: Existing Azure Static Web App WASM, desktop/mobile browsers including Safari/iOS gesture path. Existing server wire contract unchanged.  
**Project Type**: Client correctness and transient presentation within existing projects.  
**Performance Goals**: O(fixed issue-code count) state/evaluation, one next-deadline timer, one visible toast, zero renderer backlog; no new polling or per-note/frame/record .NET callbacks.  
**Constraints**: Fixed 2-second burst; 10-second continuous visible reconnect/foreground grace; 30 healthy seconds to re-arm; >=10 seconds automatic presentation spacing except failed audio stop; warning/error 8/12-second reading time. No raw errors/success toasts.  
**Scale/Scope**: One page/city scope, one record per fixed code, six capabilities; full production inventory C1-C11/R1-R10/M1-M9/A1-A8/P1-P4 and exclusions. No server/Worker log broadcasting.

## Constitution Check

Constitution **v3.3.2** checked before research and after design. Gates pass for this feature; actual browser behavior remains a release proof.

| Principles / rule | Initial / post-design | Design response |
| --- | --- | --- |
| I-II architecture/secrets | Pass / Pass | Core emits typed outcomes without presentation dependency; resource-backed safe copy only. |
| III, VI pipeline/identity | Pass / Pass | No Worker/wire/JoinKey semantic change; validate current-city usable geometry. |
| IV-V observability/delivery | Pass / Pass | Bounded classifications in structured logs, existing CI/artifacts. |
| VII-X map/music/selection | Pass / Pass | Preserve map layers, deterministic voices, empty-set unscoped behavior and master mute priority. |
| XI overlays | Pass / Pass | Immediate removal; static toast introduction permitted; no overlay timing redesign. |
| XII localization | Pass / Pass | All new text/ARIA keys use existing RouteFilterResources.resx and language variants where present. Current English-only presentation remains; no new language UI/store. The existing language-control gap against the constitution is not newly solved by this feature. |
| XIII themes | Pass / Pass | Explicit palette-based light/dark overrides; package colors do not prove contrast. |
| AGENTS Git rule | Pass / Pass | Skip commit hooks; preserve NEVER COMMIT section. |

No new constitution exception is needed. The pre-existing English-only/language-control gap remains outside this change, as in the previous plan; do not claim full multilingual UI compliance.

## Project Structure

### Documentation

```text
specs/061-enrich-failure-notifications/
  spec.md
  checklists/requirements.md
  plan.md
  research.md
  data-model.md
  contracts/notification-policy-v1.md
  contracts/capability-outcomes-v1.md
  quickstart.md
  testing-plan.md                  # pre-task layer/TDD/traceability enrichment
  tasks.md                         # next workflow; not created by planning
```

### Source Code (existing seams; new names are proposals)

```text
src/Client/ChefKnifeStudios.TransitJazz.Client.Core/
  Services/{SignalRNotificationService,AttentionGate,HttpService}.cs
  Services/EndpointsServices/GtfsEndpointsService.cs
  Services/LiveDeliveryState.cs                         [new outcome]
src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/
  Services/Notifications/{NotificationCoordinator,NotificationIssue,NotificationPolicy}.cs [new]
  Services/{ToastService,SettingsService}.cs
  Services/SettingsChangeController.cs                  [new explicit-effects owner]
  Services/JsInterop/{ITransitSynthJsInterop,TransitSynthJsInterop}.cs
  ViewModels/{ApplicationViewModel,RouteFilterViewModel}.cs
  Components/Notifications/NotificationHost.razor[.css]  [new]
  Components/AudioUnlockOverlay.razor
  Components/Map.razor[.cs,.Helper.cs]
  Components/{FABs,Blades}/                             [existing action controls]
  Components/Stats/StatsOverlay.razor                    [modal visibility only]
  Resources/RouteFilterResources.resx
  wwwroot/js/{map-interop,transit-synth,crossing-dispatcher}.js
src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/
  Program.cs
  Layout/MainLayout.razor
  Pages/TransitMap.razor[.cs]
  wwwroot/css/app.css                                  [pinned Toast overrides]
src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/
  Notification*Tests.cs / *ReadinessTests.cs / Settings*Tests.cs [new]
  AttentionGateTests.cs / existing Stats*Tests.cs
```

**Structure Decision**: Core owns transport/HTTP facts, ApplicationViewModel owns route load and live-state projection, Map/TransitMap and synth adapter own coarse browser outcomes. One pure coordinator and one layout presenter. No bus, generic event framework, new project, or blanket exception interceptor.

## Design Details

### Lifetimes and scope

A browser-application coordinator depends only on TimeProvider/pure policy. Existing singleton owners can report without resolving scoped presentation services. MainLayout mounts NotificationHost inside its theme and registers a presenter after first render. Its delegate uses InvokeAsync and scoped ToastService/IMatToaster; disposal unregisters and cancels/removes owned work. No singleton constructor resolves IToastService, MatBlazor, localStorage, or the root provider. Change SettingsService from transient to browser-scoped shared snapshot; a scoped settings-change owner coordinates effects and saves. No broad rewrite of unrelated existing singleton graphs.

Invalidate page/city scope before leaving. Connection/map-instance/audio-attempt/setting-selection operation generations reject obsolete results; generations are counters, not incident identities. Startup incidents survive late host registration but are revalidated. Full city reload starts a fresh application. All callbacks, pending timers and DotNetObjectReferences have explicit cleanup.

### Truthful outcomes before feedback

SignalR exposes transport, desired delivery, current-city membership, reason and connection generation. IsConnected requires connected transport plus confirmed current join while desired. Start/config/join/version failures stay failed. Reconnecting/Closed invalidate membership. Reconnected captures its own connection and rejoins safely only if desired; an in-flight join followed by pause must reconcile/leave. Await stop/disposal of replaced connections and detach handlers. AttentionGate must update confirmed membership only after successful outcomes, including initial failure and current connection replacement, rather than an assumed initial joined flag.

Route readiness distinguishes pending, usable, unavailable/invalid and caller cancellation. Require current-city JoinKey and usable LineString geometry (at least two distinct finite, geographically valid points); preserve identity semantics. Partial usable data may serve; zero usable current-city data is unavailable. ApplicationViewModel owns R1/R2 once; page/map observers never duplicate. Propagate caller cancellation through GET and parsing, distinguish independent deadlines, and preserve one-shot semantics unless explicitly redesigned. Reload is the initial action; no in-place Retry is promised.

Audio has one native gesture attempt/result observed by Blazor. Arm listener before import awaits; warm the module before interaction and call start/resume within the native event when available. A click before import readiness must request a fresh gesture when ready, not assume activation survives an asynchronous import. Only running AudioContext confirms unlock. Failure retains overlay and re-arms with inline retry/continue muted; continuation is not unlock. Observe asynchronous module/sample/reverb failures beyond the initial call, make promises settle, retain pre-request failure for reassessment, and preserve the single exact synth URL/module instance.

Map readiness follows confirmed initialization and required domain-layer creation. Fatal constructor/WebGL/required-layer failure produces persistent fallback; isolated resource errors stay silent. A 20 visible-second watchdog only changes local loading feedback. Style completion requires verified sources/layers and applied style; a timer may trigger verification, not establish success. Verified previous-style restoration permits M4; unusable map uses M1. M5 requires terminal evidence or two failed valid batches spanning >=10 visible seconds, and verified later render for recovery. No per-frame .NET callbacks.

### Incident policy and genuine MatToast presentation

Follow the [policy contract](contracts/notification-policy-v1.md): fixed burst, continuous visible grace, capability evidence, bounded suppression and healthy re-arm. During spacing/hidden/modal blocks retain incidents, not toast objects; revalidate at next permitted presentation. Mark included/explained children communicated. A warning may escalate once to error subject to spacing; only failed audio stop bypasses spacing. Dismissal and online events never prove recovery.

Pinned MatBlazor supports Add returning MatToast and Remove(handle). MaxDisplayedToasts limits rendering only and retains hidden additions, so configure one as a safeguard and **never add a backlog**. ToastService owns one handle; remove before replacement. RequireInteraction=true, show/hide transitions zero, progress disabled; application TimeProvider allows 8/12 seconds visible reading, paused while hidden/hovered/focused. Observe OnToastsUpdated membership for removal, because direct Remove does not fire OnClose. Presenter failure logs once per host lifetime and retains local status; never recursively notify.

NotificationHost contains the **sole genuine MatToastContainer**, one native sibling close button and one pre-existing atomic live region. Disable the package mousedown-only close; native click supports keyboard and localized accessible name. Announce severity/message once after successful add, never on package timer updates. No focus on appearance; manual close restores the previous connected control if focus was inside, and automatic timeout pauses while focus is inside. Keep useful impact/recovery details in persistent local state so disappearance does not remove the only explanation.

Top-center host uses mobile safe-area insets, bounded width and clear navigation. Override pinned package fixed positioning, z-index and colors at the host boundary in both themes; honor reduced motion. A2 remains local. Stats or blocking overlays that prevent global access defer global presentation until close and revalidation; no second host. Modal visibility is an accessibility gate, not a new source of incidents.

### Complete inventory and explicit settings effects

The [outcome contract](contracts/capability-outcomes-v1.md) maps all rows to owners, evidence and presentation. First deliver C1-C6, R1/R2, confirmed M1, inline A2 and requested A4; verify muted, paused, cancelled and obsolete negatives before broadening. C11/R3/M4-M6/A3/A5-A7/P2-P3 then require reliable coarse evidence and working actions. All remain part of this feature, with unsupported warnings disabled until their prerequisites pass.

Settings effects apply before committing shared confirmed state and saving. Failed effects revert controls/snapshot; partially applied effects require compensating restore or explicit confirmed mixed/unavailable state. One operation reports effect **or** save failure. Session-only warning requires changed preferences actually applied across every control in memory. Startup default/save failures stay silent. Mute failure attempts master mute before a tab-close error. A3 copy remains gated until AudioFab performs real native gesture resume.

## Phase 0: Research Results

[research.md](research.md) resolves source seams, pinned MatBlazor API/lifecycle, dependency direction, timing, gesture and accessibility choices. No unresolved technical clarification. Browser and evidence-instrumentation proofs remain implementation/release work.

## Phase 1: Design Outputs

[data-model.md](data-model.md), [policy contract](contracts/notification-policy-v1.md), [outcome contract](contracts/capability-outcomes-v1.md), [quickstart.md](quickstart.md). Update AGENTS SPECKIT marker directly (no update-agent-context script exists) and preserve other instructions. Post-design gates pass.

## Phase 2: Task Handoff

Pre-task testing enrichment is complete in [testing-plan.md](testing-plan.md); no tests or browser proofs have run in this planning pass. Tasks must use its T01-T09 requirement/story/SC and full inventory mapping, ordered red-green-refactor slices, deterministic boundary/concurrency rules and B01-B23 mapping of every source acceptance row. Meaningful expected behavior must fail before its application implementation; after green, simplify with the targeted test passing. Missing-API compile failures alone do not establish the behavioral red step.

Order: truthful transport/routes/unlock/local fallbacks -> coordinator/host -> initial owner coverage and healthy/muted/background/obsolete silence -> conditional evidence/settings fallback -> complete inventory/browser/accessibility acceptance. Exercise actual production reconnect handlers with controlled transport completions; the existing fake's simulated reconnect policy cannot prove them. Keep policy permutations in desktop xUnit, installed renderer contracts distinct from spies, and trusted gesture/DOM/focus/assistive/layout proof in the real browser. Each conditional row needs positive evidence, silent insufficient-evidence tests and a working promised action before enablement.

Use the testing plan's baseline/targeted/full-client/Release-build commands and record red/green plus browser/device/screen-reader evidence. Current client CI publishes without running tests; publish success is not a coverage gate. Required browser/device proofs left unrun stay explicit pending release gates. No blanket retries/feed-health detector, broader error boundary, production deployment or commit.

## Complexity Tracking

No new constitution violations or runtime dependencies. The small coordinator and host adaptation implement specified behavior unavailable in the existing renderer; no generic notification framework.
