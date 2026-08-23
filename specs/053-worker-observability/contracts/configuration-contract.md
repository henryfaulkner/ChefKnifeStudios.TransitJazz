# Configuration and Deployment Contract

| Key | Production | Development | Notes |
|---|---|---|---|
| `Worker__CycleIntervalSeconds` | `10` | `10` | Source of liveness threshold. |
| `Metrics__Enabled` | true only after governance gates | true for local observability | Independent metrics kill switch. |
| `Metrics__ExportIntervalMilliseconds` | `10000` | `10000` | Cannot exceed worker cycle interval. |
| `Metrics__OtlpMetricsEndpoint` | ACA secret-backed configuration | unset | Explicit Cloud path ending `/v1/metrics`. |
| `Metrics__OtlpAuthorization` | ACA secret reference | unset | Basic authorization; never source controlled. |
| `Metrics__ServiceName` | `transitjazz-transit-worker` | same | Stable aggregation identity. |
| `Metrics__Environment` | deployment value | `development` | Stable environment identity. |
| `Metrics__LocalPrometheusEnabled` | false | true only for local stack | Production validation rejects true. |

- Existing API ingress remains for API/SignalR only; metrics add no port, route, probe, or ingress dependency.
- Bicep uses Key Vault-backed ACA secret references. Neither Bicep, Dockerfiles, parameters, nor appsettings contains credentials.
- Publisher and Grafana provisioning tokens are separate and least privileged.
- Production enablement requires Constitution IV amendment, external-egress approval, active-series calculation, contact-point test, stopped-worker page, and token-rotation test.
- Collector adoption is reviewed when buffering, redaction, shared traces/logs, or replica batching becomes necessary.
