# Feature Specification: Transit Telemetry Datasets for the Query Bridge

**Feature Branch**: `014-transit-datasets`  
**Created**: 2026-06-04  
**Status**: Draft  
**Input**: User description: "Update telemetry-mcp for transit parquet datasets (tools/telemetry-mcp/DESIGN-transit-datasets.md)"

## Overview

The telemetry query bridge currently lets an operator ask questions, in natural
language through their assistant, about a single built-in demo dataset (the
"iris" sample). Meanwhile, the logging sidecar now publishes three real
operational datasets describing how the transit reconciliation pipeline behaves:
per-vehicle snap decisions, per-vehicle position deltas, and per-cycle summaries.
This feature retargets the query bridge away from the demo dataset and onto those
three real datasets, so operators can interrogate live transit behavior instead of
sample data — while preserving the same safety guarantees that prevent a question
from doing anything other than reading the intended data.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Query a real transit dataset by name (Priority: P1)

An operator investigating transit pipeline behavior asks their assistant a
question that targets one of the three real datasets (snap, lerp, or cycle) and
expresses a filter condition over that dataset's fields (for example, "show snap
records where the snap distance is over half a kilometer"). The bridge selects the
correct dataset, applies the filter, and returns matching rows.

**Why this priority**: This is the entire point of the feature. Without the ability
to name a real dataset and filter it, the bridge has no value beyond the demo it
replaces. It is the minimum viable slice — it delivers value on its own.

**Independent Test**: Can be fully tested by issuing a query that names a valid
dataset and a valid filter over that dataset's fields, and confirming matching rows
are returned from that dataset and no other.

**Acceptance Scenarios**:

1. **Given** the snap dataset exists, **When** the operator queries `snap` with a
   filter over a snap field, **Then** matching snap rows are returned.
2. **Given** the cycle dataset exists, **When** the operator queries `cycle` with a
   filter combining a numeric field and a yes/no field, **Then** matching cycle rows
   are returned.
3. **Given** the lerp dataset exists, **When** the operator queries `lerp` with a
   filter combining a numeric field and a text field, **Then** matching lerp rows are
   returned.

---

### User Story 2 - Scope a query to a specific day (Priority: P2)

The datasets are organized by calendar day. An operator wants to look at a
particular day's data (for example, yesterday's cycles) rather than the entire
history. They supply the date alongside the dataset and filter, and only that day's
records are considered. If they omit the date, the current day is used.

**Why this priority**: Operators almost always investigate "what happened on day X."
Without date scoping every query would scan all days, which is slower and noisier.
It builds directly on Story 1 but is not required for the bridge to be useful.

**Independent Test**: Can be tested by issuing the same filter twice, once with an
explicit past date and once with no date, and confirming the explicit-date query
returns that day's records while the no-date query targets the current day.

**Acceptance Scenarios**:

1. **Given** a valid dataset and filter, **When** the operator supplies a date in
   year-month-day form, **Then** only that day's records are considered.
2. **Given** a valid dataset and filter, **When** the operator supplies no date,
   **Then** the current (UTC) day is used.

---

### User Story 3 - Unsafe or unsupported queries are rejected before any data is touched (Priority: P1)

A query that names a dataset that does not exist, references a field that is not
part of the chosen dataset, supplies a value of the wrong kind for a field, or
otherwise falls outside what the bridge supports is refused with a clear reason,
and no data access is attempted.

**Why this priority**: The bridge's safety model is the reason it can be exposed to
an assistant at all. Retargeting the datasets replaces the entire set of recognized
fields, so the rejection behavior must be re-verified against the new fields. A
regression here is a security regression, not just a usability one — so it shares
top priority with Story 1.

**Independent Test**: Can be tested by issuing queries with an unknown dataset, an
unknown field, a field borrowed from a different dataset, a mistyped value, and a
date in the wrong form, and confirming each is rejected with a reason and no data is
read.

**Acceptance Scenarios**:

1. **Given** a query naming a dataset outside the supported set, **When** it is
   submitted, **Then** it is rejected before any field or data is examined.
2. **Given** a filter referencing a field that belongs to a different dataset,
   **When** it is submitted against the chosen dataset, **Then** it is rejected as an
   unknown field.
3. **Given** a yes/no field compared to a numeric value (rather than a yes/no
   value), **When** it is submitted, **Then** it is rejected for value-type mismatch.
4. **Given** a date that is not in year-month-day form, **When** it is submitted,
   **Then** it is rejected before any data is read.
5. **Given** a filter using a field from the previously-supported demo dataset,
   **When** it is submitted, **Then** it is rejected as an unknown field.

---

### Edge Cases

- **Field shared across datasets but compared incorrectly**: Some field names (such
  as the cycle identifier or vehicle identifier) appear in more than one dataset.
  A query must be validated against the chosen dataset's fields, not a global field
  list.
- **Empty result for a valid query**: A well-formed query that simply matches no
  records returns an empty result, not an error.
- **No data for the requested day**: A valid query for a day that has no recorded
  data returns an empty result rather than failing.
- **Date/time field compared to a value**: Date/time fields are compared using a
  written date value, not a raw number; a numeric comparison against a date/time
  field is rejected.
- **Field name containing a separator/path character**: Field names in the new
  datasets contain no path separators, so any field reference containing one is
  treated as an unknown field.
- **Migration of an existing configuration**: An operator whose configuration still
  points at the old demo dataset receives a clear startup error directing them to the
  new configuration rather than silently running against nothing.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The bridge MUST support exactly three datasets — snap, lerp, and
  cycle — and MUST reject any dataset name outside that set before examining the
  query's filter.
- **FR-002**: Each query MUST identify which of the three datasets it targets; the
  dataset is a required input.
- **FR-003**: The bridge MUST validate every field referenced in a filter against
  the chosen dataset's own set of recognized fields, and reject any unrecognized
  field.
- **FR-004**: The set of recognized fields, their names, and their value kinds for
  each dataset MUST match the frozen field contract published by the logging sidecar
  feature, so that the names operators use line up with the data that exists.
- **FR-005**: The bridge MUST distinguish value kinds — numeric, text, date/time,
  and yes/no — and reject a comparison whose value is the wrong kind for the field
  (for example, a yes/no field compared to a number, or a date/time field compared
  to a bare number).
- **FR-006**: Date/time fields MUST be compared using a written date value; yes/no
  fields MUST be compared using a yes/no value, not a numeric stand-in.
- **FR-007**: The bridge MUST no longer recognize any field from the previously
  supported demo dataset; such fields MUST be rejected as unknown.
- **FR-008**: Field references that include a path/separator character MUST be
  rejected as unknown, since the supported fields contain no such characters.
- **FR-009**: A query MAY scope itself to a single calendar day supplied in
  year-month-day form; the day value MUST be validated to that exact form before use
  and MUST never be drawn from the free-text filter.
- **FR-010**: When no day is supplied, the bridge MUST default to the current UTC
  day.
- **FR-011**: The dataset, the day, and the filter MUST each be validated
  independently before the bridge assembles and runs the query; none of them may
  reach the executed query unvalidated.
- **FR-012**: The data source the query reads from MUST be derived from a fixed,
  bridge-controlled template parameterized only by the validated dataset and day —
  operator input MUST NOT be able to redirect the query to a different data source.
- **FR-013**: The bridge MUST retain its existing protections against unsupported
  operations and characters; this change does not relax them.
- **FR-014**: The bridge's self-description presented to the assistant MUST name the
  three datasets and offer transit-relevant examples in place of the demo examples,
  so the assistant forms valid queries.
- **FR-015**: A configuration that still references the removed demo-dataset setting
  MUST produce a clear startup error pointing to the replacement setting, rather than
  starting in a broken state.

### Key Entities *(include if feature involves data)*

- **Snap dataset**: One record per per-vehicle snap decision within a reconciliation
  cycle. Fields describe the vehicle, route, raw and snapped position, snap distance,
  outcome category, optional speed/bearing, and a staleness flag.
- **Lerp dataset**: One record per per-vehicle position delta (for vehicles that had
  a prior state). Fields describe the vehicle, prior route and position, prior and
  current observation times, and position/speed/bearing/time deltas.
- **Cycle dataset**: One record per completed reconciliation cycle. Fields describe
  cycle timing, counts of vehicles by outcome, skip reasons, feed metadata, cache
  sizes, and sidecar health counters.
- **Query request**: An operator's request, consisting of a required dataset name, an
  optional day, and a required filter condition over the chosen dataset's fields.
- **Field contract**: The frozen, shared definition of each dataset's field names and
  value kinds, owned by the logging sidecar feature and consumed here as the source of
  truth for what operators may reference.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can retrieve matching records from each of the three
  datasets using a natural-language request, without knowing where or how the data is
  stored.
- **SC-002**: 100% of queries that name an unsupported dataset, reference an
  unknown or wrong-dataset field, use a wrong-kind value, or supply a malformed day
  are rejected with a stated reason and cause no data access.
- **SC-003**: Every field an operator is told they can reference corresponds to a
  field that actually exists in the targeted dataset (zero mismatches between the
  advertised field set and the real data contract).
- **SC-004**: An operator can scope a query to a chosen day, and omitting the day
  reliably targets the current day.
- **SC-005**: An operator whose configuration still uses the removed demo setting is
  told, at startup, exactly which setting to use instead — with no silent failure.
- **SC-006**: No query input can cause the bridge to read from a data source other
  than the intended dataset-and-day location.

## Assumptions

- The three datasets and their field contracts are already produced and frozen by
  the logging sidecar feature; this feature consumes that contract and does not
  redefine it.
- The underlying data-access tool the bridge delegates to is unchanged; only the
  bridge's targeting, validation field set, and self-description change.
- The bridge continues to be a local operator tool driven through an assistant, not
  an end-user-facing or publicly exposed service.
- Datasets are organized by calendar day, and querying a single day at a time is
  sufficient for this feature; multi-day ranges and surfacing the day as a filterable
  field are explicitly out of scope.
- Choosing which fields to return (projection) is out of scope; queries return the
  full record set for matching rows.
- Existing protections against unsupported operations, characters, and the data
  source being operator-controlled remain in force and are not part of what this
  feature changes — only re-verified against the new field set.
