# Atlanta data source investigation — 2026-11-07

## Findings

The production MARTA rail source is failing while the bus source still works.
The repository still configured the legacy rail URL. MARTA's October 29 email
announces this replacement and says existing API keys remain valid:

```text
https://developerservices.itsmarta.com/mcsmrail/itsmarta/railrealtimearrivals/developerservices/traindata
```

On November 7, both the legacy and replacement URLs returned HTTP 400 with
`ErrorDetails: Invalid API Key` when requested without a key. The Container App
had no rail API key or rail URL environment override, and the committed settings
contain no rail API key. No worker user-secrets file was present locally.
An authenticated response/schema check remains outstanding. The email does not
establish that keyless requests remain supported.

The bus vehicle-positions feed returned HTTP 200 and a 12,530-byte protobuf
response. Production logs at 14:00:15 UTC reported 152 Atlanta vehicles processed,
37 tone opportunities emitted, and successful publication, alongside repeated
`MARTA rail source failed; bus path unaffected` warnings with `HttpRequestException`.

## Why the dashboard has counts but no distance

The production stats response generated at 14:08:45 UTC, with a 14:00 UTC cutoff,
reported no contributing distance hours. All 9 closed hours of the Atlanta local
day (05:00–14:00 UTC) and all 24 preceding completed hours (November 6 14:00 through
November 7 14:00 UTC) were excluded with `partial_cohort`. Reported vehicles still
had contributions, with limitations including `missing_source` and `source_failure`.
The last-24-hour vehicle count was 251; this count is partial, not a complete fleet
total.

`MartaCity` combines bus and rail fetch outcomes. In `Worker.cs`, activity is eligible
only for a successful or empty combined fetch; a partial fetch fails that gate.
`RouteHourStatisticsCapture` marks those cycles incomplete, and the stats query
excludes partial city/hour cohorts. This explains why a rail failure also removes
bus distance and route-activity charts, despite live bus processing continuing.
Logs confirm that Atlanta route-hour writes themselves still succeed (86 rows for
13:00–14:00 UTC), so absent charts do not establish a stopped database writer.

## Prepared change and recovery

Moved the rail endpoint from the positional city array into the named source
section `TransitSources:atlanta:RailRealtime` in the worker and WebAPI production
and development appsettings files. The city array retains the source's enabled
flag and reported-vehicle identity policy; the endpoint and API key no longer
depend on Atlanta being at index zero.

To restore production, configure the existing MARTA key through a Container App
secret reference named by environment setting
`TransitSources__atlanta__RailRealtime__ApiKey`,
and deploy the URL change. Keep the key out of source, reports, and command output.
Verify that an authenticated replacement endpoint returns the JSON array consumed
by `MartaCity`, including `TRAIN_ID`, `LINE`, `LATITUDE`, `LONGITUDE`, and `IS_REALTIME`.
Then verify rail-source warnings stop, live trains return, and a subsequent full
UTC hour has eligible statistics. Other coverage failures can also keep an hour
partial. The overlay refreshes hourly and reports completed hours only.

Previously excluded hours cannot be reconstructed merely by changing the URL;
the missed live train observations were not captured. No production configuration,
database history, or deployments were changed during this investigation.

Validation: all four modified settings files parse as JSON and all six endpoint
values match the migration URL. The existing `CityConfigParityTests` passed (5/5),
and `git diff --check` passed. Authenticated integration validation is pending
availability of the existing MARTA API key.

## Evidence query

Workspace: `marta-jazz-dev-law` (`dd9c8c7e-dae8-410b-b876-2cee18c7ad2c`), resource
group `marta-jazz-dev-rg`. Table: `ContainerAppConsoleLogs` (Basic plan).
UTC range: November 7, 2026, 13:55–14:10. Limit: 15. Output: table.
Serving revision inspected: `marta-jazz-dev-ca-server--0000202`.

Executed with the official Azure Monitor Basic-table search interface:

```kusto
ContainerAppConsoleLogs
| where TimeGenerated between (datetime(2026-11-07T13:55:00Z) .. datetime(2026-11-07T14:10:00Z))
| where Log contains 'MARTA' or Log contains 'atlanta' or Log contains 'route-hour'
| project TimeGenerated, Log
| take 15
```

Stats evidence came from the read-only query endpoint:
`POST /stats/overlay/query` with
`{"scope":"city","citySlug":"atlanta","routeJoinKeys":[]}`.
