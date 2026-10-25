# Feature Specification: Useful Failure Notifications

**Feature Branch**: `061-enrich-failure-notifications`  
**Created**: 2026-10-25  
**Status**: Draft  
**Input**: Enrich warning and error notifications for the production visitor experience, guided end to end by [Warning and error notifications: design and implementation plan](../../docs/TOAST_NOTIFICATIONS_DESIGN_PLAN.md). Preserve useful silence, conservative coverage, incident aggregation, exclusions, and readiness prerequisites. Include test-driven development (TDD) and automated/browser coverage in subsequent planning.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Understand a failed core experience (Priority: P1)

As a visitor, I receive one understandable warning or error when a requested core capability actually fails, with a truthful next action and enough persistent explanation to understand an ongoing failure.

**Why this priority**: A silent broken map or falsely connected live experience leaves the visitor unable to understand or recover from a failure.

**Independent Test**: Exercise failed initial connection, failed city subscription, unusable route data, confirmed map initialization failure, and requested audio failure individually. Verify one eligible notification per incident, the appropriate local status, and no falsely successful state.

**Acceptance Scenarios**:

1. **Given** a visitor starts a supported city, **When** initial live connection fails or its city subscription fails, **Then** live delivery remains unavailable and one error explains that live transit could not connect and offers reload.
2. **Given** transport has reconnected, **When** the city cannot be rejoined, **Then** delivery is not represented as recovered and one actionable live-delivery error is eligible.
3. **Given** a completed current-city route load, **When** it fails with no usable current-city data or contains no usable route geometry, **Then** one route error explains the outcome and dependent map/page observers add no duplicate warning.
4. **Given** map startup is still pending without terminal evidence, **When** 20 visible seconds pass, **Then** delayed local loading feedback may offer reload and no toast claims failure; a confirmed terminal failure instead produces one error and a persistent map fallback.
5. **Given** audio was requested and the unlock overlay was dismissed after a confirmed successful unlock, **When** required playback support fails, **Then** one audio error offers reload. A speculative failure before audio is requested remains silent until reassessment establishes current impact.

---

### User Story 2 - Stay informed without a stream of interruptions (Priority: P1)

As a visitor, simultaneous failures are summarized once, repeated failures remain quiet, and recovery does not add another interruption.

**Why this priority**: Uncoordinated warnings obscure the experience and make the underlying failure harder to understand.

**Independent Test**: Use controlled time to report simultaneous failures, retries, recovery, and flapping. Count presentations and verify their content and eligibility at each boundary.

**Acceptance Scenarios**:

1. **Given** three eligible failures begin at 0, 0.4, and 1.2 seconds, **When** the fixed two-second collection window ends, **Then** exactly one error summarizes the affected capabilities without request counts or raw errors.
2. **Given** an incident has been announced, **When** retries report it at 4, 8, and 20 seconds or the visitor dismisses its notification, **Then** no duplicate announcement occurs while the incident remains unresolved.
3. **Given** established live delivery is reconnecting, **When** it recovers within 10 continuous visible seconds, **Then** no toast appears; if it remains interrupted beyond that grace, one warning is eligible, and terminal closure permits at most one error escalation.
4. **Given** a failure is waiting to be shown, **When** the affected capability recovers before presentation, **Then** its pending notification is cancelled and recovery is silent.
5. **Given** an announced capability recovers, **When** it fails again before 30 continuous healthy seconds, **Then** it remains suppressed; a new failure after that interval may begin a new incident.

---

### User Story 3 - Keep control of audio and preferences (Priority: P2)

As a visitor, I can retry failed audio enablement, continue without audio, and trust that controls describe the settings actually applied.

**Why this priority**: Recovery instructions must work, especially when audio cannot start or stop, and persistence failure must not masquerade as successful application.

**Independent Test**: Deny an audio enable attempt, retry with a real user gesture, block saved preferences, and fail selected effects. Verify local retry, confirmed control state, and no contradictory notifications.

**Acceptance Scenarios**:

1. **Given** audio enablement fails or remains suspended, **When** the attempt finishes, **Then** the unlock overlay remains with an inline error and a working gesture retry, plus a continue-without-audio option; no duplicate toast appears.
2. **Given** a previously enabled audio session cannot resume automatically, **When** a verified user gesture can recover it, **Then** one warning may point to the audio button only if that button actually performs the resume action.
3. **Given** storage is blocked on a first visit, **When** defaults are applied successfully in memory, **Then** startup stays silent; after an explicit successfully applied preference change cannot be saved, one warning explains that it applies to this session.
4. **Given** an explicit setting cannot be applied, **When** the failure is confirmed, **Then** controls revert to the confirmed state and one useful effect warning appears instead of an additional persistence warning.
5. **Given** a visitor requests mute, **When** the normal stop fails, **Then** a master mute is attempted first; if audio still cannot be stopped, one error tells the visitor to close the tab to stop playback.

---

### User Story 4 - Receive only current and useful feedback (Priority: P2)

As a visitor, returning to the page or changing cities never replays obsolete problems, and failures already explained where I am working stay local.

**Why this priority**: Old or duplicated messages incorrectly describe the current experience.

**Independent Test**: Hide and restore the page, pause delivery, change operation/city, open Stats, and delay the notification host. Complete older work with failures and verify that only still-current eligible incidents can appear.

**Acceptance Scenarios**:

1. **Given** the page is hidden or delivery intentionally paused, **When** background failures or reconnects occur, **Then** no notification appears and paused delivery does not resume solely because transport reconnects.
2. **Given** the visitor returns to the foreground, **When** intended delivery or audio is being restored, **Then** current state is reassessed with 10 seconds for recovery unless terminal failure is already confirmed; historical bursts are never replayed.
3. **Given** a city, route selection, or operation changes, **When** an older operation fails, **Then** it cannot notify the new scope. Leaving/disposal invalidates its callbacks and timers.
4. **Given** three Stats reads fail or an accepted snapshot becomes stale, **When** Stats displays its error/retry or stale state, **Then** zero global toasts appear and its existing retry, cancellation, and retained-data behavior is preserved.
5. **Given** the notification host becomes ready after startup, **When** earlier failures have already recovered, **Then** none are replayed; unresolved current incidents remain eligible.

---

### User Story 5 - Read and dismiss accessible feedback (Priority: P2)

As a visitor using a small screen, keyboard, or assistive technology, I can read a warning, reach essential controls, and dismiss it without losing focus.

**Why this priority**: A notification must not prevent the visitor from recovering or interfere with the audio and map controls.

**Independent Test**: Present warnings and errors in both themes on desktop and mobile with the audio overlay, Stats dialog, city menu, and settings blade open. Check announcement count, keyboard dismissal, focus, reading time, and control access.

**Acceptance Scenarios**:

1. **Given** a notification appears, **When** it is read with assistive technology, **Then** its plain-text severity and message are announced once without sound or automatic focus movement.
2. **Given** a narrow screen or open overlay, **When** feedback is eligible, **Then** it is readable, respects safe areas and reduced motion, and does not cover essential navigation/map/audio controls.
3. **Given** a modal prevents access to the global notification area, **When** failure feedback is needed, **Then** it is provided locally or deferred until the modal closes and revalidated before presentation; there is still only one global notification host.

### Edge Cases

- An offline event alone, an online event, an unrelated successful request, a dismissed notification, or elapsed display time proves neither capability failure nor recovery.
- A healthy quiet feed, empty replay, no moving vehicles/crossings, stale individual records, missing optional metadata, route eviction, and initial audio lock remain silent.
- Caller cancellation from navigation, selection, intentional suspension, or teardown remains silent; an independent operation deadline expiring is a failure.
- An invalid route response and map consumers observing the resulting empty cache share the route incident; a successful response alone is insufficient evidence of readiness.
- Individual tile, glyph, image, sample, note, record, or frame failures do not each create an incident.
- A common cause is used only when confirmed; unrelated failures are summarized without claiming a network outage.
- A parent incident recovering does not automatically reannounce an already-explained child; a distinct previously uncommunicated actionable impact may be reconsidered.
- Slow map startup, fallback timers, swallowed calls, persisted values, or a returned connection attempt cannot be treated as confirmed success.
- An unusable renderer/startup shell needs persistent fallback where rendering permits; failure to show a notification cannot recursively create another notification.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST produce zero toasts during healthy startup, audio enablement, route selection, theme/preference changes, city switching, intentional mute/backgrounding, Stats open/refresh/close, and successful recovery.
- **FR-002**: An eligible notification MUST concern a current city/operation, have actual failure evidence and meaningful user impact, lack adequate visible local explanation, and lack an existing incident already explaining that impact. Errors denote a requested core capability or operation that cannot run; warnings denote temporary interruption, useful partial operation, or session-only preference persistence.
- **FR-003**: The system MUST implement the complete production decision inventory below. Conditionally eligible cases MUST remain silent until their listed prerequisites establish actual impact. The linked design source's decisions, exclusions, and implementation prerequisites remain governing input to subsequent planning; this specification does not authorize arbitrary exception-to-toast coverage.
- **FR-004**: Live readiness MUST require both established transport and successful current-city subscription when delivery is desired. Initial failure, missing/invalid connection configuration, subscription failure, and version rejection MUST never report connected success. Reconnection MUST preserve intentional pause and require confirmed city rejoin before recovery. Replaced connections and obsolete callbacks MUST not affect current readiness.
- **FR-005**: Route readiness MUST distinguish pending, usable success, unavailable/malformed/empty completion, and caller cancellation; one current-city owner MUST explain route-load failure. Current-city usable cached data MAY satisfy readiness. Reload is the initial retry action; no in-place route Retry action may be promised without a working repeatable load.
- **FR-006**: Audio enablement MUST have one confirmed outcome per user attempt, keep the overlay until success or explicit continuation without audio, and support a fresh native gesture retry. Requested audio failures MUST be observable beyond the initial call, while muted/pre-unlock speculative failures MUST remain silent until current impact is established.
- **FR-007**: Map readiness and style restoration MUST reflect confirmed outcomes. Terminal initialization failure MUST have a persistent map fallback. A 20-second visible-time loading watchdog MAY offer local reload but MUST NOT infer failure from elapsed time alone.
- **FR-008**: Preference application and saving MUST have distinct outcomes. Before session-only persistence warnings are enabled, all relevant controls MUST share a working in-memory preference state. Failed changes MUST revert to or visibly identify confirmed applied state; one click MUST NOT create both persistence and effect notifications.
- **FR-009**: The system MUST track incidents with fixed issue codes, affected capability, severity, page/city scope, relevant operation generation, active/resolved state, and eligibility time. Current records MUST be bounded to one per known code/current scope; exception text, addresses, vehicle/route identifiers, and timestamps MUST NOT create unbounded identities or a chronological notification queue.
- **FR-010**: The first eligible issue MUST start a fixed two-second burst window that later reports do not extend. At its end, still-current eligible failures MUST form one presentation at the highest applicable severity. Repeated reports MUST NOT restart eligibility, grace, or display timers.
- **FR-011**: Same-capability failures MUST use one capability-specific message. Confirmed common causes MAY replace child messages while retaining suppression. Unrelated issues MUST name at most two capabilities, using a general parts-of-the-app message for more, without numeric request counts, exception lists, or an invented common cause. Inline-owned issues MUST NOT inflate the summary.
- **FR-012**: Every included or suppressed incident MUST be marked communicated and stay silent for its unresolved lifetime, including after dismissal. A distinct actionable child may be reconsidered after a parent resolves only if that impact was not already communicated. A newly eligible error MAY replace an active warning once, including terminal live failure escalating a reconnect warning; same-severity updates MUST NOT repeatedly announce an incident.
- **FR-013**: Established live interruption MUST receive a warning only after 10 continuous visible seconds with delivery desired; confirmed terminal failure is eligible without that grace. Automatic presentations MUST be at least 10 seconds apart, except a confirmed inability to stop audio MAY bypass spacing. During spacing the system MUST retain current incident state and revalidate later, rather than queue a stale presentation.
- **FR-014**: There MUST be at most one visible toast and no hidden presentation backlog. Warnings MUST allow 8 seconds and errors 12 seconds of reading time with a close control. If immediate replacement/removal is unsupported, the current toast MAY finish before the next freshly revalidated presentation; stacking is forbidden.
- **FR-015**: Verified capability recovery MUST cancel pending presentations, remove obsolete visible feedback when supported, and clear persistent failure state silently. The affected capability MUST remain suppressed until 30 continuous healthy seconds have elapsed; flapping before then MUST NOT reannounce. Only evidence for that capability counts as recovery.
- **FR-016**: Hidden pages MUST update incident state without showing notifications. Foreground return MUST reassess current failures and allow 10 seconds for intended delivery/audio recovery unless terminal evidence already exists. Intentional hidden/muted pause, shutdown, cancellation, and obsolete generations MUST never notify. Full city reload MUST start a fresh scope; disposal MUST invalidate pending work.
- **FR-017**: Startup incident state MUST survive notification-host registration, then be revalidated. The host MUST show only still-current unresolved eligible failures. Presentation failure MUST be logged once and MUST NOT recursively notify or destroy usable inline feedback.
- **FR-018**: Copy MUST state the affected capability and a truthful available recovery action using existing resource-backed plain-text severity labels. It MUST NOT expose raw internal errors, response bodies, server addresses, credentials, deployment instructions, or sign-in advice for this public app. Browser offline state MAY qualify copy only alongside observed eligible network failure. No new blanket automatic request retries are authorized; any later retry MUST respect server retry instructions.
- **FR-019**: Ongoing core failure MUST retain persistent local status after a toast disappears. Existing Stats error/stale/retry presentation and accessible chart fallbacks MUST remain locally owned without global duplication.
- **FR-020**: One global notification host MUST support one assistive announcement per presentation, keyboard dismissal, sufficient touch targets, readable contrast in both themes, reduced motion, safe-area insets, and no automatic focus stealing or notification sounds. Preferred placement is top-center clear of navigation and map/audio controls. Modal interference MUST use local feedback or defer and revalidate, without adding a second host.
- **FR-021**: Delivery MUST begin with trustworthy outcomes and first coverage C1-C6, R1/R2, confirmed M1, and inline A2/requested A4; muted/background behavior MUST be verified before broader conditional coverage. All other inventory rows remain part of this feature's explicitly gated scope, not permission to enable unsupported warnings.
- **FR-022**: Implementation MUST use TDD for meaningful automated policy/readiness coverage: express the expected observable behavior in a failing test before implementing it, then make it pass and simplify while keeping it passing. Validation MUST use deterministic time and bounded failure simulations plus browser failure-injection checks for gesture trust, asynchronous outcomes, overlays, visibility, and assistive behavior. Coverage MUST exercise this specification and every source acceptance scenario; tests MUST verify visitor policy and readiness rather than merely mirror individual error handlers. Subsequent planning MUST enrich the testing/TDD approach before task generation; this specification does not execute that planning or any tests.

### Production Decision Inventory

IDs preserve traceability to the [governing design inventory](../../docs/TOAST_NOTIFICATIONS_DESIGN_PLAN.md). Wording below expresses the required user meaning; final resource copy is reviewed with actual failure injection.

| Source IDs | Required outcome and eligibility |
| --- | --- |
| C1, C2 | Error for failed initial connection/configuration or city subscription: live transit could not connect; reload to try again. Same live-delivery incident. |
| C3 | Warning only after the reconnect grace: live updates are interrupted and trying to reconnect. Recovery during grace is silent. |
| C4 | Unexpected terminal closure: error that live updates stopped; reload to reconnect. At most one escalation of C3. |
| C5 | Failed desired resume or rejoin: error that updates could not resume; reload. Mere disconnected state follows C3/C4, not a fabricated join failure. |
| C6 | Version rejection: connection error advising reload, then trying later if it continues. Same live incident. |
| C7 | No standalone offline toast. Observed eligible network failure may say the app appears offline and to check the connection; retain its impact severity. Online only triggers reassessment. |
| C8, C9, C10 | Silent intentional pause/leave/shutdown/disposal, hidden paused reconnect, quiet or empty feed/replay, stale individual vehicles, or eviction. No upstream outage inferred from silence. |
| C11 | Error only on confirmed global live-processing failure with valid delivery expected; deduplicate against a connection/routes/map explanation. Isolated bad records remain silent. |
| R1 | One route error for failed/timed-out/unusable load with no usable current-city cache: routes could not load; reload. Internal failure classifications do not change safe copy. |
| R2 | One route error for a completed supported-city response containing zero usable geometries: route data unavailable; try another city or later. Initial empty cache alone is insufficient. |
| R3 | Partial invalid routes are silent by default. Warning only for an explicitly selected unusable route with no local explanation; prefer local selection feedback where practical. |
| R4 | Secondary observations of R1/R2 are silent; successful transport does not prove usable route data. |
| R5, R6 | Stats initial errors remain inline; refresh failure retains stale labeling and local warning/retry state. Preserve safe failure mapping and server retry instructions. |
| R7, R8, R9 | Stats disabled/empty/partial/pending/fallback/missing-count states stay inline; cancellations/obsolete results are silent. Chart failure uses accessible local table/text fallback, or the Stats inline error if none remains. Time formatting fallback is silent. |
| R10 | No global request hooks for unused or hypothetical callers; future operations need their own contextual decision. |
| M1 | Confirmed fatal initialization: error plus persistent map fallback and reload; unsupported browser capability instead suggests another browser. |
| M2, M3 | Pending readiness uses local loading feedback, never a timer-only failure toast. Individual tile/glyph/image errors, transient style sources, and isolated visual effects remain silent. |
| M4 | Failed explicit style/theme change with verified previous-map restoration: one warning that the previous style remains. An unusable map uses M1 instead. |
| M5 | Persistent rendering failure only after readiness and terminal evidence or two failed valid batches spanning at least 10 visible seconds: error for unusable transit display; warning for only a secondary layer. Recovery requires verified successful rendering; no per-frame reports. |
| M6 | Hover/background/optional effects remain silent. Failed explicit display setting without local feedback gets one settings warning with confirmed control state restored. |
| M7 | Pending geometry, bounded pre-ready eviction, and absent route/motion timing remain silent; a confirmed root failure is owned by R1/M1. |
| M8, M9 | Failed explicit city navigation while the old page remains usable gets one error to reload and retry. Normal full reload, unknown-city default fallback, and existing NotFound feedback create no toast or false claim about the requested city. |
| A1 | Autoplay lock, no audio request, mute, filtered notes, normal loading, and no crossings remain silent. Silence is not a playback failure detector. |
| A2 | Failed unlock stays inline on the overlay with retry and continuation without audio. No duplicate toast, premature dismissal, or false unlock success. |
| A3 | Failed resume after prior unlock gets a warning only when the offered audio-button gesture really resumes playback. |
| A4 | Required playback-support failure after unlock/request gets one audio error with reload. Speculative pre-request failures are silent and later reassessed. |
| A5 | Required sounds still failing after 10 unresolved seconds or terminal failure: one audio warning if some required voices work, error if none work. No per-note/voice/sample messages; recovery before eligibility is silent. |
| A6 | Failed explicitly selected background sound while main audio works: warning and confirmed applied/reverted control state. Optional warming/percussion without requested impact stays silent. |
| A7 | Failed stop attempts master mute first, then error with tab-close action if needed; failed filter/change gets a warning and truthful controls. Saved preferences do not prove playback changed. |
| A8 | Individual notes, duration fallback, inactive cleanup, optional warmup, and developer toggles stay silent; confirmed global playback failure belongs to A4/A5. |
| P1, P4 | Absent/version-changed/corrupt settings recovered with defaults and initial default-write failure stay silent with working in-memory defaults. |
| P2 | Failed persistence after an explicit change successfully applies in shared session state: one warning per storage incident that changes apply this session but could not be saved. |
| P3 | Failed application even in memory: one warning with confirmed control state restored, or A7 error for failure to stop audio. No duplicate persistence message. |

### Exclusions and Scope Boundaries

- Analytics, viewport measurement, outside-click registration, and visibility listeners do not notify directly. A later concrete failed user operation may be explained by its owner.
- Teardown/unsubscribe/disposal errors, cancelled timers, and intentional connection stops remain silent, including when leaving the page.
- Route labels/colors/categories, missing blurbs/optional metadata, empty selection, and successful defaults/fallbacks remain silent.
- Developer-only audio tools, legacy or unused playback helpers, and debug controls produce no production toasts.
- Pre-layout startup failures and an unusable renderer retain persistent fallback where possible; they do not depend on a toast. A broader renderer error boundary is separate work.
- Worker/feed, database/retention, telemetry, cache, and persistence logs are not broadcast to visitors; only observed current client-capability impact qualifies through the inventory.
- No stale-feed detection, guessed silence thresholds, notification inbox/history, server notification endpoint, new notification dependency, blanket request-to-toast hook, blanket retries, or success notifications are included. A trustworthy upstream-health contract is separate work.

### Key Entities *(include if feature involves data)*

- **Capability**: A visitor-visible function: live delivery, routes, map, audio, settings, or city navigation. Its actual requested/healthy state determines impact and recovery.
- **Incident**: A bounded current-scope failure with fixed code, capability, severity, active/resolved state, eligibility time, and communicated status. Multiple low-level reports may describe one incident.
- **Scope**: The current page/city and relevant user-operation generation. Replaced or disposed scopes cannot present feedback.
- **Presentation candidate**: A freshly evaluated summary of eligible incidents, never a durable queued toast. It has safe copy, severity, and reading duration; included children are suppressed together.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every healthy-session and silent/local-only scenario in this specification and the governing design acceptance table produces exactly zero global toasts.
- **SC-002**: Three eligible failures inside the fixed two-second window produce exactly one useful summary; repeated reports and dismissal produce zero further same-incident announcements before verified recovery plus 30 healthy seconds.
- **SC-003**: Reconnection recovered within 10 continuous visible seconds produces zero toasts; an interruption exceeding that grace produces at most one warning and at most one terminal error escalation, with no simultaneous visible toasts.
- **SC-004**: Every tested initial-connection, city-subscription, route-validation, map-readiness, and audio-unlock failure preserves truthful readiness and a working stated recovery action or persistent fallback; no failed outcome is labeled successful.
- **SC-005**: Every tested hidden, cancelled, disposed, obsolete-scope, and recovered-before-presentation outcome produces zero stale presentations, including after foreground return or late host registration.
- **SC-006**: In desktop/mobile checks across both themes and each production overlay, every presented notification is readable, keyboard-dismissible, announced once, and leaves essential controls reachable without focus theft; warnings and errors allow their specified 8/12-second reading periods.
- **SC-007**: Failed explicit settings tests yield no more than one relevant notification per action/incident, and session-only save warnings occur only when the visitor's changes actually remain applied in the session.

## Assumptions

- The [design plan](../../docs/TOAST_NOTIFICATIONS_DESIGN_PLAN.md) is the governing end-to-end source; its audited production inventory and implementation prerequisites are preserved rather than extended to hypothetical future callers. Existing notification presentation and localization resources are reused.
- Proposed timing, reading duration, placement, and copy defaults are adopted as testable initial behavior, not measured platform guarantees. Failure injection and accessibility review must validate them before release.
- First coverage is C1-C6, R1/R2, confirmed M1, and A2/A4; conditional coverage remains disabled until its reliable signals and recovery actions exist. In particular R3/M5/A5 cannot substitute guessed quiet-feed thresholds for evidence.
- Trustworthy connection/subscription outcomes, route validation and caller-cancellation distinction, retryable confirmed unlock, observable asynchronous map/audio outcomes, and shared in-memory settings fallback are prerequisites for corresponding notifications. Logging, timers, completed calls, or persisted settings alone are insufficient.
- Planning must preserve existing architectural ownership and browser scope: low-level transport exposes outcomes without presentation dependency, route readiness has one owner, capability boundaries report coarse outcomes, and only the application coordinator requests toast presentation. Preserve one shared audio module and existing intentional pause semantics; do not introduce a bus, history store, or new endpoint.
- Before implementation, the existing pinned notification renderer must be verified for a single visible presentation, duration, dismissal, replacement/removal, and accessibility. Unsupported replacement uses the FR-014 finish-and-revalidate alternative; assumed capabilities from another library are unacceptable.
- Browser validation is necessary for real gesture trust, asynchronous callbacks, mobile safe areas, overlays, and screen-reader announcements; the design inventory is source-based and has not itself proved those behaviors through a live failure-injection run.
- This feature changes the current production browser experience only. Server operations and new feed-health contracts remain outside scope. No implementation, deployment, or commit is performed by this specification workflow.
