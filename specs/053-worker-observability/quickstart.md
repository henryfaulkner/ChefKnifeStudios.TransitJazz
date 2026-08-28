# Quickstart: Worker Observability

## Production prerequisites

1. Ratify the Constitution IV amendment that permits Grafana Cloud for this worker.
2. Obtain approval for operational telemetry leaving the Azure/Geotab boundary.
3. Create distinct least-privileged Grafana publisher and provisioning tokens.
4. Store the tokens in Key Vault and configure ACA secret references. Never place them in appsettings, Bicep parameters, Docker Compose, or shell history.

## Local workflow

1. Set `Metrics__Enabled=true`, `Metrics__ExportIntervalMilliseconds=10000`, and `Metrics__LocalPrometheusEnabled=true`. Leave Cloud endpoint and authorization unset.
2. Start local observability services:

   ```powershell
   docker compose -f docker-compose.yml -f docker-compose.observability.yml up --build
   ```

3. Run the API host; it registers the worker:

   ```powershell
   dotnet run --project src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI
   ```

4. Confirm Prometheus sees the development-only scrape, then open Grafana's `transitjazz-worker-overview` dashboard. City panels must show configured city values only.
5. Run tests:

   ```powershell
   dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests
   ```

6. Simulate one city fetch failure while another succeeds. Verify only that city degrades. Stop the host to verify the global worker-gone page path.

The dashboard UID is `transitjazz-worker-overview`. Local Prometheus scrapes only the development listener and never receives Grafana Cloud credentials. The co-hosted API ingress remains dedicated to API/SignalR; the development-only listener accepts the Docker `host.docker.internal` scrape request and is never enabled in production.

## Production rollout

1. Deploy Key Vault, ACA secret references, metrics configuration, and Grafana provisioning with metrics disabled.
2. Verify dashboard assets, usage panels, alert contact point, and active-series budget.
3. Enable `Metrics__Enabled`, deploy a revision, and confirm metrics within two 10-second exports.
4. Verify every configured city and deliberately exercise one-city missing, worker stopped, errors, slow cycles, input stopped/high lag, and final shutdown flush.
5. Rotate the publisher token, redeploy its secret reference, and verify delivery again.

## Safety checks

- Production has no `/metrics` endpoint, metrics listener, or metrics probe.
- API ingress remains only for existing API/SignalR work.
- Metrics have no vehicle, route, customer, entity, URL, exception, or arbitrary-text attribute.
- Active series remain below 1,000 and are checked after the first production interval and every city-set change.
