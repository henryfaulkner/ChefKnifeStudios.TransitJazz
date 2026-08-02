# Feature Specification: Egress Reduction at Current Scale

**Feature Branch**: `051-egress-reduction`
**Created**: 2026-07-25
**Status**: Draft
**Input**: User description: "docs/EGRESS_REDUCTION_SMALL_SCALE.md — reduce outbound data-transfer cost at 500–2,000 concurrent users via measurement-first observability, cheaper app startup, background-tab pause, and a single coordinated slimming of live vehicle updates"

## Clarifications

### Session 2026-07-25

- Q: Should hiding the tab pause live-update delivery unconditionally, or only when audio is also muted? → A: Confirmed by the product owner: pause a session only when the tab is hidden AND audio is muted — ambient background listening is supported behavior, so audio-unmuted sessions keep streaming while hidden. FR-007/FR-008 and SC-003 stand as written.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Operator can see what the system actually transfers (Priority: P1)

As the operator, I can see the real, measured size of every live-update publish, per city, recorded durably over time — and my operational logs actually land somewhere queryable — so that every optimization in this feature (and any future one) is judged against observed numbers instead of arithmetic estimates. The hosting plan is also moved off a tier whose monthly bandwidth cap would silently stop serving the app as usage grows toward the target range.

**Why this priority**: Every other story's value claim is currently an estimate derived from the declared data contract, not a wire capture. Measuring first validates or corrects all of them, and identifies whether one very large city dominates cost the way the arithmetic suggests. Logs currently go nowhere, so there is no way to verify any change worked. The bandwidth cap is an availability risk, not just a cost one: hitting it stops the app from serving at all.

**Independent Test**: Deploy only this story. Query the telemetry store and confirm a per-city payload-size measurement exists for each publish cycle; run a log query and confirm application logs are retrievable; confirm the hosting plan no longer carries the capped bandwidth allowance.

**Acceptance Scenarios**:

1. **Given** live cities are publishing vehicle updates, **When** a publish cycle completes, **Then** the actual outbound payload size for that city and cycle is recorded in the durable telemetry store alongside the existing per-cycle measurements.
2. **Given** several days of recorded measurements, **When** the operator queries them, **Then** per-city totals can be compared to identify which cities dominate outbound transfer.
3. **Given** the application emits operational logs, **When** the operator queries the log store, **Then** recent application logs are present and searchable.
4. **Given** total monthly app-download bandwidth exceeds the old plan's capped allowance, **When** users load the app, **Then** the app continues to be served without interruption.

---

### User Story 2 - App startup costs a fraction of today's transfer (Priority: P2)

As a visitor, the route-map data my browser downloads on startup arrives compressed, and when I return later, unchanged data is not re-downloaded at all — so startup is faster for me and dramatically cheaper for the operator.

**Why this priority**: The route-geometry download is the single largest per-session HTTP transfer, every client fetches it on startup, and coordinate-dense data compresses extremely well (70–85%+ typical). The underlying data only changes once per day, yet today it is rebuilt and re-sent in full on every request. This is the best savings-per-effort item that carries no behavioral risk.

**Independent Test**: Load the app and confirm the route-data response is compressed and substantially smaller than today's; reload and confirm the unchanged data is revalidated without being re-transferred; confirm the rendered map is identical.

**Acceptance Scenarios**:

1. **Given** a browser that accepts compressed responses, **When** it requests route geometry or the route catalog, **Then** the response is delivered compressed, with a transfer size reduced by at least 70% versus the uncompressed size.
2. **Given** a returning visitor whose cached copy matches the current data, **When** their browser revalidates, **Then** the server confirms freshness without re-sending the body.
3. **Given** the daily data refresh has produced new route data, **When** a returning visitor revalidates, **Then** they receive the new data in full.
4. **Given** many concurrent startup requests, **When** route data is served, **Then** the served bytes come from a precomputed representation rather than being rebuilt per request.

---

### User Story 3 - Backgrounded, silent tabs stop consuming data (Priority: P3)

As a visitor who has muted the audio and switched to another tab, my session stops receiving live vehicle updates entirely; when I come back to the tab, the map catches up immediately from a fresh snapshot with vehicles in their correct current positions. If my audio is playing, backgrounding the tab changes nothing — the ambient soundscape keeps going.

**Why this priority**: Outbound transfer is linear in delivery cadence, and long-lived unattended sessions cost exactly as much as engaged ones today. Pausing hidden-and-muted sessions is estimated as the largest single win in the package for half a day of effort — but it depends on the audio-mute condition to avoid breaking ambient background listening, which is a core product experience.

**Independent Test**: Mute audio, hide the tab, and observe that live-update traffic to that session stops; restore the tab and observe an immediate correct catch-up. Unmute audio, hide the tab, and observe updates (and sound) continue.

**Acceptance Scenarios**:

1. **Given** a session with audio muted, **When** the tab becomes hidden, **Then** the session stops receiving live vehicle updates and contributes zero live-update transfer while hidden.
2. **Given** a hidden, paused session, **When** the tab becomes visible again, **Then** the session receives a current full snapshot and the map shows vehicles at their present positions without replaying stale motion.
3. **Given** a session with audio unmuted, **When** the tab becomes hidden, **Then** live updates and the soundscape continue uninterrupted.
4. **Given** a paused session whose vehicles moved while hidden, **When** the session resumes, **Then** no incorrect animation artifacts (e.g., vehicles sweeping across the map from stale origins) are shown.

---

### User Story 4 - Live vehicle updates are half their current size (Priority: P4)

As the operator, each vehicle's live update carries only what the receiving client actually needs — position at map-appropriate precision, no repeat of data the client already holds — cutting the recurring per-vehicle payload roughly in half with zero visible change to the map or the soundscape.

**Why this priority**: This is the recurring, every-ten-seconds cost and the largest structural reduction, but it changes the delivery format shared by three separately-deployed components, so it ships last, as one single coordinated revision, after measurement (Story 1) can confirm its effect.

**Independent Test**: Compare the recorded per-city payload sizes (Story 1's measurement) before and after the change and confirm a ≥35% per-vehicle reduction (38.7% measured pre-implementation); visually confirm map animation, vehicle categorization, and audio triggering are unchanged.

**Acceptance Scenarios**:

1. **Given** the slimmed update format is live end-to-end, **When** a publish cycle is measured, **Then** per-vehicle payload size is reduced by at least 35% versus the pre-change measured baseline (measured: 38.7% — see SC-004).
2. **Given** positions are transmitted at reduced encoding size, **When** vehicles render on the map, **Then** displayed positions are accurate to roughly one meter — indistinguishable from today at any usable zoom.
3. **Given** a continuously connected client, **When** it receives a vehicle it has seen before, **Then** the update omits the previous-position data and the client animates from its own retained last-known position.
4. **Given** a newly appearing vehicle, or a client that just joined or resumed, **When** the first update or snapshot arrives, **Then** it contains everything needed to place and animate the vehicle correctly with no motion artifacts.
5. **Given** a vehicle whose route is present in the client's route catalog, **When** its update arrives, **Then** the update omits the vehicle-type label and the client resolves it from the catalog; **Given** a vehicle whose route failed to match the catalog, **Then** the update still carries the explicit "unknown" label so the data-quality signal is preserved.
6. **Given** an out-of-date client (e.g., cached app version) meets the new format, **When** it attempts to connect, **Then** it fails cleanly at connection time rather than misrendering data.
7. **Given** a vehicle update with the label omitted arrives before the route catalog has loaded, **When** it renders, **Then** it shows as "unknown" rather than a guessed category, and is re-resolved to its true category once the catalog loads.

---

### Edge Cases

- A vehicle appears for the first time mid-session: its first update must be self-contained (no reliance on client-retained history).
- A client joins or resumes while some vehicles have been stationary for a long time: the join-time snapshot must still include those vehicles, self-contained, so nothing vanishes or animates incorrectly.
- A stationary-or-stale vehicle in the snapshot must not be presented as "moving" — the existing staleness signal must survive the format change (a previously fixed regression).
- Vehicle updates arrive before the route catalog finishes loading (an existing, already-observed startup race): category-omitted vehicles must resolve to "unknown" in the interim and be corrected when the catalog lands — never defaulted to a concrete category.
- A vehicle leaves the rendered set and later reappears with no previous-position data: its stale retained position must have been discarded, so it snaps into place rather than animating from an obsolete origin.
- The tab visibility change fires while audio state is mid-transition (user muted moments before hiding): the pause decision must reflect the current mute state at hide time.
- A user rapidly toggles tab visibility: repeated pause/resume cycles must not leak subscriptions, duplicate snapshots, or double-deliver updates.
- The browser's cached copy of route data is stale after the daily refresh: revalidation must detect the change and deliver fresh data, never serve a false "unchanged".
- A visitor's browser does not accept compressed responses: content must still be served uncompressed and correct.
- During the coordinated format rollout, an old cached client connects to the new server: the connection must be rejected at negotiation rather than silently degrading (this is the current behavior and must be preserved).
- A separately-branded deployment ships from its own branch: the format revision must land there too, or that deployment breaks.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST record the actual outbound payload size of every per-city live-update publish cycle in the existing durable telemetry store (not one-off logs), attributable by city and time, so per-city cost share can be computed over any date range.
- **FR-002**: Application operational logs MUST be delivered to a queryable log store; today they are discarded because the wiring is incomplete.
- **FR-003**: The app-hosting plan MUST NOT impose a monthly bandwidth cap that the target usage range (500–2,000 concurrent users) can exhaust; serving the app must not stop mid-month due to a plan limit.
- **FR-004**: Route-geometry and route-catalog responses MUST be served compressed to clients that accept compression, including over secure connections, with no change to response content.
- **FR-005**: Route-geometry and route-catalog responses MUST be served from a representation precomputed when the daily data refresh completes, rather than rebuilt per request.
- **FR-006**: Route-geometry and route-catalog responses MUST support client revalidation such that an unchanged body is confirmed fresh without being re-transferred, and a changed body (after the daily refresh) is delivered in full.
- **FR-007**: When a session's tab is hidden AND its audio is muted, the session MUST stop receiving live vehicle updates and contribute zero live-update transfer while in that state.
- **FR-008**: When a session's audio is unmuted, hiding the tab MUST NOT interrupt live updates or the soundscape — ambient background listening is preserved behavior.
- **FR-009**: When a paused session's tab becomes visible again, the session MUST receive a current full snapshot and render vehicles at present positions, without replaying motion that occurred while hidden.
- **FR-010**: Vehicle positions MUST be transmitted at a precision of roughly one meter (the precision already applied before transmission today) using an encoding sized to that precision, not to full floating-point width.
- **FR-011**: Updates for vehicles the client has already observed MUST omit previous-position data; the client MUST animate from its own retained last-known position for that vehicle.
- **FR-012**: First-observation updates and join/resume snapshots MUST remain self-contained: they include everything required to place and animate a vehicle correctly, including its staleness state, with no dependence on client-retained history.
- **FR-013**: Updates MUST omit the per-vehicle category label when the client can resolve it from its route catalog, EXCEPT when the category is the explicit "unknown" data-quality signal, which MUST always be transmitted.
- **FR-013a**: The client's category resolution MUST NOT invent a category. When a vehicle's update omits the category and its route is absent from the client's route catalog — including before the catalog has finished loading — the vehicle MUST be treated as "unknown", never defaulted to a concrete category such as "bus". A vehicle resolved as "unknown" solely because the catalog had not yet loaded MUST be re-resolved once the catalog arrives, so the startup race does not permanently mislabel it.
- **FR-014**: All live-update format changes in this feature (FR-010, FR-011, FR-013) MUST ship together as one coordinated format revision — not as separate revisions — and the revision MUST land in every deployment lane that carries the format, including any separately-branded deployment branch.
- **FR-015**: A client running the old format against a new server (or vice versa) MUST fail cleanly at connection time; it must never connect and misinterpret data.
- **FR-016**: All changes in this feature MUST produce no visible difference in map animation, vehicle categorization, or audio behavior for an active, foregrounded session.

### Key Entities

- **Vehicle Update Record**: The per-vehicle unit of live data delivered on every publish cycle — identity, route membership, current position, motion timing, speed/heading, staleness, and category. This feature slims it: position at map precision, previous-position omitted when the client holds it, category omitted when derivable.
- **City Update Batch**: The per-city collection of vehicle update records published each cycle; the unit whose outbound size is measured and recorded (FR-001).
- **Join/Resume Snapshot**: The self-contained current-state batch delivered to a client that connects or resumes; unlike steady-state updates, it retains full previous/current position pairs and staleness state.
- **Route Catalog**: The per-city set of route definitions and geometry downloaded at startup; refreshed daily on the server; the reference the client uses to resolve a vehicle's category; the subject of compression, precomputation, and revalidation.
- **Telemetry Cycle Record**: The existing per-city, per-cycle measurement row, extended with the measured outbound payload size.
- **Session Attention State**: The combination of tab visibility and audio-mute state that determines whether a session receives live updates.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Within one day of deployment, actual per-city outbound payload sizes are recorded and queryable, and the operator can state each city's share of total live-update transfer over any date range — replacing estimate-based figures.
- **SC-002**: Route-data startup transfer size is reduced by at least 70% for clients that accept compression, and a repeat visit with unchanged data transfers effectively zero route-data bytes.
- **SC-003**: A hidden tab with muted audio generates zero live-update transfer for the entire time it is hidden, and catches up correctly within one publish interval of becoming visible.
- **SC-004**: Measured per-vehicle live-update payload is reduced by at least **35%** from the pre-change measured baseline. **Amended 2026-08-01** (was "at least 40%, target 45–50%"): the Phase 0 baseline measured production at ~68 B/vehicle, and an empirical MessagePack sizing of the v2 shape put the steady-state reduction at **38.7%** (69.8 B → 42.8 B). The original 40% was an estimate-era target; the residual is dominated by the `VehicleId`/`RouteJoinKey` strings that v2 deliberately does not touch, so 40% is unreachable without string slimming (out of scope). Threshold set to 35% as a regression floor beneath the measured 38.7%. Evidence: `results.md`.
- **SC-005**: Total monthly outbound data transfer at equivalent traffic is reduced by 60–75% versus the measured pre-feature baseline.
- **SC-006**: The app remains continuously served throughout the month at up to 2,000 concurrent users — no plan-cap service interruption.
- **SC-007**: Zero user-visible regressions: an active foreground session shows identical map animation, vehicle categories, and audio behavior before and after each phase ships.

## Out of Scope

Recorded explicitly so these are decisions, not oversights. All come from the companion 100k-scale assessment and are wrong at 500–2,000 users, or are deferred pending measurement:

- **Viewport-scoped delivery** (sending each client only vehicles in view) — weeks of effort for modest savings at this scale; reconsider above ~5,000 users.
- **Managed real-time messaging service** — a monthly floor cost for capacity this scale will not touch.
- **Splitting the data worker into its own deployable** — only matters beyond one replica; the system stays at one.
- **Cache locking granularity, multi-region, zone redundancy** — not reachable or not egress problems at this scale.
- **Delta/differential encoding of updates** — the slimming in Story 4 captures most of the benefit at a fraction of the complexity.
- **Idle-session cadence downgrade** (slower updates after inactivity) — deferred until the shipped package is measured; may be unnecessary.
- **Suppressing unchanged/stale vehicles from steady-state updates** — deferred; largest behavioral blast radius in the source analysis (snapshot correctness hazards), revisit only after the format change is stable and measured.

## Assumptions

- **Hidden-tab pause is gated on audio mute** (FR-007/FR-008). The source analysis flagged "does audio continue in a hidden tab?" as an open product question; it is now resolved (see Clarifications, Session 2026-07-25): background listening is supported behavior, so only sessions that are both hidden AND muted pause. This is a settled product decision, not an assumption.
- **Success is measured in relative reduction, not dollars.** The source document's dollar figures are estimates from list prices that drift; SC-002/SC-004/SC-005 are defined against measured baselines captured by Story 1, which is why Story 1 ships first.
- **Roughly one-meter position precision is sufficient** for vehicle dots at any usable map zoom; this precision is already applied to values before transmission today, so no displayed information is lost.
- **The current single-replica, co-hosted architecture is retained.** Nothing in this feature changes deployment topology.
- **The existing join/resume snapshot mechanism is reused** for tab-resume catch-up (Story 3) and remains the compatibility path for slimmed updates (Story 4); no new snapshot mechanism is introduced.
- **Ordering**: Story 1 (measure + observability + plan cap) ships before optimization stories; Story 4's format changes ship as one revision, last, so measurement brackets their effect. Story 2 and Story 3 are independent of each other and of Story 4.
- **A separately-branded deployment lane exists** (separate branch); FR-014's obligation to land the format revision there is inherited from the established multi-lane deployment constraint.
