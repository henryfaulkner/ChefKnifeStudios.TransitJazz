# Worker observability release checklist

| Gate | Evidence | Status |
| --- | --- | --- |
| FR-024 governance, 14-day retention, credential rotation, contact points, collector criteria | worker-governance.md | Ready |
| Dashboard identity, local isolation, co-hosted ingress exception | quickstart.md | Ready |
| City set, quota, and series impact | worker-series-budget.md | Ready |
| SC-001: stopped, idle, input-starved, failed, working (5/5) | controlled rollout evidence | Pending production drill |
| SC-002: three stopped-worker runs within three intervals | controlled rollout evidence | Pending production drill |
| SC-007: WorkerStalled, WorkerGone, CityMissing, input, error, slow/lag routes | notification evidence (6/6 designated routes) | Pending production drill |

Production enablement requires every pending drill row to contain its affected city, alert severity, notification route, repetition count, and evidence location.
