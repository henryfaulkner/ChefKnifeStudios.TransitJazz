# Feature Specification: Remove the Parquet Telemetry Sidecar

**Feature Branch**: `055-remove-parquet-sidecar`  
**Created**: 2026-08-30  
**Status**: Draft  
**Input**: User description: "I want to full remove the parquet sidecar"

## Overview

Feature 013 introduced an in-process telemetry sidecar that buffers worker events and writes immutable Parquet part-files to blob storage, with a private query tool, an MCP bridge, an exploration skill, and an in-app telemetry page reading them. Feature 054 replaced the diagnostic purpose of that path with centralized structured logging plus Grafana metrics, and deliberately left the old path running behind an evidence-gated dual run.

This feature retires that path completely: the writing sidecar, its configuration and dependency, the reader tooling, the in-app telemetry page, and the storage it wrote to. After it ships, the project has exactly two observability surfaces — Grafana for numeric monitoring and centralized structured logs for event investigation — and no component in the repository reads or writes Parquet.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Retire the Writing Path (Priority: P1)

The release owner removes the telemetry sidecar from the running worker and API so no service buffers, serializes, or uploads telemetry rows, while every existing monitoring and investigation signal continues to work unchanged.

**Why this priority**: This is the change that actually stops the cost, the managed-identity write access, and the background work. Everything else in this feature is cleanup that depends on the writing path being gone.

**Independent Test**: Deploy the worker and API with the sidecar removed, run them for a full multi-city cycle window, and confirm no telemetry objects are written, no startup or shutdown errors appear, cycles complete on their normal cadence, and every Grafana panel and structured-log investigation still returns the same signals as before.

**Acceptance Scenarios**:

1. **Given** the worker is running after removal, **When** it completes multiple city cycles, **Then** no telemetry data objects are created in storage and the cycle cadence, published batches, and vehicle behavior are unchanged from before the removal.
2. **Given** an operator is watching existing monitoring, **When** the removal is deployed, **Then** every numeric monitoring panel continues to receive samples with no gap attributable to the removal.
3. **Given** an investigator reproduces a known worker anomaly, **When** they follow the centralized log investigation workflow, **Then** they find the same structured event evidence that was available before the removal.
4. **Given** the worker is stopped and restarted, **When** it shuts down and starts up, **Then** no flush, upload, credential, or missing-configuration error is produced, because no such step remains.
5. **Given** any remaining configuration is loaded, **When** the services start, **Then** no setting exists whose only purpose was to control telemetry buffering, flushing, container targeting, or the sidecar kill switch.

---

### User Story 2 - Remove Orphaned Reader Surfaces (Priority: P1)

A visitor to the site and a developer working in the repository no longer encounter a telemetry view, query tool, bridge, or exploration workflow that reads a data store which no longer receives writes.

**Why this priority**: The moment writing stops, every reader becomes a surface that silently shows stale or empty results. Leaving them is worse than never having had them, because they look functional while quietly misinforming.

**Independent Test**: Browse the site and confirm no telemetry view or link to one is reachable; inspect the repository and confirm no tool, bridge configuration, or documented workflow claims to query telemetry.

**Acceptance Scenarios**:

1. **Given** a visitor is on the site's landing page, **When** they look for a telemetry view, **Then** no link to one is present and no route serves such a page.
2. **Given** a developer opens the repository's tool and integration configuration, **When** they list available data-query integrations, **Then** none references the retired telemetry store.
3. **Given** a developer looks for guidance on investigating worker behavior, **When** they consult the documented workflows, **Then** the only workflow offered is the centralized log investigation path, and no workflow instructs them to query the retired store.
4. **Given** the solution is built and its automated tests are run, **When** the build completes, **Then** it succeeds with no unresolved reference, no dead configuration binding, and no test asserting behavior of the removed path.
5. **Given** a reviewer searches the repository for references to the retired telemetry path, **When** the search completes, **Then** remaining matches are limited to historical records — prior feature specifications, past incident reports, and changelog-style history — and no active code, configuration, tool, or current guidance document.

---

### User Story 3 - Reclaim the Storage and Its Access (Priority: P2)

The release owner removes the telemetry storage and the write permission granted to the services, so the project no longer pays for, or holds standing write access to, a store nothing uses.

**Why this priority**: This delivers the remaining cost and least-privilege benefit, but it is genuinely irreversible for historical data, so it follows the code removal rather than blocking it.

**Independent Test**: After the writing path is gone and the retention decision is recorded, remove the storage and its access grant, then confirm the services still start, run, and monitor correctly with no permission or configuration error.

**Acceptance Scenarios**:

1. **Given** the writing path has been removed and deployed, **When** the storage and its access grant are removed, **Then** the worker and API start and run a full cycle window with no permission, credential, or missing-resource error.
2. **Given** the infrastructure definition is inspected after removal, **When** a reviewer reads it, **Then** it declares no telemetry storage, no telemetry container, no telemetry write role assignment, and no dual-run toggle.
3. **Given** historical telemetry data existed, **When** removal is performed, **Then** the decision to discard or first export that history has been explicitly recorded and approved by the release owner beforehand.

---

### User Story 4 - Retire Only After the Evidence Gate Passes (Priority: P1)

The release owner can confirm, before any removal ships, that centralized logging has independently proven itself over the required evidence window, so the fallback is discarded only once it is genuinely no longer needed.

**Why this priority**: The prior feature deliberately kept both paths running specifically so this retirement would be safe. Removing before that window completes discards the safeguard that justified building it.

**Independent Test**: Attempt to advance the removal with the evidence record incomplete and confirm it is blocked; complete the record and confirm removal is then authorized.

**Acceptance Scenarios**:

1. **Given** the dual-run evidence record has unfinished or failing entries, **When** removal is proposed, **Then** it is blocked and the specific unmet entries are identified.
2. **Given** every evidence entry has passed for the full required consecutive-day window, **When** the release owner reviews the record, **Then** removal is authorized and the authorization is recorded with an approver and date.
3. **Given** removal has been authorized and performed, **When** a reviewer consults the release record afterward, **Then** it shows the completed evidence window, the authorization, and the fact that the legacy path is retired rather than still pending.

---

### Edge Cases

- A deployment lands the worker and API at different times: neither service may fail, warn, or behave differently because the other still has, or no longer has, the sidecar — each must run correctly against the removed state independently.
- Storage is deleted before the code that writes to it is fully deployed everywhere: the services must degrade quietly without crash-looping, blocking a cycle, or emitting repeated credential noise. The stated deployment order exists to avoid this, but the system must tolerate it.
- A stale client build still requests the removed telemetry view: the request must fail cleanly as a missing route rather than surfacing an error page implying a service outage.
- An investigator follows a bookmarked link or saved instruction pointing at the retired path: they must reach a clear indication that the path is retired and be directed to the centralized log workflow.
- Self-health signals that existed only to monitor the sidecar (buffer occupancy, dropped records, persist failures) no longer have a subject: they must be removed rather than left reporting constant zeros that a viewer could misread as healthy.
- Historical telemetry data is requested after storage removal: the response must be that the data was intentionally discarded under a recorded decision, not an implication that it was lost.

## Requirements *(mandatory)*

### Functional Requirements

**Writing path removal**

- **FR-001**: The system MUST NOT buffer, serialize, or upload telemetry rows from any running service.
- **FR-002**: The system MUST NOT retain any background component whose purpose is draining a telemetry buffer or flushing telemetry on a timer or at shutdown.
- **FR-003**: The system MUST NOT retain the third-party columnar-file dependency, in any project, once no component reads or writes such files.
- **FR-004**: The system MUST NOT retain configuration settings whose only purpose is controlling telemetry buffering, flush cadence, channel capacity, target container, storage endpoint, connection string, or the sidecar kill switch — in any environment's configuration.
- **FR-005**: The system MUST continue to emit every centralized structured log event and every numeric monitoring sample that existed before removal, unchanged in name, meaning, and cadence.
- **FR-006**: The system MUST NOT retain self-health signals that exist solely to report on the removed sidecar.
- **FR-007**: Worker cycle behavior — cadence, per-city processing, batch publication, and vehicle output — MUST be observably unchanged by the removal.

**Reader surface removal**

- **FR-008**: The system MUST NOT serve a user-facing telemetry view, and MUST NOT present a link to one from any page.
- **FR-009**: The system MUST NOT expose service endpoints whose purpose is returning telemetry rows or telemetry summaries read from the retired store.
- **FR-010**: The system MUST NOT retain client-side services, page components, or shared data contracts that exist solely to carry telemetry rows between the retired store and a view.
- **FR-011**: The repository MUST NOT retain standalone tools whose purpose is querying the retired telemetry store.
- **FR-012**: The repository MUST NOT retain integration configuration registering a telemetry query capability against the retired store.
- **FR-013**: The repository MUST NOT retain agent workflows or guidance instructing a reader to query the retired telemetry store; the centralized log investigation workflow MUST remain the sole documented investigation path.
- **FR-014**: Current guidance documents MUST describe the retired path as retired; historical records — prior feature specifications, incident reports, and changelog-style history — MUST be left intact as written.

**Infrastructure removal**

- **FR-015**: The infrastructure definition MUST NOT declare telemetry storage, a telemetry container, or a telemetry write role assignment.
- **FR-016**: The infrastructure definition MUST NOT retain a toggle for keeping the legacy path enabled.
- **FR-017**: The services MUST NOT hold standing write access to any telemetry store after removal.

**Gate and sequencing**

- **FR-018**: Removal MUST NOT ship until the dual-run evidence record shows every entry passing for the full required consecutive-day window.
- **FR-019**: The authorization to proceed MUST be recorded with an approver and date before removal ships.
- **FR-020**: The decision to discard historical telemetry data, or to export it first, MUST be explicitly recorded and approved before storage is removed.
- **FR-021**: Storage removal MUST follow successful deployment of the code that stops writing to it.
- **FR-022**: The release record MUST reflect the retired state after removal, rather than continuing to describe the path as pending.

**Verification**

- **FR-023**: The solution MUST build and its automated tests MUST pass with no unresolved reference to, and no test asserting behavior of, the removed path.
- **FR-024**: A repository-wide search for the retired path MUST return only historical records, with no active code, configuration, tool, or current guidance document among the results.

### Key Entities

- **Telemetry sidecar**: The removed in-process capability that buffered worker events and persisted them as columnar files to blob storage, together with its buffering worker, sink abstraction, event row schemas, and options.
- **Telemetry store**: The removed blob container and storage account that held the persisted files, and the write permission granted to the services over it.
- **Reader surfaces**: The removed consumers of the store — the in-app telemetry view and its supporting endpoints, client service and shared contracts; the standalone query tool; the query bridge and its integration registration; and the exploration workflow.
- **Retained observability**: The centralized structured logging path and the numeric monitoring path, both of which survive this feature unchanged and become the only observability surfaces.
- **Evidence record**: The dual-run release record whose completed entries, approver, and date authorize this removal.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Over a full day of normal operation after removal, zero telemetry data objects are created.
- **SC-002**: The recurring cost attributable to telemetry storage and its data operations falls to zero within one billing period of storage removal.
- **SC-003**: Every monitoring panel and every structured-log investigation available before removal returns equivalent results after removal, with a 100% match on the reviewed set.
- **SC-004**: Worker cycle completion cadence after removal stays within normal historical variation, with no sustained regression.
- **SC-005**: Zero visitor-reachable links or routes lead to a telemetry view.
- **SC-006**: Zero tools, integrations, or current guidance documents instruct anyone to query the retired store.
- **SC-007**: The full build and automated test suite passes with zero failures attributable to the removal.
- **SC-008**: A repository-wide search for the retired path returns matches only in historical records.
- **SC-009**: The services hold zero standing write permissions on any telemetry store after removal.
- **SC-010**: Removal ships only after the evidence record shows a complete passing window, verifiable from the recorded approver and date.

## Assumptions

- Centralized structured logging and numeric monitoring together cover every diagnostic need the telemetry path served; the evidence gate is precisely the mechanism that confirms this before removal ships.
- The in-app telemetry view is a developer-facing diagnostic rather than a visitor feature, so deleting it removes no visitor-valued capability. It is not rebuilt against the centralized log workspace, because that would duplicate monitoring the project already has.
- Historical telemetry data has no ongoing analytical value beyond the evidence window. The explicit recorded decision required before storage removal is the checkpoint where this assumption is confirmed rather than assumed.
- The exploration workflow's non-telemetry capabilities, where it has any, are considered separately from its telemetry query function; only the retired-store dependency is in scope here.
- Historical feature specifications and incident reports describing the telemetry path are accurate records of their time and are left unedited; only current guidance is updated.
- Deployment order — stop writing first, then remove storage — is the intended sequence, and the system is expected to tolerate the reverse order without crash-looping should it occur.

## Dependencies

- The centralized structured logging capability must be deployed and operating in every environment where the telemetry path is removed.
- The dual-run evidence record must be complete and passing for its full required window, with a recorded approver and date.
- A recorded decision on discarding or exporting historical telemetry data must exist before storage removal.
- Infrastructure change authority is required to remove the storage account, container, and role assignment.

## Out of Scope

- Rebuilding the telemetry view against the centralized log workspace, or any replacement in-app analytics surface.
- Any change to what the centralized structured logging path emits, how it is queried, or how it is retained.
- Any change to numeric monitoring — its metrics, dashboards, alerts, or retention.
- Exporting or migrating historical telemetry data into another store; only the decision about that data is in scope.
- Worker data-processing behavior, city onboarding, wire format, and every other application capability unrelated to the retired path.
- Retroactive editing of historical specifications, incident reports, or changelog-style history.
