# UI Contract: Notification Policy v1

Applies to the production browser only. Governing requirements FR-001 through FR-022 and [source inventory](../../../docs/TOAST_NOTIFICATIONS_DESIGN_PLAN.md). No new endpoint or server event.

## Reporting boundary

Owners report **typed current outcomes**, not exception strings. A report carries fixed issue code/capability, severity, scope and owner generation, requested/desired state, terminal or bounded partial evidence, local explanation ownership, and optional confirmed cause. A recovery report must prove that same capability is healthy. Proposed coordinator operations are Report/Resolve, SetScope/Visibility/PresenterAvailability and Evaluate; names may adapt to existing conventions. No component except the coordinator's registered presenter calls IToastService for this feature.

Reject before aggregation: obsolete scope/operation/connection, caller cancellation, disposal, intentional shutdown/pause, muted/pre-request speculative audio, adequate local explanation, and already-explained impact. A failed independent deadline is not caller cancellation. Browser offline state qualifies copy only alongside an observed failed network capability; online triggers reassessment only.

Local ownership is an inventory decision (especially Stats, unlock, and selected-route feedback), not an automatic test for the existence of any text. A persistent fallback required alongside a first core-failure toast (for example M1) does not suppress that specified first announcement; it supplies lasting explanation afterward. Do not create a second live announcement when that fallback is rendered with the toast.

The finite policy maps codes into capability groups:

| Group | Inventory | Eligible impact |
| --- | --- | --- |
| Live startup/join/version | C1/C2/C5/C6 | Same live error lifecycle with safe reason-specific copy. |
| Live interruption | C3/C4 | Grace warning, optional one terminal escalation. |
| Live processing | C11 | Confirmed global processing failure, deduplicate against roots. |
| Route unavailable | R1/R2 | Same owner; failure vs completed no usable geometry wording. |
| Selected route impact | R3 | Only explicit selected unusable route without local explanation. |
| Map unavailable | M1/M5 | Root unusable map/render impact; no secondary duplicates. |
| Map/style setting | M4/M6 | Verified fallback/failed explicit effect, settings incident as appropriate. |
| Navigation | M8 | Failed explicit navigation while old page usable. |
| Audio resume/support/voices | A3/A4/A5 | One requested-audio capability message at applicable severity. |
| Audio explicit texture/filter/stop | A6/A7 | Truthful confirmed effect; failed stop has special safety action. |
| Settings application/persistence | P2/P3 | Apply failure wins over save warning for same action. |

Remaining rows are silent/local decisions in [capability contract](capability-outcomes-v1.md). Codes stay finite; route, vehicle, URL, sample, exception text and timestamps cannot become incident identity.

## Time and selection rules

1. Established reconnect interruption is eligible only after **10 continuous visible seconds** with delivery desired. Hide/pause resets grace; repeating reports do not reset it. Terminal C4/C5 evidence is immediately eligible. Foreground reassesses intended delivery/audio with a new 10-second allowance unless terminal failure is already known.
2. A5 requires currently needed voice-load evidence for **10 unresolved seconds** or terminal failure, independent from detecting audible silence. M5 nonterminal detection requires two failed valid batches spanning **at least 10 visible seconds**. R3 needs explicit current selection evidence.
3. First eligible uncommunicated impact opens a fixed **2-second burst**. Reports at t=0, 0.4 and 1.2 present once at t=2. Repeats do not extend it. Recovery/staleness/local ownership before commit removes the candidate.
4. Present no sooner than **10 seconds after previous automatic presentation**. Terminal escalation obeys spacing; confirmed inability to stop audio may bypass spacing. A lone new issue during spacing still completes its fixed burst, then waits for the next permitted opportunity. Revalidate all current incidents then; never store a prebuilt toast for later.
5. Highest severity wins. Same capability has one message. Confirmed common cause may explain child impacts; unrelated causes name at most two capabilities, otherwise general app wording. No numeric request counts, raw error lists or invented network outage. Inline-only issues do not increase the summary.
6. Successful presentation marks all included and explicitly explained/suppressed impacts communicated. Dismissal/reading timeout cannot reset them. Repeats at t=4,8,20 remain silent. Failed presentation does not claim communication; log once, preserve local state, avoid recursive or tight-loop retries.
7. One newly eligible error can replace an active warning once, after revalidation/spacing. Remove before Add. Same-severity changes never repeatedly announce the same incident. A distinct unrelated incident is not automatically swallowed; it follows burst/spacing and current-state rules.
8. Actual recovery cancels pending candidates and removes obsolete display where the presentation no longer applies. A mixed summary containing recovered and unresolved impacts may be removed and unresolved details remain local; do not reannounce its already-communicated remainder. Recovery is silent. Re-arm only after **30 continuous healthy seconds** for the capability; flapping before then stays suppressed.
9. Hidden/unavailable host/modal-blocked states permit zero presentations. Track current outcomes only. Returning/host registration/modal close triggers reassessment, never historical replay. Obsolete scope disposal cancels all pending work.

All policy durations use monotonic TimeProvider. One next-deadline timer per coordinator suffices; owner operations may have their own bounded deadline/watchdog. No real sleeps in policy tests.

## Genuine MatBlazor adapter contract

- One MatToastContainer in NotificationHost; Warning -> Warning, Error -> Danger. PreventDuplicates=true and MaxDisplayedToasts=1 are safeguards only.
- ToastService calls verified pinned Add(..., configure), retains returned MatToast handle, removes it before replacement, and never adds a pending object. No hidden queue. Clear is only for this exclusively owned renderer; prefer Remove(handle).
- RequireInteraction=true; ShowTransitionDuration=0, HideTransitionDuration=0, ShowProgressBar=false, ShowCloseButton=false. Application controls timeout using 8/12 seconds **visible reading time**, paused hidden/hover/focus. Closing is immediate.
- Host supplies a native localized close button adjacent to the genuine rendered toast. No custom vendor template/API is assumed. OnToastsUpdated verifies handle removal; direct Remove does not fire OnClose. Unsubscribe on teardown.
- One pre-existing role=status, aria-atomic announcement region receives severity and plain-text message once after a committed add. Vendor markup has no live role; do not nest an additional alert or rewrite live text on ticks. No sound or focus movement on appearance.
- If keyboard focus is inside when manually closed, return to the previously connected meaningful control as needed; never steal focus from elsewhere. Pause timeout while focused. Every toast expires after its visible reading budget even if the incident remains unresolved. Do not add persistent floating live/route/audio status cards; preserve internal incident state and component-local feedback.
- Bottom-left on desktop; bottom-center above the icon controls below the page's 1100px mobile breakpoint, within safe-area insets and bounded width. The host and its close control share one positioning box. Override pinned vendor positioning/z-index/colors for both themes. Reduced motion and immediate dismissal are explicit. Contrast/touch target/focus verification is browser acceptance work.
- Defer global presentation while a blocking overlay/modal makes it inaccessible; unlock/Stats retain their local failures. Revalidate on close; no second container.
- Host registers after first render through InvokeAsync, unregisters on disposal, cancels reading timer, clears announcement/handle, and rejects stale presentation IDs. Toast failure logs once and never reports another toast failure.

## Minimum acceptance examples

| Controlled sequence | Required observation |
| --- | --- |
| Healthy startup/unlock/settings/city/Stats/recovery | Zero global Add calls. |
| Three eligible unrelated capabilities within burst | One safe highest-severity summary; no counts. |
| Repeats/dismissal/unresolved retry | No duplicate Add or extended grace/display. |
| Reconnect recovers before grace; burst candidate resolves | No Add; truthful current readiness. |
| Reconnect 10s -> warning -> terminal | One warning, at most one replacing error, spacing respected. |
| Capability recovers -> fails after 29s / after >=30s healthy | Suppressed / new incident allowed. |
| Hide/modal/late host, then recover or scope change | No obsolete message on return/registration/close. |
| Failed audio stop during spacing | Master mute attempted first; if still failed one error may bypass spacing. |
| Renderer fails | One diagnostic, local status remains, no recursive notification. |

The pre-task [testing plan](../testing-plan.md) defines TDD slices, deterministic boundary/concurrency isolation, full inventory traceability and B01-B23 browser evidence. Unit invocation counts do not prove pinned package behavior, screen-reader announcements or trusted gestures.
