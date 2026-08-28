# Feature Specification: Worker Observability

**Feature Branch**: `053-worker-observability`  
**Created**: 2026-08-22  
**Status**: Draft  
**Input**: User description: "Start creating implementation of `docs/Prometheus_Grafana_strategy.md`"

## Overview

Give the green-field periodic data worker reliable, low-cost observability so an
operator can quickly determine whether it is alive, receiving input, completing useful
work, making acceptable decisions, operating efficiently, and failing. The worker must
be diagnosable from aggregate monitoring views and alerts before an operator needs to
inspect logs. Detailed, entity-level evidence remains in structured logs.

The feature deliberately separates bounded operational signals from detailed forensic
records. It must be safe to run in production, avoid unnecessary public network
surfaces, remain useful during idle periods and failures, and scale without merging
separate worker instances into one apparent source.

## Clarifications

### Session 2026-08-22

- Q: What should constitute city-level coverage? → A: Every city-specific health, input,
  work, quality, and resource signal is segmented by bounded city; city-degradation
  alerts evaluate separately per city.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Establish worker health (Priority: P1)

An operator opens the worker overview and can tell apart a stopped worker, a live but
idle worker, a worker with no input, a worker that is doing work, and a worker that is
failing. For every configured city, they can independently see the same health and data
state. The conclusion does not require reading logs first.

**Why this priority**: Detecting an unavailable or ineffective worker is the essential
operational value. A quiet periodic worker must not be mistaken for a healthy one.

**Independent Test**: Run successful, idle, input-starved, failed, and stopped worker
cycles, including tests where exactly one city is affected, then verify the health view
and its underlying signals identify the correct worker and city state for each condition.

**Acceptance Scenarios**:

1. **Given** a worker that completes an idle cycle, **When** an operator views its
   health, **Then** the worker is shown as alive and idle rather than unavailable.
2. **Given** a worker that has stopped, **When** its expected heartbeat window expires,
   **Then** the operator receives an unavailable-worker alert and can see that the
   liveness signal is missing.
3. **Given** a worker that receives no usable input, **When** it continues cycling,
   **Then** the health view distinguishes input absence from worker failure for the
   affected city without marking healthy cities as degraded.
4. **Given** a cycle that fails, **When** the cycle completes its cleanup path, **Then**
   the failure is visible while the worker's continuing liveness remains visible.
5. **Given** one configured city has stale input, a failed city tick, or degraded work
   output, **When** other cities remain healthy, **Then** the overview identifies the
   affected city and the failed signal without requiring an operator to infer it from a
   worker-wide aggregate.

---

### User Story 2 - Diagnose operational behavior (Priority: P2)

An operator investigating degraded behavior can use the monitoring views to assess work
volume, input freshness, decision quality, cycle duration, allocation pressure, queue
or cache state, and recent errors for each configured city. They can then move to
structured logs only when entity-level context is needed.

**Why this priority**: Once an incident is detected, aggregate evidence must narrow the
cause quickly without exposing sensitive or unbounded detail in monitoring data.

**Independent Test**: Feed representative successful, slow, poor-quality, and
resource-pressured summaries for one city while other cities remain healthy; verify that
each condition is shown for the affected city with a stated next diagnostic action.

**Acceptance Scenarios**:

1. **Given** a cycle that performs work, **When** it finishes, **Then** its work,
   duration, input, quality, resource, and current-state summaries are available for
   inspection.
2. **Given** a cycle with decision values on either side of configured thresholds,
   **When** an operator views decision quality, **Then** they can compare observed
   distributions with the applicable thresholds and see rejected outcomes.
3. **Given** an abnormal aggregate signal, **When** an operator opens its description,
   **Then** it explains the signal, normal behavior, and the next thing to inspect.
4. **Given** a city-attributable signal, **When** an operator compares cities, **Then**
   every configured city is separately identifiable and no entity-level identifier is
   exposed.

---

### User Story 3 - Receive actionable alerts (Priority: P3)

An on-call operator receives an alert for stopped or stalled work, sustained absence of
input, recent failures, slow cycles, or excessive input lag. Where the condition is
city-attributable, the alert identifies the affected city. The alerting path is proven by
a controlled test, not assumed to work because a rule exists.

**Why this priority**: Dashboards are useful only when someone opens them. Alerts turn
the most important failure conditions into timely action.

**Independent Test**: Deliberately create each alert condition, including stopping the
worker completely, and verify the expected severity, alert content, and configured
notification delivery.

**Acceptance Scenarios**:

1. **Given** no current worker heartbeat, **When** three configured cycle intervals
   pass, **Then** a critical stalled-worker alert fires even though no fresh observation
   is available.
2. **Given** one or more recent failed cycles, **When** an operator views alerts,
   **Then** a warning identifies recent cycle errors without waiting for the worker to
   stop.
3. **Given** an intentional alert test, **When** the test condition is met, **Then** the
   designated contact receives the notification and the result is recorded.
4. **Given** degradation in one city only, **When** its city-level alert condition is
   met, **Then** the alert names that city and does not raise equivalent alerts for
   unaffected cities.

---

### User Story 4 - Operate observability safely (Priority: P4)

A deployer can enable production observability without publishing an inbound monitoring
endpoint, exposing credentials, exporting customer or entity identifiers, or sending
local-development telemetry to the production monitoring tenancy.

**Why this priority**: The service's low-cost and low-surface-area operation depends on
safe telemetry handling from the first deployment.

**Independent Test**: Review deployed network exposure, emitted attribute values,
credential locations, and local-development settings; then run a local cycle and verify
it is visible only in the local monitoring environment.

**Acceptance Scenarios**:

1. **Given** production observability is enabled, **When** the worker runs, **Then** it
   sends aggregate telemetry outward without accepting inbound monitoring traffic.
2. **Given** a metric observation, **When** its attributes are inspected, **Then** none
   contain a customer identifier, entity identifier, filename, URL, exception text, or
   free-text value.
3. **Given** a developer runs the local monitoring workflow, **When** the worker emits
   telemetry, **Then** no data is sent to the production monitoring tenancy.

### Edge Cases

- A cycle fails after beginning work: failure is recorded, and the cycle-completion
  heartbeat still proves that the process remains alive.
- The worker is alive but has no work to do: the latest cycle signal advances while the
  latest-work signal does not; this is not reported as a dead worker.
- One configured city's feed, route index, or processing path fails while other cities
  continue: the affected city is shown and alerted independently; a worker-wide healthy
  aggregate must not conceal the city-specific degradation.
- An input timestamp is unavailable or invalid: input freshness is explicitly reported
  as unknown rather than retaining the prior healthy value or treating unknown as no lag.
- The worker stops or telemetry cannot be delivered: the absence of liveness data is
  itself treated as an alerting condition, rather than a healthy empty graph.
- A deployment replaces an instance or the worker later scales out: instances remain
  distinguishable, while overall health and alerts use stable service and environment
  identity rather than a short-lived instance identity.
- A value would create an unbounded dimension: it is retained only in structured logs
  and excluded from aggregate monitoring attributes.
- The worker is disabled for a deployment: no operational measurements are emitted and
  normal worker behavior continues.
- The worker shuts down during an export period: its final completed-cycle information is
  sent before termination whenever the runtime permits it.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide bounded aggregate operational signals that answer
  whether the worker is alive, receiving input, doing work, making acceptable decisions,
  operating efficiently, and failing. Every city-attributable signal MUST be separately
  available for each configured city.
- **FR-002**: The system MUST keep entity identifiers, customer identifiers, detailed
  reasons, correlation identifiers, and other forensic detail in structured logs rather
  than aggregate monitoring attributes.
- **FR-003**: The system MUST record a worker completion heartbeat for every cycle,
  including idle and failed cycles, and record a work timestamp only for a cycle that
  performed work. It MUST also record a completion and health result for every configured
  city in each cycle, including a city whose tick failed.
- **FR-004**: The system MUST expose the configured cycle interval so liveness thresholds
  can be evaluated relative to the worker's actual schedule.
- **FR-005**: The system MUST report input presence, valid-input count, and input
  freshness for every configured city on every cycle. An unavailable input timestamp MUST
  be represented as unknown, not as the previous observation or zero delay.
- **FR-006**: The system MUST report work volume, business outcomes, decision quality,
  decision thresholds, cycle duration, allocation pressure, queue or cache state where
  applicable, and cycle failures from each relevant cycle summary. Every value that can
  be attributed to a city MUST be separately reported for that city.
- **FR-007**: Current-state measurements MUST come from the applicable source of truth
  and MUST NOT be calculated by subtracting historical work totals.
- **FR-008**: Aggregate monitoring values MUST use base units, clear descriptions, a
  common worker prefix, bounded classifications only, and no more than one bounded
  classification per measurement. The configured city identity is the required bounded
  classification for city-attributable measurements; it MUST NOT be combined with an
  additional arbitrary or entity-level classification.
- **FR-009**: The system MUST keep fewer than 1,000 active measurement series and retain
  at least tenfold headroom against the selected service's series quota. Any proposed
  bounded classification or partition, including the configured city set, MUST document
  its allowed values and revise the series budget before release.
- **FR-010**: The system MUST allow observability to be disabled at deployment startup;
  when disabled, it MUST emit no operational measurements and MUST NOT change worker
  behavior.
- **FR-011**: Each running worker instance MUST be uniquely identifiable in its emitted
  data. Aggregate views and alerts MUST rely on stable service and environment identity,
  not a deployment-specific instance identity.
- **FR-012**: Production operation MUST publish aggregate telemetry only through an
  outbound connection and MUST NOT expose a public monitoring endpoint or enable public
  ingress solely for observability.
- **FR-013**: Production telemetry credentials MUST be supplied as a deployment secret,
  use only the permissions required to publish metrics, and never appear in source,
  container images, or infrastructure literals.
- **FR-014**: Production telemetry MUST contain operational information only. Production
  enablement MUST be gated on approval for telemetry leaving the Azure/Geotab boundary.
- **FR-015**: Monitoring views and alert definitions MUST be version-controlled. The
  worker overview MUST organize its panels in incident-triage order: health, work, input,
  and resources. The views MUST support comparison and filtering by configured city for
  every city-attributable measurement.
- **FR-016**: Every emitted aggregate measurement MUST appear in at least one monitoring
  panel, and every panel MUST describe its meaning, expected healthy behavior, and the
  next diagnostic action when abnormal.
- **FR-017**: The health view MUST provide both a single composite health result and the
  individual liveness, work, and error signals that explain an unhealthy result. It MUST
  provide the equivalent health result and signal breakdown for each configured city.
- **FR-018**: The system MUST define alerts for a stalled worker, missing worker,
  sustained absence of input, recent cycle errors, sustained slow cycles, and sustained
  high input lag. An alert based on missing liveness data MUST treat no data as alerting.
  Every city-attributable alert condition MUST evaluate independently for each configured
  city and identify the affected city in its alert content; a healthy city MUST NOT alert
  because another city's condition is degraded.
- **FR-019**: The stalled-worker alert MUST become critical after three configured cycle
  intervals without a heartbeat. The input-stopped alert MUST warn after 15 minutes, the
  cycle-error alert MUST warn on any error in 15 minutes, the slow-cycle alert MUST warn
  when the 95th-percentile duration exceeds 30 seconds for 15 minutes, and the input-lag
  alert MUST warn when lag exceeds 900 seconds for 10 minutes.
- **FR-020**: An input-lag alert MUST require a known, positive input-lag value so an
  unknown timestamp cannot be interpreted as zero delay.
- **FR-021**: The service MUST provide a local monitoring workflow that uses the same
  version-controlled views as production while isolating developer telemetry from the
  production monitoring tenancy.
- **FR-022**: The service MUST verify its monitoring contract at three levels: emitted
  measurement values and names, worker behavior across successful/idle/failed cycles,
  and the binding between emitted measurements, monitoring panels, and alerts.
- **FR-023**: The service MUST send pending telemetry during orderly shutdown so the final
  completed-cycle information is available after a deployment replacement whenever
  network conditions permit.
- **FR-024**: Operational documentation MUST record the 14-day data-retention limit,
  dashboard identity, alert runbooks, credential rotation procedure, approved contact
  points, the configured city set and its series-budget impact, and the conditions that
  would justify adding an intermediary telemetry service.

### Key Entities

- **Worker Cycle**: One scheduled attempt by the data worker, whether it processed work,
  was idle, or failed.
- **Cycle Summary**: The bounded aggregate of a completed cycle used by both operational
  measurements and structured logs.
- **Configured City Identity**: The closed, documented set of city names the worker
  processes. It is the sole allowed operational classification on city-attributable
  measurements and is not an entity identifier.
- **Operational Measurement**: A numeric, bounded observation of health, input, work,
  decision quality, resource use, configuration, or failure.
- **Structured Log Event**: Detailed forensic evidence for a cycle or entity, including
  information deliberately excluded from operational measurements.
- **Service Identity**: Stable service and environment identity together with a
  per-instance identity used to distinguish concurrent worker instances.
- **Monitoring View**: A version-controlled set of panels that presents operational
  measurements in incident-triage order.
- **Alert Rule**: A version-controlled condition, severity, notification route, and
  documented response for an operational risk.
- **Telemetry Policy**: The approved limits on monitoring data, series budget, retention,
  credential handling, and external-data governance.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In controlled tests of stopped, idle, input-starved, failed, and
  work-producing cycles, operators correctly identify the worker state and the affected
  city in 5 of 5 cases without inspecting structured logs first.
- **SC-002**: A deliberately stopped worker produces a critical notification within three
  configured cycle intervals in 100% of three consecutive rollout tests.
- **SC-003**: 100% of successful, idle, and failed test cycles advance the completed-cycle
  heartbeat; 0% of idle cycles incorrectly advance the work timestamp.
- **SC-004**: 100% of emitted aggregate measurements are represented by at least one
  monitoring panel, and 100% of measurements referenced by a panel or alert are present
  in the emitted measurement contract.
- **SC-005**: The released measurement set remains below 1,000 active series and at least
  ten times below the selected service's series quota under expected production load.
- **SC-006**: Review of the released measurement contract finds 0 customer identifiers,
  entity identifiers, filenames, URLs, exception messages, or free-text attributes.
- **SC-007**: Each of the six required alert conditions is deliberately exercised before
  production enablement; 6 of 6 reaches its designated notification route at the stated
  severity.
- **SC-008**: A local-development run sends 0 observations to the production monitoring
  tenancy, while the same version-controlled monitoring views render local data.
- **SC-009**: 100% of monitoring panels include a description of the signal, healthy
  behavior, and the next diagnostic action.
- **SC-010**: In tests where exactly one configured city has stale input, a failed city
  tick, poor decision quality, slow processing, or resource pressure, the overview and
  any resulting alert identify that city in 5 of 5 cases while unaffected cities remain
  healthy.

## Assumptions

- The target is a new, periodic, single-tenant data worker that starts with one replica
  but may scale later; the observability design must remain correct if it does.
- City identity is a finite configured set rather than an entity identifier. Adding or
  renaming a city is a deliberate configuration change that updates the monitoring
  contract and series budget before release.
- A 10-second measurement-export interval, approximately 250 expected active series, and
  14 days of retained operational history meet the initial near-zero-cost requirement.
  The interval matches the worker's 10-second cycle so the three-cycle liveness target can
  be observed; actual active-series and sample-rate usage must be checked after rollout.
- The hosting environment can provide deployment secrets, outbound HTTPS connectivity,
  process-level health checks, and an approved notification route.
- Structured logging is available for detailed per-cycle and per-entity forensic evidence.
- The source strategy's selected hosted metrics and dashboard service is the settled
  initial provider decision; revisiting providers is outside this feature unless the
  stated cost, retention, governance, or residency assumptions change.
- Longer-term analytics will preserve important baselines outside the 14-day operational
  window rather than treating the monitoring service as a historical data warehouse.

## Out of Scope

- Reusing MyGeotab code, libraries, tenant handling, hosting topology, or monitoring
  configuration.
- Capturing entity-level evidence in aggregate operational measurements.
- Operating a self-hosted monitoring server, persistent metrics store, or a dedicated
  intermediary telemetry service in the initial release.
- Centralizing application traces or structured logs; the feature preserves the boundary
  needed to add those later.
- Long-term metrics retention, historical backfill, and permanent regression analytics
  beyond the stated operational retention period.
- Changing the worker's business decisions, scheduling policy, input source, or data
  processing behavior solely to make them observable.
