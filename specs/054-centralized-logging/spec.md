# Feature Specification: Centralized Structured Logging

**Feature Branch**: `054-centralized-logging`  
**Created**: 2026-08-30  
**Status**: Draft  
**Input**: User description: "docs/AZURE_CENTRALIZED_LOGGING_DESIGN_DOCUMENT.md"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Investigate a Worker Anomaly (Priority: P1)

An operations investigator can move from a city-level Grafana symptom to the discrete, structured events that explain it, without needing access to a bespoke telemetry data store or credentials in a prompt.

**Why this priority**: The central value of the change is making meaningful worker failures and unexpected outcomes explainable while keeping numeric monitoring in Grafana.

**Independent Test**: Trigger a safe city anomaly, then use the read-only investigation workflow with the city and time range to find the event, view its stable reason code and diagnostic context, and reproduce the search query.

**Acceptance Scenarios**:

1. **Given** a city has an anomalous zero-tone cycle, **When** an investigator searches the applicable city and time window, **Then** they receive one bounded anomaly event containing the city, cycle identifier, reason code, relevant counts, and publish outcome.
2. **Given** an investigator starts from a Grafana panel link and a symptom description, **When** they request related log evidence, **Then** the investigation uses the panel's effective city and time context and presents the correlated log evidence and its query.
3. **Given** no event matches an investigation, **When** the query completes, **Then** the result distinguishes an empty match from possible ingestion delay, retention, rate limiting, log level, or filtering explanations.

---

### User Story 2 - Safely Capture Meaningful Events (Priority: P1)

An operations team receives centrally retained application and platform logs for meaningful worker events, while normal successful cycles remain represented by Grafana metrics rather than paid duplicate log rows.

**Why this priority**: The new logging route must improve diagnostics without exposing secrets, flooding storage, or replacing existing continuous monitoring.

**Independent Test**: Run normal, anomalous, repeated-failure, recovery, and secret-bearing-input scenarios; inspect the resulting centralized records and the continued metric signals.

**Acceptance Scenarios**:

1. **Given** a normal successful city cycle, **When** it completes, **Then** it emits no paid informational record solely duplicating its metric samples or full-cycle summary.
2. **Given** a failure repeats with the same city, event type, and reason, **When** the condition persists, **Then** records are coalesced or rate-limited while the initial occurrence, material state changes, periodic reminder, and recovery remain observable.
3. **Given** configured feed credentials or a request URL containing a secret, **When** the related failure is logged, **Then** no token, key, authorization header, cookie, connection string, credential-file content, raw request/response body, or secret-bearing URL is present in the record.

---

### User Story 3 - Diagnose Access and Ingestion Problems (Priority: P2)

An authorized investigator can query the central log workspace read-only and can run a diagnostic check that identifies the first unavailable layer without revealing credentials or attempting an administrative repair.

**Why this priority**: A safe self-service investigation path is only useful if people can tell whether a failed query is caused by their environment, access, routing, or an empty result.

**Independent Test**: Exercise the investigation workflow with valid access and with simulated missing integration, authentication, authorization, connectivity, routing, ingestion-delay, and empty-result conditions.

**Acceptance Scenarios**:

1. **Given** an authorized investigator supplies a bounded query or an event identifier, **When** they request results, **Then** they receive a concise table by default or JSON on request, together with the effective workspace, table, UTC range, query, and result limit.
2. **Given** the investigation path cannot complete, **When** the investigator runs `doctor`, **Then** it identifies one failing layer and a secret-free next action without retrying a persistent permission or configuration error.
3. **Given** an investigator attempts an administrative action through the logging workflow, **When** it is requested, **Then** the workflow refuses it and performs no mutation of Azure resources or observability configuration.

---

### User Story 4 - Cut Over Without Losing Evidence (Priority: P1)

The release owner can migrate from the custom Parquet telemetry path to centralized structured logging with measured cost, proven retention and queryability, and a reversible evidence window before the old writing path is retired.

**Why this priority**: Removing the custom telemetry platform before the new route is proven would risk losing the diagnostic evidence needed to operate the worker.

**Independent Test**: Run the migration gates from baseline measurement through seven consecutive days of dual operation, including a safe canary, a zero-tone anomaly, and a retention check, then verify that removal is blocked until every gate passes.

**Acceptance Scenarios**:

1. **Given** the central route has been configured, **When** a timestamped safe canary is emitted before the route change and fresh post-change markers are emitted after it, **Then** at least two fresh markers are queryable after allowing the documented activation and ingestion delay.
2. **Given** Parquet and centralized logging are both active, **When** seven consecutive days have elapsed, **Then** a known safe day-one event, representative failure evidence, and a zero-tone anomaly remain queryable and can be compared with the legacy path.
3. **Given** any retention, query, redaction, investigation-workflow, or dual-run gate has not passed, **When** a release owner attempts to disable new Parquet writes, **Then** the cutover is blocked and historical blobs remain unchanged.

### Edge Cases

- The preferred query interface cannot query the lower-cost application-log tier; the release must use the explicitly documented Analytics fallback rather than restoring Parquet or adding another datastore.
- Log routing or diagnostic settings are absent, recently changed, or still within the documented activation window; diagnostics must report routing or ingestion delay rather than treating an empty result as proof that no event occurred.
- The observed centralized JSON shape differs from the event contract assumed by queries; production query recipes must wait for a captured, tested record shape.
- A repeated upstream outage, partial or empty input, unavailable route index, failed publication, stale or duplicate feed, or all crossings suppressed occurs; each condition must have bounded, stable evidence rather than an unbounded message stream.
- A requested historical event is outside retention or belongs to legacy custom-table history; the investigation must explain the scope of the selected table and time range.
- An audit finds another consumer of the telemetry storage path; the associated resource and access configuration must not be removed as part of this feature.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST centrally retain worker application output and Container Apps platform events in the existing Azure Log Analytics workspace, using the Azure Monitor routing path rather than the legacy custom-table route.
- **FR-002**: The system MUST retain application console logs in `ContainerAppConsoleLogs` on the Basic plan for 30 days and platform events in `ContainerAppSystemLogs` on the Analytics plan for 30 days. It MAY use the documented Analytics fallback for application logs only after the Basic-query compatibility gate is proven to fail and the decision is recorded.
- **FR-003**: The worker MUST emit single-line, structured JSON records for discrete startup, shutdown, anomaly, failure, transition, recovery, and partial-input events; it MUST NOT create a centrally paid informational record solely to duplicate a normal successful cycle or numeric metric sample.
- **FR-004**: Every queryable application event MUST provide stable, versioned event identity and correlation data: event name, event version, unique event identifier, cycle identifier, outcome, and reason code; city-scoped events MUST also provide the canonical city slug.
- **FR-005**: Events MUST include bounded diagnostic context only when relevant, including duration, deployment revision, exception type, and the defined anomalous-cycle counts and publication outcome; they MUST NOT include full transit payloads or entity arrays.
- **FR-006**: The worker MUST support the event types `WorkerStarted`, `WorkerStopped`, `CityInputFailed`, `CityInputPartial`, `CityInputEmpty`, `RouteIndexUnavailable`, `RouteIndexLoadFailed`, `RouteIndexLoaded`, `CityCycleAnomaly`, `PublishFailed`, `PublishRecovered`, `WorkerCycleFailed`, and `WorkerCycleRecovered`.
- **FR-007**: For an anomalous missing-tone cycle, the system MUST emit `CityCycleAnomaly` with one stable reason code from `NO_VEHICLES`, `STALE_FEED`, `DUPLICATE_FEED`, `ROUTE_INDEX_UNAVAILABLE`, `NO_CROSSINGS`, `ALL_CROSSINGS_SUPPRESSED`, `INPUT_FAILED`, or `PUBLISH_FAILED`, plus city, cycle identifier, relevant counts, and publish outcome.
- **FR-008**: The system MUST coalesce or rate-limit repeated identical failures by stable city, event, and reason key while retaining the initial occurrence, material state changes, periodic reminder, and recovery.
- **FR-009**: The system MUST prevent centrally retained records from containing tokens, API keys, authorization or cookie headers, connection strings, credential-file contents, full request or response bodies, or URLs that contain query-string secrets.
- **FR-010**: The existing Grafana metrics, dashboards, and alerts MUST remain the source of continuous numeric signals and alerts. Centralized logs MUST provide discrete explanations and MUST NOT replace or duplicate those signals.
- **FR-011**: The repository MUST provide a `transitjazz-logs` skill for read-only investigation of the centralized workspace. It MUST support bounded KQL queries, workspace and table discovery, event retrieval, `doctor`, a human-readable table by default, JSON on request, and always-available query display.
- **FR-012**: The logging skill MUST accept a copied Azure Logs link, workspace context, event or cycle identifier, city, deployment revision, event name, reason code, and local or UTC time range. Explicit user input MUST override link context.
- **FR-013**: When given a Grafana dashboard or panel link, the logging skill MUST obtain the effective panel time range, city, and relevant metric through the existing Grafana workflow, then use that context to find related log events without reimplementing dashboard parsing.
- **FR-014**: The logging skill MUST preserve user-provided KQL unless refinement is requested, require table-scoped and time-bounded Basic-table queries, project only useful columns, apply a result limit, and report the effective workspace, table, UTC range, KQL, and limit.
- **FR-015**: The logging skill MUST authenticate through short-lived existing Azure identity and use only workspace/table discovery, queries, and diagnostics. It MUST never request, reveal, print, persist, or accept pasted credentials, and MUST not create or modify Azure resources, observability configuration, saved queries, alerts, workbooks, role assignments, table plans, or retention.
- **FR-016**: The logging skill's `doctor` workflow MUST determine the first failing layer among integration availability, identity, workspace resolution, query permission, table or plan compatibility, ingestion freshness, minimal bounded query execution, and an empty result, then provide a secret-free next action.
- **FR-017**: Before routing is changed, the release process MUST measure the current console-ingestion baseline, identify noisy categories and messages, confirm the target workspace and retention, prove an application-log Basic-table query, and estimate the post-filter monthly ingestion and query-scan volume without selecting a commitment tier.
- **FR-018**: The release process MUST use a timestamped safe canary before the routing change and validate at least two fresh post-change markers after changing to Azure Monitor routing and diagnostic settings. It MUST allow up to 90 minutes for documented activation before declaring the route broken and use log streaming and Grafana during that interval.
- **FR-019**: The deployment MUST provide the intended human or agent principal workspace-scoped `Log Analytics Reader` access, with no Contributor, Monitoring Contributor, or observability-administration permission granted by this feature.
- **FR-020**: The worker MUST keep Parquet writes enabled during a dual-run of at least seven consecutive days. Before disabling those writes, the release MUST verify day-one event retention, representative anomaly and failure evidence including zero tones, query usability, redaction, ingestion and scan costs, and the logging skill's acceptance workflow.
- **FR-021**: After every cutover gate passes, the implementation MUST observe one normal release cycle with centralized logs as the only new log path before removing verified Parquet-only code, dependencies, APIs, tools, skills, deployment resources, documentation, tests, and feature-plan statements.
- **FR-022**: The implementation MUST preserve historical Parquet blobs and MUST NOT delete them or their storage account as part of this feature. Any resource removal MUST follow an audit confirming no other consumer and a separately approved archival or deletion decision.
- **FR-023**: The implementation MUST update the feature 053 observability plan and contracts that describe the Parquet path as unchanged, so they explicitly record this design as the superseding decision.
- **FR-024**: The implementation MUST validate the emitted JSON shape, event contract, redaction, rate limiting, log volume, routing, table retention and plans, skill read-only boundary, `doctor` outcomes, Grafana-to-log correlation, and removal guards through automated or documented release evidence.

### Key Entities

- **Structured log event**: A sparse, centrally queryable record of a meaningful worker or platform occurrence, identified by a stable name, version, event identifier, outcome, and reason code.
- **Cycle correlation**: The unique full-worker-tick identifier that associates city-scoped events and is the primary link between a Grafana symptom and log evidence.
- **Reason code**: A small, stable machine-queryable classification explaining an anomaly or failure without parsing human-readable prose.
- **Investigation context**: The bounded workspace, table, time range, city, identifiers, optional Grafana panel context, query, and result limit used to reproduce an investigation.
- **Dual-run evidence**: The measured and verified proof that legacy Parquet telemetry and centralized logs provide sufficient overlapping diagnostic coverage before the legacy write path is disabled.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A safe structured test event is queryable from `ContainerAppConsoleLogs` with 100% of required identity, correlation, outcome, and reason fields present and with zero secret-bearing values in its record.
- **SC-002**: Both central log streams are configured for 30-day retention, and a known safe test event emitted on day one remains queryable after at least seven consecutive days.
- **SC-003**: In a normal successful-city-cycle test, zero paid informational log rows are emitted solely to duplicate cycle metrics; in a zero-tone anomaly test, exactly one bounded `CityCycleAnomaly` record is emitted with the required context.
- **SC-004**: In a controlled sequence of ten identical failures followed by recovery, the system records fewer than ten failure events while retaining the initial failure and a recovery event.
- **SC-005**: An authorized investigator can complete a scripted Grafana-city-and-time-to-log-evidence investigation, including reproduction of the query, in five minutes or less using table output; the same result is available as JSON on request.
- **SC-006**: `doctor` correctly distinguishes 100% of the defined integration, authentication, RBAC, connectivity, workspace/table, ingestion-delay, Basic-query-compatibility, and empty-result test scenarios without displaying a credential.
- **SC-007**: The logging skill completes 100% of its supported read-only discovery and query scenarios while rejecting 100% of attempted Azure or observability mutations.
- **SC-008**: During seven consecutive days of dual-run, representative input-failure, publish-failure, and zero-tone anomaly evidence is available in both observability paths; no Parquet write is disabled before every stated dual-run gate passes.
- **SC-009**: Existing Grafana dashboards and alerts continue to show the numeric symptom for all central-log test scenarios, including when an event is delayed or unavailable to a log query.
- **SC-010**: After cutover, a reviewed removal audit confirms no new Parquet telemetry writes remain, while 100% of historical blobs identified by the audit remain intact pending a separate retention decision.

## Assumptions

- The existing Azure Container Apps environment and Log Analytics workspace remain the approved target for centralized logs and can be updated through the repository's normal infrastructure workflow.
- The release owner will identify the intended human or agent principal for workspace-scoped read-only access through the normal deployment process.
- The existing Grafana workflow can provide panel time and city context; the new logging skill composes with it instead of duplicating it.
- The bounded event names and reason codes in this specification are the initial public contract; additions require an explicit versioned contract update.
- The lower-cost application-log tier is the default target. Its documented Analytics fallback is limited to failure of the Basic-query compatibility gate and does not expand this feature to a second datastore or a custom telemetry platform.
- Generated deployment artifacts are regenerated through the repository's normal build process rather than manually edited.
- Production enablement follows the existing observability governance and constitutional prerequisites described by feature 053; this specification does not independently authorize those approvals.
