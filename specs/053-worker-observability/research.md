# Research: Worker Observability

## Direct Grafana Cloud metrics export

**Decision**: Use direct OTLP/HTTP metrics export from the existing Web API host. Configure an explicit stack-specific `/v1/metrics` endpoint, HTTP/protobuf transport, and a Basic authorization header built from Grafana Cloud instance ID plus a metrics-publisher token.

**Rationale**: One low-volume worker and a near-zero-operations goal do not justify a collector now. Direct export accepts loss during a transport outage. Reconsider Alloy or a collector for buffering, routing, redaction, shared traces/logs, or more replicas.

**Alternatives considered**: Production Prometheus pull adds an endpoint and scraper; Azure Log Analytics conflicts with the selected strategy; a collector is deferred.

Sources: [Grafana OTLP architecture](https://grafana.com/docs/grafana-cloud/observe-and-act/send-data/otlp/send-data-otlp/), [Grafana .NET setup](https://grafana.com/docs/opentelemetry/instrument/grafana-dotnet/), [OpenTelemetry .NET exporter](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md).

## Liveness and missing-city alerting

**Decision**: Export a global heartbeat and a city heartbeat every tick. Use `NoData = Alerting` for worker-gone rules and compare historical versus recent city presence for city-missing rules. Initialize every configured city series at startup.

**Rationale**: Generic no-data applies when a query has no series. If Atlanta disappears while Boston reports, Grafana treats Atlanta as missing rather than alerting. A `present_over_time` known-city window compared with a recent three-tick window emits a city-labelled alert and retains a separate global no-data rule.

**Alternatives considered**: Worker-wide aggregation masks city degradation; no-data-only rules fail the single-city disappearance scenario.

Sources: [Grafana missing-data guidance](https://grafana.com/docs/grafana/latest/alerting/guides/missing-data/), [No Data and Error states](https://grafana.com/docs/grafana/latest/alerting/fundamentals/alert-rule-evaluation/nodata-and-error-states/).

## Export cadence and capacity

**Decision**: Export every 10 seconds and require the interval to be no greater than the worker cycle interval.

**Rationale**: The current worker ticks every 10 seconds, so the three-cycle liveness outcome is 30 seconds. A 60-second exporter cannot meet it. At the source strategy's rough 250-series estimate, 10-second export is about 2.16 million samples per day. Keep the internal limit below 1,000 active series against the current 10,000-series free allowance and inspect real active-series and samples-per-second usage after rollout.

**Alternatives considered**: 60-second export breaks the liveness objective; slowing transit polling changes live-map behavior and is out of scope.

Sources: [Grafana pricing](https://grafana.com/pricing/?tab=free), [active-series and DPM definitions](https://grafana.com/docs/grafana-cloud/platform/pricing-and-usage/metrics/), [cardinality management](https://grafana.com/docs/grafana-cloud/platform/cost-management-and-billing/analyze-costs/metrics-costs/prometheus-metrics-costs/cardinality-management/).

## Instrumentation and testing

**Decision**: Inject `IMeterFactory` into a sealed reporter, create instruments once, and test them with `MetricCollector<T>` from `Microsoft.Extensions.Diagnostics.Testing`.

**Rationale**: Factory-created meters work with DI and isolated test collections. The reporter keeps OpenTelemetry out of `Worker`. Use counters, .NET 10 gauges, and histograms; omit `_total` from OTel counter names because Prometheus translation supplies it.

**Alternatives considered**: Static instruments impede disabling and isolated testing; `prometheus-net` does not serve the selected direct-push production path.

Sources: [.NET metrics instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation), [MetricCollector API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.metrics.testing.metriccollector-1.getmeasurementsnapshot), [Prometheus compatibility](https://opentelemetry.io/docs/compatibility/prometheus/client-libraries/).

## City truthfulness and source assets

**Decision**: Add `CityFetchResult` and use only `ITransitCity.Name` as a metric attribute. Keep heap/working set and logging-sidecar state worker-wide. Store dashboards and alerts in the repository, provision local Grafana from files, and deploy Cloud assets with Grafana Terraform resources.

**Rationale**: Current fetchers can turn failures into empty feeds, so count alone is ambiguous. Process resource readings are sampled once per full tick and cannot truthfully belong to a city. Grafana Cloud does not support filesystem provisioning; Terraform supports dashboards, rules, contact points, and notification policy.

**Alternatives considered**: `EmitsTelemetry` is a legacy Parquet gate and hides configured development cities. Route, vehicle, URL, and exception attributes violate cardinality and privacy constraints. Manual Cloud UI changes cannot meet source-of-truth requirements.

Sources: [Grafana Terraform dashboards](https://grafana.com/docs/grafana/latest/as-code/infrastructure-as-code/terraform/dashboards-github-action/), [Terraform alerting](https://grafana.com/docs/grafana/latest/alerting/set-up/provision-alerting-resources/terraform-provisioning/), [Cloud provisioning limits](https://grafana.com/docs/grafana/latest/alerting/set-up/provision-alerting-resources/file-provisioning/).
