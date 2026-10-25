# UI and Interop Contract: Capability Outcomes v1

Inventory IDs refer to [source design](../../../docs/TOAST_NOTIFICATIONS_DESIGN_PLAN.md) and [spec](../spec.md). Existing Core/Shared/browser ownership remains. Proposed types are narrow typed outcomes, not a generic operation framework. Safe resource copy represents meaning; raw errors stay diagnostic.

## Owner and evidence matrix

| IDs | Owner / required evidence | Feedback and recovery |
| --- | --- | --- |
| C1/C2 | SignalR/Core transport start/config and current JoinCity acknowledgment; ApplicationViewModel maps to one incident | Error: live could not connect; reload. Local persistent live-unavailable state. |
| C3 | Core Reconnecting while established delivery desired, >=10 continuous visible seconds | One warning: interrupted, reconnecting; recovery within grace silent. |
| C4 | Unexpected Closed/terminal delivery failure, excluding intentional stop | Error: updates stopped; reload. One escalation of C3. |
| C5 | Current desired Resume/JoinCity failed, not merely disconnected | Error: could not resume; reload. No false membership recovery. |
| C6 | Current protocol join rejection | Same live error, reload then try later if persistent; no deployment/sign-in detail. |
| C7 | Observed eligible network failure plus browser offline context | Qualify safe copy; offline/online alone silent. |
| C8/C9/C10 | AttentionGate/transport: intentional pause/leave/shutdown, hidden paused reconnect, empty/quiet feed/replay, stale individual vehicles/eviction | Silent. Reconnect cannot blind-rejoin while paused. |
| C11 | Current expected valid delivery with confirmed global batch-processing failure | One live processing error only if no root already explains it. Individual malformed records silent. Recover only verified successful valid batch processing. |
| R1/R2 | ApplicationViewModel completed failed/deadline/malformed or zero-usable-geometry current-city load, no usable current-city cache | One route error: reload for failed load; try another city/later for unavailable data. Persistent route status. Page/map consumers silent. |
| R3 | Explicit current selection is unusable and owner has reliable impact evidence | Prefer local selection feedback; otherwise one warning. Unselected partial invalid data silent. |
| R4 | Page/map cache consumer observes R1/R2 | Silent duplicate; HTTP success alone not route readiness. |
| R5/R6 | Existing Stats session initial/refresh failure | Inline error/retry/stale retained data; preserve cancellation/server retry instructions. Never global. |
| R7/R8/R9 | Stats disabled/empty/partial/pending/fallback, chart failure, formatting fallback, cancelled/obsolete result | Existing local state/table fallback only; no global toast. |
| R10 | Unused/hypothetical HTTP callers | No global request interceptor or future coverage assumed. |
| M1 | Map wrapper confirms fatal constructor/WebGL/required-layer init failure | Error plus persistent reload fallback; unsupported browser suggests another browser. Do not duplicate route root. |
| M2/M3 | Pending map, >=20 visible-second watchdog, isolated tile/glyph/image/source errors | Local delayed loading/reload option only; no timer-inferred failure toast. |
| M4 | Explicit style/theme operation failed and previous style/domain layers verified restored | One warning: previous style remains; revert confirmed control. Unusable map uses M1. |
| M5 | Ready map then terminal rendering failure OR two failed valid batches spanning >=10 visible seconds | Error for unusable primary display; warning for secondary layer only. Verified successful render clears failure. No per-frame callbacks. |
| M6 | Failed explicit display/checkpoint/trail/visibility setting without adequate local feedback | One settings warning, restore confirmed control. Hover/optional/background failures silent. |
| M7 | Pending geometry, pre-ready bounded buffer eviction, absent timing | Silent. Root belongs to R1/M1. |
| M8 | CityFab explicit navigation JavaScript failed while old page remains usable | One operation error: reload and retry; retain local action status. Normal full-page city reload silent. |
| M9 | Unknown hash default resolution / NotFound | Silent/existing inline NotFound; never claim requested unavailable city loaded. |
| A1 | Locked/pre-request, mute, filtered notes, normal loading/no crossings | Silent; absent audible output is not failure evidence. |
| A2 | Native unlock attempt rejected or context not running | Inline overlay error with fresh gesture retry and continue without audio; no toast/dismiss in finally. |
| A3 | Previously unlocked requested session fails automatic resume; actual audio-button native gesture can recover | One warning pointing to that working action. Local resume state. Gate until button is wired. |
| A4 | Required synth/Tone/dispatcher support failed after successful unlock/request and overlay dismissed | One error: audio unavailable; reload. Muted/speculative failures stored for reassessment, silent until actual requested impact. |
| A5 | Current required voices/sample/reverb unresolved >=10s or terminal failed; some vs none usable known | One audio warning if some work, error if none. Track finite palette slots internally, report capability transition only. Never note/sample/URL keys. |
| A6 | Explicit requested backfill texture failed while main transit audio works | One warning with reverted or truthful applied control; optional warming/percussion silent. |
| A7 | Requested mute/filter/unmute effect fails and playback contradicts control | Failed stop attempts master mute; only if still unable, error: close tab. Other change/filter failure warning, truthful control. |
| A8 | Individual scheduling/duration fallback/inactive cleanup/optional warmup/developer controls | Silent; established global failure only through A4/A5. |
| P1/P4 | No/version-changed/corrupt settings or initial write failure with working memory defaults | Silent startup. |
| P2 | Explicit change applied to all shared session readers, persistence failed | Once per storage incident warning: applies this session, could not save. Retain local save state. |
| P3 | Explicit setting cannot apply in memory/effect | One warning with confirmed controls, or A7 failed-stop error; suppress duplicate save warning. |

## Concrete outcome invariants

**Live**: transport Connected is necessary but not sufficient. Desired delivery plus current-city joined acknowledgment determines readiness. Pause is intentional state, not recovered join. Reconnecting clears membership; rejoin success only for same connection/scope while desired. In-flight desired changes reconcile. Lifecycle handlers capture connection/generation and detach; replaced connections are awaited/disposed. Init/Resume/Pause expose confirmed result/state; AttentionGate never changes `_joined` on swallowed failure or assumes initial true. Caller cancellation is silent; invalid config/version/start/join reasons remain bounded.

**Routes/HTTP**: one owner completes Pending -> Usable/Unavailable/Cancelled. Current-city cache with valid JoinKey and LineString (>=2 distinct finite coordinates within longitude/latitude range) may satisfy readiness. Invalid entries do not poison usable entries; all unusable is R2. Independent request deadline is failure; caller cancellation propagates through GET and deserialization instead of Result.Error. Resource copy never interpolates Result.Errors. Existing route-loaded boolean/task consumers can project richer outcomes without multiple fetches. No in-place retry promise until once-only initialization is deliberately redesigned.

**Map**: current map/style generation identifies callback validity. Readiness includes required sources/layers; JS return/timer is insufficient. Init failure retains a local fallback when rendering works. Explicit style result includes applied/restored/unavailable; restoration verifies domain layers before controls claim prior style. The four-second safety callback may initiate verification only. Nonterminal M5 counts failed valid batch attempts, not frames/no movement/absent routes; successful required render proves recovery. Dispose listeners/timers/interop references.

**Audio**: native gesture handler and Blazor click observe one attempt token/result; no independent competing unlock success flags. Attach before import await, warm module, call start/resume from real gesture when ready, otherwise require fresh gesture. Running context confirms success; failed/suspended outcome leaves overlay/retry. Continue without audio confirms mute and dismisses without marking unlocked. Later audio request must reacquire gesture. Explicit mute/filter result covers both synth and already-scheduled dispatcher's live gate. Attempt master mute before failed-stop error. Module/sample/reverb async rejections are observed and promises settle; fixed palette health reports coarse transitions only. Speculative failure can become A4/A5 only after current request reassessment. Preserve one shared uncachebusted synth module; no developer/legacy helper instrumentation.

**Settings**: browser-scoped memory snapshot initializes from valid storage/defaults; all FAB/blade readers share it. Explicit setting operation captures prior confirmed state, awaits relevant effect, commits memory if applied and saves independently. Concurrent operations use revisions so old completion cannot overwrite new request. On partial application compensate or show confirmed mixed/unavailable state, never persisted success. Failed apply wins over save warning. Storage recovery requires successful relevant persistence, not unrelated requests.

## Local ownership and exclusions

Stats retains existing error/retry/stale/cancel/accepted-data/table behavior and never reports its load failures to coordinator. Its modal visibility may defer the global host only. Unlock retains A2 local retry. Persistent route/live/map/audio/settings/action status uses the same resources without creating a second announcement of the same impact.

Analytics, viewport sizing, outside-click/visibility listener errors, cleanup/unsubscribe, optional metadata/blurbs/defaults, empty selection, legacy/debug audio and server/Worker/database/telemetry errors stay silent. A later concrete failed operation may be explained by its contextual owner. Pre-layout renderer/bootstrap failure keeps blazor-error-ui/persistent fallback; a broader error boundary is separate work. Presentation failures log once; no recursive notification.

## Delivery order and testability

First C1-C6/R1-R2/confirmed M1/local A2/requested A4 after outcome correction; then silent-case verification; then implement remaining conditional evidence and settings fallback before enabling corresponding feedback. No inventory row is replaced by generic catch coverage. Use bounded transport/interop fakes and controllable promises in automated tests; browser scenarios prove native gestures, asynchronous callbacks, modal layering/focus and one assistive announcement. Express failing observable tests before implementation (FR-022); detailed test enrichment precedes task generation.
