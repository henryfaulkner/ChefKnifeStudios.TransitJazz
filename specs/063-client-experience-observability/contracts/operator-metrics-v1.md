# Operator Metrics and Alert Contract v1

**Status**: Proposed. [Spec](../spec.md) | [Data model](../data-model.md) | [Source design](../../../docs/CLIENT_TOAST_OBSERVABILITY_DESIGN_PLAN.md).

## Shared metric definitions

Dashboards and SQL alerts consume the same reviewed attempt/window/episode/presentation views. Environment is server-assigned. Operational cohorts use attempt-start **server receipt time**; event-volume panels use receipt time; trusted historical occurrence views have a separate explicit time basis. Filter comparisons by environment, city, release, measurement version, and time basis. Unmatched results are coverage diagnostics, not attempts in primary rates.

For a start cohort, `S = succeeded`, `F = failed`, `D = deadline_exceeded`, `U = unresolved`, `X = cancelled or superseded`; eligible denominator `N = S + F + D + U`. Display `S/N`, `(F+D)/N`, `U/N`, `X`, `N`, affected page-session count, and provisional status together. A resolved-only `S/(S+F+D)` may be shown if explicitly labeled. `N=0` is `N/A`; missing terminal events remain unresolved. A fresh cohort is provisional during the seven-day correction horizon.

For each capability, display `healthy_ms / eligible_observed_ms` together with degraded, unavailable, pending, and unknown portions, deliberate exclusions, and coverage gaps. Capabilities overlap, so do not sum them into one availability percentage. Recovery duration is observed eligible time from confirmed degradation to verified healthy in the same episode; incomplete episodes are a separate count. Receipt-time backlog is paired with delivery delay. A valid empty count bucket is zero; a zero denominator is unevaluated; query error or unexpected no data is a collection incident.

Daily aggregates contain count and duration sums, mergeable fixed latency buckets, and daily affected/observed page-session counts. Calculate multi-day rates from summed numerators/denominators. Histograms produce labeled approximate percentiles; daily distinct sessions are session-days, never monthly unique visitors. Release comparisons require the same measurement version and time basis.

## Dashboard deliverable

Deliver `observability/grafana/dashboards/transitjazz-client-experience.json` with configurable PostgreSQL source UID and UTC filtering. Default to receipt-time volume and matched start cohorts. Keep experience health, visitor feedback, and collection health visibly separate.

| Section | Required panels |
| --- | --- |
| Journeys | Bootstrap/city/live/audio starts and result shares, exclusions, sample sizes, provisional label; p50/p95 successful foreground readiness with bad/deadline counts and wall elapsed context; settings effect/persistence, stop, and navigation actions. |
| Capability health | Per-capability eligible state durations, excluded and gap time, recovery distribution and incomplete episodes; live freshness as diagnostic/unknown without cadence contract. |
| Feedback | Count committed warning/error presentations by displayed severity, issue/capability and release; show linked owner episodes where known, and avoid summing overlapping issue classifications. |
| Quality and collection | Delivery delay/skew, sequence gaps, orphan/conflicting attempts, invalid/overlapping windows, local loss reports, denominator coverage, ingestion/rejection/latency, probe/synthetic status, aggregate watermark/lag and cleanup. |
| Trends and investigation | Recent safe attempt/episode/presentation evidence within raw retention; long-term daily weighted rates and approximate latency within aggregate retention; affected page-session counts with correct time basis. |

Use reviewed SQL views and fixed identifier choices. Dashboard variables use safe SQL string formatting for values. Verify datasource UID/grants at rollout; the repository README documents `cg086lcj6z6yob` but that is not proof of current production access.

## Initial alert rules

Deliver `observability/grafana/alerts/transitjazz-client-experience-alerts.json` and `docs/observability/client-experience-runbook.md`. Thresholds are proposed starting points to baseline in staging, not formal SLOs. Group by environment/city and bounded journey/capability, never event/session/attempt/release IDs. SQL rules must return sorted time-series data with numeric values and explicit denominator/coverage series; no dashboard variables or panel transformations in alert queries.

| Rule | Initial condition | Evaluation |
| --- | --- | --- |
| Journey failure elevated | `(F+D)/N >= 10%`; `N >= 20`, at least three affected page sessions in 15-minute start cohort ending two minutes ago | Every minute, three-minute pending |
| Sustained capability degradation | Degraded + unavailable >=10% of eligible observed time; at least 10 observed session-minutes and three affected sessions in 10 minutes | Every minute, three-minute pending |
| Confirmed audio stop failed | At least one fresh confirmed `still_playing` stop outcome; presentation irrelevant | Every minute, immediate |
| Bootstrap/city synthetic failing | Independent controlled browser fails readiness on two consecutive runs | Every five minutes; runner failures separate |
| Telemetry ingestion failing | Five persistence failures in five minutes | Every minute, two-minute pending |
| Collection unavailable | Ingest/database probe or dashboard query path fails | Every minute, two-minute pending |
| Trend maintenance stalled | Watermark lag above two hours or repeated cleanup failure | Every five minutes, ten-minute pending |
| Toast volume elevated | At least 10 errors from three sessions in five minutes, or 30 warnings from five sessions in ten minutes | Diagnostic/advisory initially, not primary paging |

For journey rate rules, `U/N > 10%` suppresses the health conclusion and raises a coverage advisory. For duration rules, unknown plus gap time above 10% of the eligible observation span does likewise. Require recent trustworthy observations: initially non-skewed with delivery delay at most two minutes for both starts and results; retain starts whose results are missing or stale as unresolved. Delayed backlog updates history and diagnostics, not a current outage. Duration alerts use validated occurrence intervals fully inside the evaluation window, excluding partial boundaries and reporting their excluded coverage. Fresh urgent stop failure alerts at low traffic; delayed stop failure is historical evidence.

Use configured cities plus `unknown` as stable result series and return explicit zero counts when the query is valid and observations are adequate. Do not emit a `Normal` health state from zero denominator, query failure, missing series, or insufficient coverage. Configure no-data and missing-series behavior, a separate collection route, and recovery only after an adequately observed healthy window. Set a sensible repeat interval (initially one hour), link the dashboard/runbook, reuse the operator's existing contact point, and demonstrate firing, routing, and recovery in staging before operational routing. Grafana's PostgreSQL source requires time-series results for alerts ([official data-source documentation](https://grafana.com/docs/grafana/latest/datasources/postgres/)); Grafana can mark missing label series resolved unless configured and tested ([missing data documentation](https://grafana.com/docs/grafana/latest/alerting/guides/missing-data/)).

## Independent coverage

- Bounded ingest/database probe checks intake availability and storage without inserting fake visitor events.
- A Grafana Cloud Synthetic Monitoring k6 browser check runs every five minutes for shell startup, current-city routes/map, and confirmed live join. It checks the application directly, not only the client telemetry pipeline. Its runner-held credential redirects client-experience requests to a dedicated server-controlled synthetic intake/environment, outside real-session denominators; a browser-supplied flag alone is insufficient. Verify the tenant's probe access and cost before provisioning.
- Controlled staging feed validates processing/rendering during quiet-feed periods. Supported-browser gesture/context/voice checks validate audio readiness without claiming physical audibility.
- A visit that never executes the bootstrap script is outside browser report coverage; synthetic checks are the independent detection path. A page that exits before local persistence, cleared storage, and a visitor who never returns after an outage are stated collection limits.
