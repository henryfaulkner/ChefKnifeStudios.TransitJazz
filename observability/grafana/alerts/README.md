# Client-experience Grafana alerts

`transitjazz-client-experience-alerts.json` is the Grafana alert file-provisioning artifact. Regenerate its embedded PostgreSQL queries from the reviewed SQL files with:

```powershell
.\observability\grafana\alerts\generate-client-experience-alerts.ps1
```

The current dashboard's PostgreSQL datasource UID (`cg086lcj6z6yob`) is used for SQL rules. Set `CLIENT_EXPERIENCE_METRICS_DATASOURCE_UID` before generation to fill in the Prometheus datasource UID; otherwise the artifact retains `__SET_METRICS_DATASOURCE_UID__` and must be edited before collection rules can evaluate.

The SQL queries use fixed city, journey, capability, and severity dimensions. They exclude server-labeled synthetic events from visitor-health denominators. Health rules emit `NULL` when evidence is insufficient, while valid healthy observations emit zero; Grafana no-data and query errors are configured as alerting states. Missing alert dimensions are retained for six evaluation intervals before resolution. Toast volume is diagnostic-only (`severity=advisory`). Two Prometheus rules consume the k6 app-readiness and runner-failure metrics from the five-minute browser check; verify those series are available from the selected metrics datasource in staging.

No contact points or notification policies are included. The rule pack links to dashboard UID `transitjazz-client-experience`; confirm that dashboard and the PostgreSQL/Prometheus datasource UIDs exist in the target Grafana instance before enabling rules. File-based provisioning is not available in Grafana Cloud, so Cloud users must create these rules through the supported alerting UI/API rather than placing this file on a Grafana server. Validate rule firing, no-data/query-error behavior, and recovery in staging before routing operational notifications.
