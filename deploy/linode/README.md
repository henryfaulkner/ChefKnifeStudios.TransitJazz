# TransitJazz on the existing Linode database VM

Use the [operator guide](../../docs/LINODE_MIGRATION_STEP_BY_STEP.md) for backup, local database networking, runtime configuration, pilot, cutover and recovery. These files implement its app-only layout for the owner's selected 2 GB / 1 shared vCPU host. PostgreSQL remains in its existing deployment.

The server starts with 512 MiB and a 0.25 CPU quota, matching the working Azure allocation. The proxy has a 256 MiB limit. Confirm combined host usage before production cutover; neither limit reserves resources for PostgreSQL.

For Azure Server Metrics' replacement, use the built-in Linux Server dashboards described in
[Linode host metrics](metrics/README.md). Standard Linux collection is active; native overview
queries return CPU, memory, disk and network data. The owner confirmed the native overview
displays live metrics.

## Installation after host inspection and backup verification

1. Place `compose.yaml` and `Caddyfile` in `/opt/transitjazz`, root-owned. If an existing proxy owns ports 80/443, adapt its virtual host instead of starting this proxy.
2. Copy `release.env.example` to `/opt/transitjazz/release.env`. Replace the server placeholder with the published, tested migration image digest. Caddy is pinned to the image used for local configuration validation; verify its architecture support before deploying.
3. Create root-owned mode-0600 `/etc/transitjazz/app.env` and `production.env` through the guide's secure configuration procedure. Do not copy production secrets into this repository. Pilot capture/writers and metrics remain disabled.
4. Validate `docker compose --env-file release.env config --quiet`, then follow the guide's pilot and cutover checks. Keep exactly one production worker.
5. After the manual release, recovery and capture checks pass, initialize the known-good files and install `transitjazz-deploy` at `/usr/local/sbin/transitjazz-deploy`, root-owned mode 0755. Its readiness check does not prove every city or browser works.
6. Install `transitjazz-ssh-deploy` at `/usr/local/bin/transitjazz-ssh-deploy`, root-owned mode 0755, only when setting up the restricted `tj-deploy` account described in step 20. Restrict its SSH public key command and sudo permission exactly as documented there.

The published workflow builds on `main` and `migration/linode-server-hosting`, supports manual dispatch, and enables only `deploy-linode`; the ACA deployment job has been removed. The build job runs API/worker tests before publishing SHA-tagged API and migration images. Deployment resolves the API's immutable digest and invokes the restricted SSH command. The `server-linode` environment and its `LINODE_HOST`, `LINODE_DEPLOY_KEY`, and `LINODE_KNOWN_HOSTS` settings are configured, with deployment restricted to those two branches. The first build/push/deploy succeeded in [Actions run 43598654579](https://github.com/henryfaulkner/ChefKnifeStudios.TransitJazz/actions/runs/43598654579).

## Recorded Azure rollback target — 2026-11-12

- App: `marta-jazz-dev-ca-server`, resource group `marta-jazz-dev-rg`.
- Active revision: `marta-jazz-dev-ca-server--0000204`, 100% traffic at inspection.
- Image tag: `chefknife.azurecr.io/chefknifestudios.martajazz.server.webapi:bc5e157b6d4e948597c56fe17bdd0345009219dc`.
- Resolved image digest: `sha256:f05c812e2072b4ead16f4fda4ad8e547a48acef6e3254227851ec5c90574daea`. Keep that image available in ACR throughout the rollback window.
- Endpoint: `https://marta-jazz-dev-ca-server.jollytree-dd5ca774.eastus2.azurecontainerapps.io`.
- Allocation: 0.25 CPU / 0.5 GiB; minimum and maximum replicas both one.
- Both configured Static Web App hostnames were verified against Azure.

**Production capture activated, 2026-11-14T13:06:22Z:** see [migration status](../../docs/LINODE_MIGRATION_STATUS.md) for evidence and remaining release gates. Azure is stopped; the new CI-published ACR image is running with production capture and metrics settings. Category writes and metric round trips are verified for all eight cities. Both public clients still need their endpoint release. Pull-only ACR access, restricted CI SSH, successful digest deployment, known-good files, malformed-command rejection, lock rejection and failed-pull preservation are verified. Failed-readiness rollback, uninterrupted full-hour capture, whole-host reboot and recurring backups remain pending. Historical dashboard discrepancies also existed before cutover and need separate investigation.

## Local preparation validation — 2026-11-12

- Hosting tests: 11 passed, covering allowed/unapproved CORS preflights, exact loopback proxy trust, one forwarded hop, development-only test broadcasting, worker hub negotiation and shutdown configuration.
- Full API suite: 333 passed, 78 skipped. Skipped database-dependent tests are not a successful live database validation.
- Worker suite: 239 passed. Release server build succeeded; existing dependency advisories remain.
- Compose parsed successfully with an empty temporary runtime env file; actual host configuration is still pending.
- Caddy configuration validated using the pinned image; no public DNS/certificate issuance was attempted.
- Bash syntax checks and malformed/empty deploy-command rejection passed. Successful deployment, concurrent deployment and readiness-failure rollback remain host acceptance checks.
- Workflow YAML parsed and both deploy jobs were verified disabled. GitHub environment credentials and actual Actions execution remain pending.
- Client Release publish succeeded after preparing both BaseUri changes. The published local artifact was checked for `https://api.transitjazz.com` on both API and SignalR entries. Both live sites were separately checked and still use the Azure endpoint.
