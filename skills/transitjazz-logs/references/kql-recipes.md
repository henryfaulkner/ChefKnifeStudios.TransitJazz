# Draft bounded KQL recipes

Status: **DRAFT — pending T036 capture of the actual Azure `Log` JSON shape.** These recipes show
the required table/range/projection/limit guard. Replace only the `parse_json(Log)` projections
after a redacted real row is captured and reviewed; do not infer the final Azure formatter shape.

Use a finite UTC range no wider than necessary. The examples use a 15-minute window and end with a
1–100 `take`. Do not add a second source table, `join`, `find`, KQL `search`, `externaldata`, or
unbounded operator to a Basic console query.

## Event by ID

```kusto
ContainerAppConsoleLogs
| where TimeGenerated between (datetime(2026-08-30T12:00:00Z) .. datetime(2026-08-30T12:15:00Z))
| extend Event = parse_json(Log)
| extend EventId = tostring(Event.EventId), EventName = tostring(Event.EventName), City = tostring(Event.City)
| where EventId == 'EVENT_ID'
| project TimeGenerated, EventId, EventName, City, Log
| take 10
```

## Cycle/city/reason anomaly

```kusto
ContainerAppConsoleLogs
| where TimeGenerated between (datetime(2026-08-30T12:00:00Z) .. datetime(2026-08-30T12:15:00Z))
| extend Event = parse_json(Log)
| extend CycleId = tostring(Event.CycleId), EventName = tostring(Event.EventName), ReasonCode = tostring(Event.ReasonCode), City = tostring(Event.City)
| where EventName == 'CityCycleAnomaly' and City == 'atlanta' and ReasonCode == 'NO_CROSSINGS'
| project TimeGenerated, CycleId, EventName, ReasonCode, City, Log
| take 20
```

## Input, route-index, publish, recovery, revision, and freshness

Use the same finite `where` and `extend` shape, changing only the bounded predicate and projected
fields:

```kusto
ContainerAppConsoleLogs
| where TimeGenerated between (datetime(2026-08-30T12:00:00Z) .. datetime(2026-08-30T12:15:00Z))
| extend Event = parse_json(Log)
| extend EventName = tostring(Event.EventName), Outcome = tostring(Event.Outcome), ReasonCode = tostring(Event.ReasonCode), City = tostring(Event.City), DeploymentRevision = tostring(Event.DeploymentRevision), FeedFreshnessSeconds = todouble(Event.FeedFreshnessSeconds)
| where EventName in ('CityInputFailed', 'CityInputPartial', 'CityInputEmpty', 'RouteIndexUnavailable', 'PublishFailed', 'PublishRecovered', 'WorkerCycleRecovered')
| project TimeGenerated, EventName, Outcome, ReasonCode, City, DeploymentRevision, FeedFreshnessSeconds, Log
| take 50
```

System-table queries use the same finite range and limit, but must not parse application event JSON
unless the selected row actually contains it.

