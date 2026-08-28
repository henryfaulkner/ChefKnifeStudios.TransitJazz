# Monitoring Assets Contract

## Source of truth

The repository owns `observability/grafana/dashboards/transitjazz-worker-overview.json` with stable UID `transitjazz-worker-overview`, canonical alert source `observability/grafana/alerts/transitjazz-worker-alerts.json`, local provisioning, and Grafana Terraform resources. Default dashboard range is six hours; refresh is 10 seconds; rows are Health, Work, Input, Resources. Every panel describes meaning, healthy behavior, and next diagnostic action.

## Required alert behavior

| Alert | Scope | Condition | State/severity |
|---|---|---|---|
| `WorkerStalled` | worker | Last full-cycle heartbeat older than three worker intervals | Critical; no data alerting |
| `WorkerGone` | worker | Full-cycle counter missing | Critical; no data alerting |
| `CityMissing` | city | Previously present city absent from recent three-tick window | Critical; one alert per missing city |
| `CityInputStopped` | city | No input for 15 minutes | Warning |
| `CityCycleErrors` | city | Any city errors in 15 minutes | Warning |
| `CityCycleSlow` | city | City duration p95 above 30 seconds for 15 minutes | Warning |
| `CityInputLagHigh` | city | Known input lag above 900 seconds for 10 minutes | Warning |

`CityMissing` uses historical presence, not only generic no-data. Input lag requires a known, positive timestamp-derived value. City alert summaries include `transit_city` and must not alert for unaffected cities.

## Binding-test rules

1. Every emitted metric appears in at least one dashboard panel.
2. Every dashboard or alert metric exists in the development scrape.
3. City panels and rules filter/group by `transit_city`.
4. Worker-gone/stalled use `NoData = Alerting`; city-missing uses explicit presence logic.
5. No query, label template, or description contains a prohibited identifier.
