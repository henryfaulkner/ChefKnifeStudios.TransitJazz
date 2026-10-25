# Quickstart: Implementing and Validating Useful Failure Notifications

Planning output only. Do not create commits or execute optional before/after commit hooks. Preserve unrelated working changes. No deployment, migration or server configuration change is required.

## Read before implementation

Read [spec](spec.md), [plan](plan.md), [testing plan](testing-plan.md), [research](research.md), [data model](data-model.md), [policy contract](contracts/notification-policy-v1.md), [outcome contract](contracts/capability-outcomes-v1.md), and the [governing design](../../docs/TOAST_NOTIFICATIONS_DESIGN_PLAN.md). Pre-task testing enrichment is complete in the testing plan; task generation must preserve its T01-T09 coverage, ordered red-green-refactor slices and B01-B23 source-acceptance evidence. Names in the plan are proposed seams; do not build a generic notification framework.

## Baseline and TDD workflow

From repository root in PowerShell:

```powershell
git status --short
dotnet test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj
dotnet build src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj
```

Record baseline failures before editing. For each meaningful behavior, first write a failing observable test, run the targeted case and observe its intended assertion failure after any minimal API seam compiles; implement enough to pass, then simplify while keeping it passing. Use existing xUnit and hand-written spies/fakes, controllable monotonic TimeProvider/deadlines including scheduled callbacks, bounded connection callbacks and pending interop completions. Drive actual production reconnect handlers rather than reproducing policy inside a fake. Do not use wall-clock sleeps, live feeds or network outages as unit tests. No unit invocation count can prove package behavior, browser gesture or screen-reader behavior. See the testing plan for exact boundary, controlled completion and isolation rules.

Order implementation/test slices:

1. Truthful transport/join/intentional pause and replacement cleanup; usable-route validation/cancellation; confirmed retryable audio unlock and persistent map/live/route fallback.
2. Pure bounded incident policy: fixed burst, grace, suppression, re-arm, spacing, scope/generation, hidden/modal/late-host and safe aggregation.
3. Genuine single MatToast adapter/host: one handle, no backlog, immediate removal/replacement, native close, one announcement and both themes.
4. First owner coverage C1-C6/R1-R2/confirmed M1/local A2/requested A4; verify healthy/muted/background/obsolete negatives before widening.
5. Conditional evidence C11/R3/M4-M6/A3/A5-A7 and shared settings apply/save fallback P2/P3. Test insufficient evidence stays silent before enabling each.
6. Full inventory/browser/accessibility acceptance, existing Stats regression, and user review of uncommitted changes.

## Deterministic policy examples

- Eligible failures at 0, 0.4, 1.2 seconds -> one highest-severity summary at 2; repeats at 4/8/20 and dismissal -> no repeats.
- Reconnect recovers before 10 continuous visible seconds -> zero toast; unresolved grace -> warning; terminal failure -> at most one replacing error subject to 10-second spacing.
- Recovery before burst/display cancels; actual recovery plus 29 healthy seconds still suppresses flapping; >=30 healthy seconds re-arms. Hide/pause is not health proof.
- Hidden/modal/late host sees recovered or obsolete work -> no replay. Unresolved current failure is reassessed; foreground live/audio gets grace unless terminal.
- Three Stats failures -> zero global toast; local retry/stale policy unchanged. Inline unlock failure never enters a burst.
- Failed setting effect plus failed storage -> only effect message. Blocked first-visit storage -> silence with shared defaults. Later applied change unsaved -> one persistence warning.

## Browser failure injection

Use the established local development launch configuration and disposable browser state. Inject at controlled transport, HTTP, module/interop and storage boundaries; do not alter production feeds/configuration. Keep fault injection test-scoped, not a visitor-facing debug panel or new endpoint.

Validate desktop plus mobile viewport, light/dark themes and a real Safari/iOS gesture path where available. Include initial hub/config/join/version failure, reconnect within/beyond grace, reconnect while paused, failed rejoin, malformed/all-unusable routes, old-city completions, slow map without terminal evidence, fatal init and style restore, unlock denial/retry/continue muted, requested module/voice failure, failed audio stop/master-mute fallback, blocked storage and renderer/presenter/disposal failure.

Run quiet healthy startup, unlock, route selection, theme/preference change, city switch, Stats open/refresh/close and recovery with a presentation spy/visible count: **zero toasts**. Quiet feeds/no notes, individual tile/sample/note/record errors, optional warming and metadata also remain silent unless their explicit capability gates are proved.

Check the single genuine MatToast with audio overlay, Stats dialog, city menu and settings blade open: defer/revalidate when inaccessible; do not duplicate host. Verify native close via Tab/Enter/Space and touch, no focus theft, stable focus after dismissal, safe areas and essential controls reachable, reduced motion, readable contrast, 8/12 visible-second reading periods paused while focus/hover/hidden, and exactly one actual screen-reader announcement per presentation. Persistent local impact/action remains readable after dismissal. Record browser/device/screen-reader versions and outcomes; DOM role checks alone are insufficient.

## Completion checks

Use the [testing plan's commands and evidence gates](testing-plan.md#commands-and-evidence-gates): run the full affected Client.Shared.Tests suite (including AttentionGate, route/city/selection and Stats), build the WebApp in Release, and record red/green targeted results. Repeat broader checks only for new changes or unresolved failures. Inspect resource keys and both theme styles; prove pinned MatBlazor Add/Remove options/membership with the real dependency, one genuine MatToastContainer, bounded incidents and zero hidden queue. Complete B01-B23 plus conditional-inventory checks, recording browser/device/screen-reader versions and actual outcomes. Current client CI publishes without running tests, so publish success does not establish these gates. Unrun required browser/device proofs stay pending release gates. No runtime checks were performed by the documentation enrichment. Leave changes uncommitted for review; no production enablement occurs in this workflow.
