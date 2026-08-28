# Worker Observability Governance

## Production-enable gate

| Prerequisite | Status | Evidence |
| --- | --- | --- |
| Constitution IV amendment permitting Grafana Cloud metrics | Approved | Project owner approval recorded 2026-08-22. |
| Co-hosted worker topology amendment | Approved | Project owner approval recorded 2026-08-22. |
| Worker-artifact rule amendment | Approved | Project owner approval recorded 2026-08-22. |
| External operational-telemetry egress approval | Approved | Project owner approval recorded 2026-08-22. |

Production metrics remain disabled until all rows are approved, the series-budget gate passes,
and the release checklist has the required alert and acceptance evidence. The existing API
ingress is not used for metrics; production telemetry is outbound OTLP/HTTP only.

## Operational policy

- Retention: Grafana Cloud operational metrics are retained for 14 days.
- Credentials: publisher and provisioning credentials are distinct least-privileged secrets in
  Key Vault. Rotate a credential by replacing its Key Vault value, redeploying the ACA secret
  reference, and verifying metric publication or provisioning with the replacement value.
- Contact points: designated on-call destinations are provisioned from version-controlled
  Grafana Terraform and verified in the release checklist before production enablement.
- Collector review: add an intermediary telemetry service only when buffering, redaction,
  shared traces or logs, fan-out, or multi-replica batching becomes necessary.
