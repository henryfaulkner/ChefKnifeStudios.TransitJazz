# Implementation Plan: Worker Observability

**Branch**: `053-worker-observability` | **Date**: 2026-08-22 | **Spec**: [spec.md](spec.md)

## Summary

Instrument the existing `TransitDataWorker` with bounded, per-city operational metrics.
The deployed worker remains co-hosted in the public Web API Container App (user-selected
option C), subject to a separate constitutional amendment for the co-hosted topology and
worker-artifact requirements. It exports metrics directly to Grafana Cloud over OTLP/HTTP
and exposes no production metrics listener. The existing Parquet sidecar retains
entity-level evidence.

The design adds a domain-owned reporter, records every configured city on every tick, and
evaluates alerts independently per city. It uses explicit missing-series logic because
generic Grafana no-data alerting cannot identify one missing city while others report.

## Technical Context

**Language/Version**: C# / .NET 10.0  
**Primary Dependencies**: `OpenTelemetry.Extensions.Hosting` 1.17.0 and `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.17.0 are explicit host dependencies in `Server.WebAPI` and explicit implementation dependencies where the worker reporter uses them; development-only `OpenTelemetry.Exporter.Prometheus.HttpListener` 1.17.0-beta.1 is explicit in `Server.WebAPI` and its contract tests.  
**Storage**: Grafana Cloud metrics with 14-day operational retention; existing Azure Blob/Parquet structured telemetry is unchanged  
**Testing**: xUnit; `Microsoft.Extensions.Diagnostics.Testing` 10.0.0; `dotnet test`; local Prometheus scrape and asset-binding tests  
**Target Platform**: .NET 10 Linux Container App; local Docker Compose  
**Project Type**: Background worker hosted by the ASP.NET Core Web API  
**Performance Goals**: City health within one 10-second tick; stopped-worker alert within three ticks; 10-second metric export; exporter does not block polling  
**Constraints**: Under 1,000 active series and at least tenfold headroom against the selected service quota; `transit.city` is the sole city attribute; no identifier, route, vehicle, URL, error text, or free-text label; `service.instance.id` is resource-only and excluded from metric dimensions, aggregation, and alert grouping; production has no metrics endpoint; credentials are deployment secrets  
**Scale/Scope**: Seven configured cities and one replica initially, with the maximum replica ceiling treated as a release input; city-attributable health, input, output, reconciliation-outcome, crossing-suppression, duration, and cache signals plus worker-wide liveness and process signals. No synthetic decision score is introduced.

## Resolved Planning Decisions

1. **Co-hosted topology**: `Worker` is registered in `Server.WebAPI/Program.cs`; Bicep deploys
   this public API host only. Keep this topology and add no metrics port, route, or probe.
   The topology remains blocked for production deployment until the separate constitutional
   amendment permitting this co-hosted arrangement is approved and recorded.
2. **Direct Cloud delivery**: Send metrics only to the explicit Grafana Cloud
   `/v1/metrics` endpoint over OTLP/HTTP with an ACA secret-backed, least-privileged Basic
   authorization header. Accept export loss during transport outages; reconsider a collector
   for buffering, redaction, fan-out, or multi-replica batching.
3. **10-second export cadence**: The worker already polls every 10 seconds. A 60-second
   exporter cannot satisfy the specified three-cycle liveness goal. Export every 10 seconds,
   recalculate sample rate, and monitor actual active-series/samples-per-second use.
4. **Canonical city dimension**: Initialize the closed `ITransitCity.Name` set at startup.
   It is the only city attribute and is independent of legacy `EmitsTelemetry`.
5. **City disappearance**: Compare a known-city presence window with the recent
   three-tick presence window. Retain `NoData = Alerting` for totally silent worker rules.
6. **Worker liveness alert semantics**: Keep `WorkerStalled` and `WorkerGone` distinct.
   `WorkerStalled` means a heartbeat exists but is older than three configured intervals;
   `WorkerGone` means the full-cycle counter or series is absent and uses `NoData = Alerting`.
   Both conditions require separate alert-contract tests and runbook explanations.
7. **Truthful input status**: Introduce `CityFetchResult`; valid-record count, source
   timestamp, and fetch outcome distinguish empty-but-successful input from failure.
8. **Transit quality mapping**: Actual reconciliation outcomes and crossing-suppression
   counts are the authoritative quality signals. Do not invent a decision score. Expose
   threshold or rejected-outcome measurements only when real worker configuration provides
   them; otherwise dashboards and tests must show the quality-threshold view as not
   applicable rather than fabricating values.
9. **Series budget**: Calculate the released series set from instrument count, histogram
   buckets, configured cities, label combinations, and maximum replicas. Record the selected
   service quota and require both fewer than 1,000 active series and at least tenfold quota
   headroom as production-release gates.
10. **Instance identity**: Set a unique `service.instance.id` OTel resource attribute for
    each running worker process or replica. Use stable service and environment identity for
    aggregation and alerts; never add the instance ID as a metric label or alert grouping key.
11. **Operational documentation**: Maintain an explicit FR-024 documentation matrix and
    make its completeness a release gate. Governance owns retention, credential rotation,
    external-egress approval, contact-point ownership, and collector-adoption criteria;
    the quickstart owns dashboard identity and local/production isolation; the alert runbook
    owns alert response and deliberate firing; the series-budget document owns the city set,
    quota, and series impact; the release checklist records evidence that every item is
    complete.
12. **Acceptance evidence**: Maintain a release-verification matrix for SC-001, SC-002,
    and SC-007. It must record each scenario, expected signal and operator result, affected
    city where applicable, notification route, severity, repetition count, and evidence
    location. The matrix must prove 5/5 state-identification cases, three consecutive
    stopped-worker rollout tests within three intervals, and 6/6 alert conditions reaching
    their designated routes at the stated severity.
13. **Composite health**: Derive worker-wide and per-city health in dashboard expressions
     from the underlying signals; do not emit a second health gauge. Missing liveness or
     presence is `Unavailable`; a live worker or city with an active error, input, slow-cycle,
     or quality degradation is `Degraded`; otherwise it is `Healthy`. Idle-but-live remains
     healthy, and the individual signals remain visible as the explanation.

## Constitution Check

### Initial Gate — Blocked Pending Prerequisites

| Constitution area | Result | Plan response |
|---|---|---|
| Cloud architecture | Blocked prerequisite | The selected co-hosted Option C topology conflicts with the constitution's independently deployable worker requirement. A separate, explicit amendment must permit this arrangement before production deployment. |
| No frontend secrets | Pass | Endpoint and authorization remain server-side ACA secrets. |
| Two-pass pipeline | Pass | Observation does not alter event payloads, route keys, or publishing. |
| OpenTelemetry backend | Blocked prerequisite | Constitution IV names Azure Log Analytics while the selected strategy uses Grafana Cloud. A separate, explicit Constitution IV amendment must be approved and recorded before production enablement. |
| CI/CD | Blocked prerequisite | The selected topology also omits a separate worker artifact, while the constitution requires a distinct Background Service Docker Image. A separate amendment must address this artifact rule before deployment. |
| Governance | Blocked prerequisite | The constitutional amendments, external telemetry approval, documentation-matrix completion, series-budget evidence, acceptance-evidence matrix, secret rotation, no-listener check, and alert proof are release gates. |

**Gate decision**: The user selected Grafana Cloud and the co-hosted Option C topology.
Design and implementation planning may proceed, but production deployment and enablement
remain blocked until separate, explicit constitutional amendments cover the Grafana backend,
the co-hosted worker topology, and the worker-artifact rule. Those amendments must be
approved and recorded according to the constitution's amendment procedure, external-telemetry
approval must be complete, and the series-budget evidence must pass. This feature plan does
not perform, authorize,
or substitute for those constitutional changes. No Cloud exporter, token, Grafana
deployment, production secret activation, or production topology rollout may occur before
those prerequisites are met.

### Post-Design Gate — Blocked Pending Prerequisites

The design preserves API ingress without using it for metrics, bounds every metric
attribute, and isolates local telemetry. The separate backend, topology, and artifact-rule
amendments, together with external-egress approval, remain mandatory before production
deployment or enablement; the selected design is not itself a production approval.

## Project Structure

```text
specs/053-worker-observability/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── metrics-contract.md
│   ├── monitoring-assets-contract.md
│   └── configuration-contract.md
└── tasks.md                         # generated later

src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/
├── Program.cs                        # actual production host: OTel + reporter DI
└── ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj # explicit host-side OTel packages

src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
├── Metrics/                          # reporter, summaries, options
├── Cities/                            # CityFetchResult migration
├── Worker.cs
└── ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.csproj

src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/
├── WorkerMetricsReporterTests.cs
├── WorkerMetricsLifecycleTests.cs
├── PrometheusScrapeContractTests.cs
└── MonitoringAssetBindingTests.cs

observability/
├── grafana/{dashboards,alerts,terraform,provisioning}/
└── prometheus/prometheus.yml

docker-compose.observability.yml      # local Prometheus + Grafana
bicep/                                # Key Vault/ACA secret refs; no ingress change
```

**Structure Decision**: Instrumentation belongs in the worker project and is wired into
the real production host (`Server.WebAPI`). Grafana assets stay separate so local and
Cloud delivery use the same dashboard source. Option C excludes a separate worker image
or Container App.

## Implementation Sequence

1. Verify the separately approved backend, topology, and artifact-rule constitutional
   amendments together with external-telemetry approval and passing series-budget evidence
   as production prerequisites; keep metrics disabled and the co-hosted deployment unreleased
   until all are complete. T001 may record approval evidence after those separate changes,
   but must not self-authorize or silently perform any constitutional amendment.
2. Add independent `MetricsOptions` and `WorkerOptions`; preserve 10-second polling.
3. Add `CityFetchResult` and adapt all city implementations/fakes for failure, empty
   success, valid count, and source timestamp.
4. Add the sealed reporter, null reporter, summaries, and startup city-series initialization.
5. Extract a testable worker-cycle seam. Report cities outside the legacy Parquet gate,
   city errors in catches, and worker completion in an outer finally.
6. Add explicit host-side OTel package references to the WebAPI project, including the
   development-only Prometheus listener package used by `Program.cs` and local contract
   tests; configure OTel in Web API `Program.cs` with stable service/environment identity
   and a unique resource-only `service.instance.id`; only mirror standalone-worker DI if it
   remains a supported local executable.
7. Add instrument, lifecycle, scrape, and asset-binding tests before cloud delivery,
   including configured and unconfigured quality-threshold behavior, preservation of
   actual reconciliation outcomes, distinct `WorkerStalled`/`WorkerGone` no-data behavior,
   and multi-instance resource identity without instance-based aggregation.
8. Add development-only Prometheus/Grafana assets and verify production listener rejection.
   Local alert provisioning owns alerts only, while Prometheus scrape and dashboard
   provisioning owns scrape and dashboard setup. Both consume the canonical dashboard and
   alert assets created earlier; no local provisioning task may duplicate alert ownership.
9. Add Grafana Cloud dashboard, alert, contact point, and routing provisioning with separate
   publisher and provisioning secrets.
10. Extend Bicep with Key Vault/ACA secret refs and metrics config; retain existing API ingress
    and use only process probes, never `/metrics`.
11. Calculate the exact series cost from instrument count, label combinations, histogram
    buckets, configured cities, and maximum replica ceiling; record the selected service
    quota and tenfold-headroom calculation in `docs/observability/worker-series-budget.md`,
    add usage panels, and make the two budget limits release gates.
12. Prove distinct stalled-worker and missing-worker behavior, one-city-missing,
    all-cities-missing, error, slow, input, token rotation, and shutdown-flush cases before
    release.
13. Complete the FR-024 documentation matrix across `worker-governance.md`, `quickstart.md`,
    `worker-alert-runbook.md`, `worker-series-budget.md`, and `worker-release-checklist.md`,
    then record the evidence in the release checklist.
14. Complete the SC-001/SC-002/SC-007 verification matrix in
    `docs/observability/worker-release-checklist.md`, including the exact case counts,
    three consecutive stopped-worker rollout results, notification routes, severities, and
    linked evidence before production release.
15. Define and test the worker-wide and per-city composite-health dashboard expressions,
     including unavailable, degraded, healthy, and idle-but-live cases, without introducing
     a duplicate emitted health instrument.

### Provisioning Ownership

Canonical dashboard and alert assets are created before local provisioning. The local alert
provisioning task owns alert rules; the Prometheus/dashboard provisioning task owns scrape
configuration and dashboard setup. Their task dependencies must reflect this order rather
than treating consumers as parallel with the assets they consume.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Grafana Cloud differs from Constitution IV | The user selected Grafana Cloud and the source strategy requires hosted Grafana metrics/alerting. | Azure Log Analytics rejects the selected backend and asset model; a separately approved amendment remains mandatory before production enablement. |
| No separate no-ingress worker | The user selected Option C co-hosting and will pursue a separate constitutional amendment for the topology and artifact rules. | Splitting adds image, workflow, Bicep app, identity, secrets, and cutover outside the chosen option. |
| Development-only Prometheus listener | It locks translated metric names and tests the same dashboards locally. | A production listener adds an unnecessary inbound surface; Cloud has no file provisioning. |
