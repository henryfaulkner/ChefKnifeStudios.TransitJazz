# TransitJazz structured log event v1

The source contract is `specs/054-centralized-logging/contracts/structured-log-event-v1.md`.
The application event fields are structured state inside the console `Log` value; recipes must be
updated from an actual, redacted `ContainerAppConsoleLogs` row before production use.

## Required envelope

| Field | Rule |
|---|---|
| `EventName` | One of the eleven v1 names below |
| `EventVersion` | Positive integer, currently `1` |
| `EventId` | Fresh opaque identifier |
| `CycleId` | One opaque identifier per worker tick when applicable |
| `Outcome` | `Succeeded`, `Partial`, or `Failed` |
| `ReasonCode` | Bounded uppercase code when classification exists |
| `City` | Canonical lowercase city slug for city events |
| Optional context | Non-negative counters, duration, safe revision, exception type, publish state |

## Event names

`WorkerStarted`, `WorkerStopped`, `CityInputFailed`, `CityInputPartial`, `CityInputEmpty`,
`RouteIndexUnavailable`, `CityCycleAnomaly`, `PublishFailed`, `PublishRecovered`,
`WorkerCycleFailed`, and `WorkerCycleRecovered`.

## Missing-tone reasons

`NO_VEHICLES`, `STALE_FEED`, `DUPLICATE_FEED`, `ROUTE_INDEX_UNAVAILABLE`, `NO_CROSSINGS`,
`ALL_CROSSINGS_SUPPRESSED`, `INPUT_FAILED`, and `PUBLISH_FAILED`.

## Prohibited values

Never display tokens, API keys, authorization/cookie headers, connection strings, credential files,
full request/response/feed bodies, entity arrays, raw URLs with query secrets, or arbitrary exception
text. Exception type names and safe endpoint identities are the only permitted failure context.

