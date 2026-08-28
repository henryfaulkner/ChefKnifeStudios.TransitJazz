# Worker metrics series budget

Configured city set: atlanta, washington-dc, boston, new-york-city, toronto, philadelphia, denver (7). Maximum replicas: 1.

| Component | Calculation | Series |
| --- | --- | ---: |
| Worker gauges/counters | 11 worker instruments × 1 replica | 11 |
| Worker histogram | 1 histogram × 12 exported series | 12 |
| City gauges/counters | 24 instruments × 7 cities × 1 replica | 168 |
| City histogram | 1 histogram × 12 exported series × 7 cities | 84 |
| **Total** | bounded metric set | **275** |

Grafana Cloud quota selected for release: 10,000 active series. The 275-series estimate is below the 1,000 internal limit and leaves more than tenfold quota headroom (10,000 / 275 = 36.4). A city-set or replica-ceiling change must recalculate this table before release. Monitor `grafanacloud_instance_metrics_usage_active_series` and samples-per-second after every deployment.
