# Worker alert runbook

`WorkerStalled` means a heartbeat exists but is older than three configured intervals. `WorkerGone` means the full-cycle series is absent and Grafana no-data is alerting. Investigate both as critical global incidents; use city alerts for city-scoped action.

To validate deliberately: stop the host for three intervals (repeat three times), suppress one configured city while another reports, simulate fetch failure, hold input empty for 15 minutes, hold p95 duration above 30 seconds for 15 minutes, and supply known lag above 900 seconds for 10 minutes. Confirm the designated notification route and severity each time.

Before release: verify active-series and samples-per-second panels, rotate the publisher token through Key Vault and redeploy its ACA secret reference, stop the host after a completed cycle and confirm shutdown flush, and record each notification result in the release checklist.
