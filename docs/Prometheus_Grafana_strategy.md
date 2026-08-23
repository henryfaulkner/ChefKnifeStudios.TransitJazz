# Core Strategy Summary: Prometheus + Grafana for a Green-Field Data Worker

## 1. Intent and desired outcome

The goal is to give a new periodic data worker reliable, low-cost observability without importing MyGeotab code or operating monitoring infrastructure.

The design should make it possible to answer, quickly and unambiguously:

1. **Is the worker alive?**
2. **Is input data arriving?**
3. **Is the worker doing useful work?**
4. **Are its decisions or outputs good?**
5. **Is it fast and resource-efficient?**
6. **Has it failed recently?**

The desired outcome is a service that can be diagnosed from dashboards and alerts without reading logs first, while preserving structured logs for entity-level forensic investigation. It must detect the difference between:

- a dead worker;
- a healthy but idle worker;
- a worker receiving no input;
- a worker processing data but producing poor decisions;
- a worker experiencing errors or resource pressure.

The implementation should be inexpensive to run, secure by default, portable, testable, and maintainable by keeping all observability decisions in source control.

---

## 2. Non-negotiable constraints

The target service:

- Runs outside the MyGeotab solution.
- May not reuse MyGeotab code or shared libraries.
- Runs on Azure Container Apps.
- Initially uses one replica, with minimum and maximum replicas set to one.
- Uses open-source dependencies only.
- Has a near-zero operating-cost requirement.
- Must not expose unnecessary inbound network surfaces.
- Must keep telemetry free of customer identifiers, entity IDs, free text, and other high-cardinality data.

AssetCoupling is used only as a design reference. Its instrumentation patterns transfer, but its implementation, libraries, multi-tenant handling, and hosting topology do not.

---

## 3. Core architectural model: two telemetry tracks

The system separates observability into two independent tracks.

### Track A — Metrics

Metrics provide bounded, near-real-time aggregate signals:

- health;
- liveness;
- throughput;
- latency;
- saturation;
- data freshness;
- decision distributions;
- resource usage.

Metrics are intended for dashboards, alerting, and trend analysis.

### Track B — Structured logs

Structured logs retain detailed, per-cycle or per-entity information:

- individual decisions;
- entity identifiers;
- reasons;
- correlation IDs;
- detailed failures;
- forensic context.

The key rule is:

> Entity identifiers belong in structured logs, not metric attributes.

Metric cardinality must remain bounded. Metrics should explain how much work occurred and how healthy the system is; logs should explain exactly what happened to a particular entity.

---

## 4. Production architecture decision

### Recommended architecture

The worker emits metrics directly to Grafana Cloud using OpenTelemetry over OTLP/HTTP.

```text
Azure Container App
  .NET worker loop
    Domain metrics interface
      OpenTelemetry Meter
        OTLP exporter
          HTTPS outbound
            Grafana Cloud Free
              Prometheus-compatible storage
              Grafana dashboards
              Grafana-managed alerts
```

Production deliberately has:

- no Prometheus server;
- no Prometheus TSDB;
- no monitoring VM;
- no persistent metrics volume;
- no Alertmanager;
- no inbound metrics endpoint;
- no Kubernetes `ServiceMonitor`;
- no sidecar collector initially.

### Why push instead of pull

A pull-based model would require a Prometheus server to reach the ACA worker. That would introduce:

- a monitoring host or service;
- storage requirements;
- networking and ingress requirements;
- persistent-volume concerns;
- operational maintenance;
- series aliasing risks if ACA replicas increase.

The push model removes those dependencies. Each replica sends its own telemetry and receives a unique `service.instance.id`.

The design therefore no longer depends on `maxReplicas: 1` for correctness. Scaling out would not cause multiple replicas to be silently represented as one scraped target.

### New push-model limitation

Push removes the Prometheus-generated `up` metric.

If the worker dies, it stops sending metrics. Grafana may see no data, which can look identical to a healthy but idle worker unless liveness is designed explicitly.

Therefore:

- the heartbeat timestamp is the primary liveness signal;
- liveness alerts must use **No Data = Alerting**;
- stopping the worker must be tested as an explicit rollout exercise.

---

## 5. Instrumentation library decision

### Use OpenTelemetry for .NET

OpenTelemetry is mandatory for the production design because it:

- natively exports OTLP to Grafana Cloud;
- requires no collector for the initial deployment;
- supports resource attributes;
- is vendor-neutral;
- can later support traces and logs;
- avoids adding a sidecar solely to bridge Prometheus exposition to OTLP.

`prometheus-net` is rejected for production because it does not directly provide the required push path. Using it would require a second container running Grafana Alloy or an OpenTelemetry Collector to scrape the worker and forward the data.

### Direct SDK export initially

Start with direct SDK export because:

- there is one worker;
- telemetry volume is low;
- occasional loss during a network outage is acceptable for a periodic worker;
- the SDK retries transient failures during the export interval;
- it avoids another container and configuration surface.

Introduce a Grafana Alloy sidecar later if the service requires:

- buffering during extended network outages;
- shared traces and logs;
- richer enrichment or redaction;
- more replicas with stronger per-replica batching requirements.

### Resource identity

Configure resource attributes once at startup:

- `service.name`;
- `service.version`;
- `service.instance.id`;
- `deployment.environment`.

`service.instance.id` is useful for distinguishing replicas, but it changes across ACA revision deployments. Dashboards and alerts must not depend on it being stable. Use stable dimensions such as `service.name` and `deployment.environment` for aggregation.

---

## 6. Metrics backend and cost decision

### Recommended backend: Grafana Cloud Free

The document estimates approximately:

- 250 active series;
- 60-second export interval;
- 360,000 samples per day;
- 10.8 million samples per month;
- approximately 2.5% of the 10,000-series free-tier allowance.

Grafana Cloud Free provides:

- no infrastructure to operate;
- Prometheus-compatible metrics storage;
- Grafana dashboards;
- Grafana-managed alerting;
- 14-day retention;
- up to three users.

### Rejected or conditional alternatives

| Option | Decision |
|---|---|
| Grafana Cloud Free | Recommended |
| Azure Monitor plus Azure Managed Grafana | Rejected for near-zero cost; Grafana is the dominant expense |
| Azure Monitor plus Grafana Cloud as the UI | Viable if Azure-native retention or residency becomes necessary |
| Self-hosted VM | Rejected because of infrastructure, maintenance, storage, and operational burden |

The important conclusion is that self-hosting is not near-zero once the operational surface is included.

### Accepted trade-offs

1. **Fourteen-day retention**  
   It is sufficient for alerting and incident triage but not long-term regression analysis. Preserve important baselines in release notes, snapshot key panels, or move storage to Azure Monitor if longer retention becomes essential.

2. **Telemetry leaves the Azure/Geotab boundary**  
   Data-governance approval is required before production. Metrics must contain operational information only, with no customer or entity identifiers.

3. **Three-user limit**  
   Use shared dashboards and team alerting rather than provisioning individual users unnecessarily.

---

## 7. Cardinality and export policy

Cardinality is treated as both a cost control and a data-governance control.

Rules:

- Target fewer than 1,000 active series.
- Maintain at least 10× headroom against the free-tier quota.
- Use no high-cardinality attributes.
- Do not include entity IDs, customer IDs, filenames, URLs, exception text, or free text.
- Allow at most one metric attribute, and only when it is a bounded enum.
- Do not add a tenant attribute unless the worker genuinely has bounded partitions.
- If a partition attribute is necessary, document every allowed value and recalculate the series budget.
- Add an active-series usage panel to the dashboard.

The export interval is the primary cost dial. Use:

```text
OTEL_METRIC_EXPORT_INTERVAL=60000
```

A 60-second interval is preferred over 15 seconds because the worker cycles in minutes and higher resolution adds cost without useful visibility.

---

## 8. Reporter abstraction and dependency structure

The worker must not reference OpenTelemetry types directly in its domain logic.

Use a domain-shaped interface:

```text
IWorkerMetricsReporter
WorkerMetricsReporter
ThresholdSnapshot
```

The interface has three operations:

1. `ReportCycle(...)`  
   Called once after a successful cycle body. Emits work, throughput, quality, duration, input, allocation, and current-state metrics.

2. `ReportCycleCompleted(...)`  
   Called from `finally` for every cycle, including idle cycles and cycles that fail. Emits the heartbeat, cycle counter, configured interval, and work timestamp when applicable.

3. `ReportCycleError()`  
   Called from the exception path. Increments the cycle error counter.

The reporter implementation:

- is `sealed`;
- is the only component that references OpenTelemetry;
- creates all instruments in its constructor;
- stores instruments in `readonly` fields;
- receives an `IMeterFactory`;
- is registered through dependency injection at the composition root;
- has no metric creation or name lookup on the hot path;
- uses explicit OpenTelemetry package references in the project file;
- uses `IWorkerMetricsReporter?` and null-conditional calls as the off-switch.

### Metrics enablement

Use one configuration value:

```text
Metrics__Enabled
```

Read it once at startup. If disabled, register a null reporter. There is no need to build MyGeotab-style runtime feature-flag infrastructure for this single-tenant service.

Changing the setting requires an ACA revision deployment, which is acceptable at this scale.

---

## 9. Canonical metric surface

Metric design follows six questions.

### 1. Liveness

- `myworker_last_cycled_timestamp_seconds` — updated every cycle.
- `myworker_last_worked_timestamp_seconds` — updated only when work occurs.
- `myworker_cycles_total` — includes idle cycles.
- `myworker_cycle_interval_seconds` — publishes the configured interval.

The interval metric is important because dashboards and alerts can use relative thresholds such as `2 × interval` instead of hardcoded values.

### 2. Input freshness

- `myworker_input_records_valid`;
- `myworker_has_input_records`;
- `myworker_input_lag_seconds`.

The input-present metric is a binary 0/1 signal intended for health expressions. Unknown input lag must be explicitly reset to zero every cycle; otherwise the previous healthy value remains visible during an outage.

### 3. Work and state

- `myworker_items_processed` with one bounded classification attribute if needed;
- one counter per business outcome, such as `<outcome>_total`;
- `myworker_active_<entity>_count`, read from the source of truth.

Current state must not be derived by subtracting counters. Counters reset on restart and do not represent state that existed before the process started.

### 4. Decision quality

- `myworker_decision_score` histogram;
- `myworker_below_threshold_rejections`;
- one `_config_*` gauge for every decision threshold used in dashboards or alerts.

Histogram buckets must align with actual business thresholds so Grafana can display exact tiers instead of interpolated ranges. AssetCoupling uses boundaries such as 0.31, 0.50, and 0.70; the generic worker example uses equivalent threshold-aligned values. The implementation must use the worker's real configured thresholds.

### 5. Cost and saturation

- `myworker_cycle_duration_seconds` histogram;
- `myworker_cycle_allocated_bytes`;
- queue and cache size gauges;
- eviction or cleanup counters.

Duration buckets should reflect operational thresholds rather than a generic default ladder. The reference boundaries are:

```text
0.5, 1, 2, 5, 10, 20, 30, 60, 120, 300 seconds
```

Allocation measures bytes allocated during the cycle using the difference in `GC.GetTotalAllocatedBytes`. It measures allocation pressure, not live heap size.

### 6. Failure

- `myworker_cycle_errors_total`.

This counter is incremented in the catch block. It exposes failures that may otherwise leave other metrics looking normal.

### Naming rules

- Use one service prefix.
- Use OTel dot-style instrument names internally.
- Expect Prometheus ingestion to expose snake_case names.
- Use base units in names: `_seconds`, `_bytes`, `_count`, `_percent`.
- Counters end in `_total`.
- Descriptions must explain what is measured, when it is written, and what zero means.
- Do not use milliseconds or megabytes as stored units.

---

## 10. Emission discipline

The implementation must enforce the following rules:

1. Build attribute/tag sets once per cycle, not once per observation.
2. Set immutable configuration gauges once rather than on every cycle.
3. Initialize counters and every bounded attribute permutation at startup so panels do not see missing series as zero or No Data.
4. Write every gauge every cycle, including explicit resets for unknown values.
5. Bound all in-cycle collections feeding histograms; record truncation in structured logs.
6. Place work reporting in `try`, errors in `catch`, and heartbeat reporting in `finally`.
7. Compute metrics and structured logs from the same aggregate cycle object.
8. Flush telemetry during shutdown so the final cycle before an ACA revision swap is exported.

The required cycle structure is:

```text
start allocation measurement
resolve metrics reporter once

try
  try
    execute cycle
    create aggregate summary
    write structured log
    report metrics
  catch non-cancellation exception
    report cycle error
    log exception
finally
  report cycle completion / heartbeat
```

This structure ensures that a failed cycle still proves the worker is alive, while an entirely dead worker eventually produces no heartbeat data.

---

## 11. ACA and network surface

Production has no metrics endpoint and no ingress:

```text
ingress: disabled
```

ACA probes must test process liveness only:

- TCP probe;
- exec probe;
- or a minimal `/healthz` endpoint on a directly probed container port.

Do not use `/metrics` as the health probe.

The worker being alive is not the same as the worker doing useful work. ACA probes handle process availability; heartbeat and input metrics handle application behavior.

For development only, optionally enable the OpenTelemetry Prometheus exporter and expose `/metrics`. This allows local inspection and makes it possible to run a local Prometheus/Grafana stack.

---

## 12. Dashboards and alerting

### Dashboard ownership

The dashboard JSON is the source of truth and remains in git. Grafana is treated as a rendering and execution surface.

Use:

- a stable, hand-written UID such as `myworker-overview`;
- a one-minute refresh matching the 60-second export interval;
- a default time range of the last six hours;
- an environment variable only if one stack contains multiple environments;
- no tenant variable for the single-tenant worker.

The dashboard rows follow triage order:

1. Health
2. Work
3. Input
4. Resources

Every emitted metric must appear on at least one panel.

Every panel description must explain:

- what the metric means;
- what healthy behavior looks like;
- what to inspect next if it is abnormal.

### Composite health

Health is represented as 1 or 0 by multiplying boolean signals:

```text
fresh cycle heartbeat
× fresh work heartbeat
× no recent errors
```

PromQL uses `== bool` to convert comparisons into 0/1 values. Multiplication acts as logical AND.

The composite health panel is paired with a signal-breakdown panel showing each component separately. The composite is for alerting and triage; the breakdown explains why health is bad.

### Histogram visualization

Decision histograms are decomposed into named business tiers using cumulative bucket subtraction. Rejected-below-threshold counts are added separately so the visualization includes the entire evaluated population, not only accepted candidates.

A second panel overlays:

- configured threshold gauges;
- p5, p50, and p95 observed scores.

This supports tuning and behavioral analysis, not just alerting.

### Alert rules

Use Grafana-managed alert rules. Required alerts include:

- **WorkerStalled** — heartbeat older than three cycle intervals; critical; No Data = Alerting.
- **WorkerGone** — missing cycle counter; critical; No Data = Alerting.
- **WorkerInputStopped** — no input for 15 minutes; warning.
- **WorkerCycleErrors** — any errors in the last 15 minutes; warning.
- **WorkerCycleSlow** — p95 duration above 30 seconds for 15 minutes; warning.
- **WorkerInputLagHigh** — lag above 900 seconds for 10 minutes; warning.

Any alert reading `input_lag_seconds` must also require `input_lag_seconds > 0`, because zero means unknown/no valid timestamp rather than zero delay.

Contact points should route to Teams or PagerDuty and must be tested by deliberately firing an alert.

The most important rollout test is:

> Stop the worker and verify that a page is actually received.

---

## 13. Testing strategy

Three test layers are required.

### Layer 1 — instrument contract tests

Using an in-memory `IMeterFactory`, verify:

- every expected instrument exists;
- every value is correctly wired;
- the work timestamp is absent for idle cycles;
- unknown gauges reset to zero;
- counters and attribute permutations are initialized;
- Prometheus-facing names render correctly when the Prometheus exporter is enabled.

### Layer 2 — worker interaction tests

Drive real cycle behavior with a mocked reporter and verify:

- metrics disabled → no reporter calls;
- metrics enabled → completion called once;
- exception → error called once and completion still called once;
- idle cycle → completion called with `didWork = false`.

### Layer 3 — dashboard and alert binding tests

Parse the dashboard JSON, alert definitions, and a test scrape. Assert that:

- every dashboard metric exists in the emitted scrape;
- every alert metric exists;
- every emitted metric is represented by at least one dashboard panel;
- every liveness rule uses `No Data = Alerting`.

This layer prevents the reference implementation's known failures, including renamed metrics, missing panels, inconsistent queries, and untested alert wiring.

---

## 14. Local development

Do not send developer telemetry to the production Grafana Cloud stack.

Use:

- development-only Prometheus exposition;
- local OSS Prometheus;
- local OSS Grafana;
- Docker Compose;
- the same dashboard JSON used in production.

The local stack should mount the dashboard directly from the repository so local behavior closely matches Grafana Cloud.

If the real OTLP path must be tested, use a local collector or a separate personal Grafana Cloud stack.

---

## 15. Security, governance, and operations

- Store the Grafana token in Key Vault and expose it to ACA through a secret reference.
- Never place credentials in the image, source control, or Bicep/Terraform literals.
- Use a metrics-publisher-scoped token, not an administrator token.
- Rotate the token periodically and test the worker afterward.
- Obtain explicit approval for telemetry leaving Azure/Geotab.
- Document the 14-day retention limit and mitigation in `CONTEXT.md`.
- Add dashboard UID and alert runbook links to operational documentation.
- Keep alert definitions, dashboards, infrastructure, and observability code in source control.
- Use an ADR documenting the OpenTelemetry decision and when it should be reconsidered.

---

## 16. Implementation sequence

1. Record the OpenTelemetry decision, metric design, cardinality calculation, and governance approval.
2. Implement the reporter interface, OpenTelemetry reporter, resource configuration, and unit tests.
3. Add the local Prometheus/Grafana development stack.
4. Create the dashboard and dashboard/metric binding test.
5. Create the Grafana Cloud stack and configure the Key Vault-backed token.
6. Enable OTLP export and verify metrics appear within minutes.
7. Import the dashboard and add the active-series usage panel.
8. Configure alerts and contact points.
9. Stop the worker deliberately and verify liveness paging.
10. Disable ACA ingress, configure process probes, and verify shutdown flushing.
11. Test token rotation and document retention and operational limitations.

---

## Strategic conclusion

The recommended strategy is:

> Use a small domain-owned reporter abstraction backed by OpenTelemetry, emit bounded aggregate metrics directly over OTLP/HTTP to Grafana Cloud Free, keep detailed entity-level evidence in structured logs, disable production ingress, and make heartbeat plus No Data alerting the foundation of liveness.

This produces a near-zero-cost observability system with:

- no monitoring infrastructure to operate;
- no network-reachable metrics endpoint;
- portable vendor-neutral instrumentation;
- bounded and auditable telemetry;
- dashboards and alerts stored as code;
- explicit support for health, freshness, throughput, quality, cost, and failure;
- tests that validate both emitted metrics and their dashboard/alert consumers.