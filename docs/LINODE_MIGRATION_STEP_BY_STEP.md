# Move the TransitJazz server onto the existing Linode database VM

**Prepared:** 2026-11-08

**Updated:** 2026-11-12 — owner selected and reports completing the upgrade to 2 GB / 1 shared vCPU. Use the existing working Azure allocation as the initial application limit.

**Purpose:** A runnable operator guide. These steps have not been executed against your VM. Commands and templates require the checks and substitutions below.

**Your chosen layout:** run the combined TransitJazz API/SignalR/worker Docker image on the existing PostgreSQL Linode, now upgraded to 2 GB / 1 shared vCPU. Keep Azure Static Web Apps, Azure DNS, ACR, Key Vault and Grafana initially. Maintenance interruptions are acceptable. The [cost reassessment](LINODE_MIGRATION_COST_REASSESSMENT.md) supplies the historical billing comparison; this guide records the owner's subsequent decision to proceed.

This supersedes the separate-VM sizing and recovery assumptions in the [migration design](LINODE_SERVER_MIGRATION_DESIGN_DOCUMENT.md). The existing database stays in place. Its VM and disks are stateful: rebuilding the machine is not a harmless application reset.

## How to use this guide

Repository preparation now includes [deploy/linode](../deploy/linode/README.md): the app Compose project, Caddyfile, deploy scripts and release reference example. The server hosting changes and tests are prepared locally, and the server workflow has disabled Azure/Linode deployment jobs pending cutover preparation. These edits require owner review/commit/publication; they do not establish that any host operation or deployment has happened. Use the files from that directory instead of manually transcribing the corresponding templates below. Runtime secrets must remain outside the repository.

**Executed pilot:** [migration status](LINODE_MIGRATION_STATUS.md) records the verified host inventory, backups/restores, Docker installation, public HTTPS/MessagePack delivery and remaining production cutover gates. Use that record to avoid repeating completed host operations.

Work through the numbered steps in order. Each has an expected result and a recovery branch. Do not paste the entire document into a terminal. The stepwise skill's interactive format is adapted here to your request for a complete written guide.

Commands labeled **Workstation / PowerShell** run in your Windows repository checkout. Commands labeled **Linode / Bash** run over SSH on the database VM. The main path assumes a Linux x86-64 VM, Bash, and PostgreSQL reachable through a host TCP port. The Docker installation link below is for Ubuntu; use your distribution's official instructions if different.

Record these non-secret values before beginning:

| Value | Fill in |
| --- | --- |
| Linode label, ID, region, current plan | `REPLACE_ME` |
| VM IPv4 / operator SSH account | `REPLACE_ME` |
| PostgreSQL database and app role | `REPLACE_ME` |
| PostgreSQL deployment | Host service, or existing Docker project/container |
| PostgreSQL version, port, data directory/volume, backup destination | `REPLACE_ME` |
| Existing HTTP proxy and occupied ports | `REPLACE_ME` |
| API hostname | `api.transitjazz.com` |
| Azure resource group / Container App | `marta-jazz-dev-rg` / `marta-jazz-dev-ca-server` |
| ACR / server repository | `chefknife.azurecr.io` / `chefknifestudios.martajazz.server.webapi` |
| Old ACA revision, image digest, endpoint | Capture in step 2 |
| Target server image digest | Capture in step 9 |

Never put secrets in this worksheet, Git, screenshots, command arguments, or workflow artifacts. Do not run `docker inspect` or `docker compose config` without filtering: they can reveal environment secrets. Use `docker compose config --quiet` for validation.

## 1. Inspect the upgraded 2 GB VM and record its resource baseline

**Linode / Bash — read-only:**

```bash
uname -m
cat /etc/os-release
free -h
df -h
sudo ss -lntp
sudo systemctl status postgresql --no-pager
```

If Docker is installed, also inspect only names, images, ports and resource use:

```bash
sudo docker ps --format 'table {{.Names}}\t{{.Image}}\t{{.Ports}}'
sudo docker stats --no-stream --format 'table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}'
sudo docker compose version
```

Record the existing DB startup procedure, mounts, PostgreSQL major version, connections and other applications. Check peak memory and disk usage over a normal day where available; one idle snapshot is insufficient. Identify whether 80, 443, 8080 and 5432 are already occupied.

**Selected sizing:** 2 GB RAM / 1 shared vCPU. The owner reports the app works on Azure with minimum and maximum replicas both set to one; the supplied portal screenshot shows 0.25 vCPU / 0.5 GiB for that container. Start the Linode app with the corresponding **512 MiB memory limit and 0.25 CPU quota**, running one server container. Retain the proxy's 256 MiB limit. This replaces the earlier unmeasured 1.5 GiB app budget and 4–8 GB host recommendation.

The app and proxy limits total 768 MiB; PostgreSQL, Docker, the OS and cache share the remaining nominal 1.25 GiB. Limits are ceilings, not reservations. Keep the DB's connection count and memory settings unchanged initially. Target roughly 25% host memory available under representative combined load, including cold GTFS loading, feed refresh and DB maintenance. The working Azure allocation is evidence for this starting point; the pilot verifies shared-host behavior and Linux CPU throttling. Increase the app limit only if measured demand requires it and the DB-safe host budget allows it. A larger VM is a fallback if the combined workload cannot fit, rather than a prerequisite for starting.

Compare the **incremental price of resizing your existing VM** and its resulting account transfer pool. Do not count a new standalone $12 VM as the cost of this plan. Confirm the selected SKU's transfer allowance; some generations have no included transfer. [Akamai plan pricing](https://www.akamai.com/cloud/pricing/cpu).

**Proceed when:** the VM reports the selected 2 GB plan, you know what runs on it, how PostgreSQL starts and where its data lives, and you have recorded baseline resource use. Confirm combined-load headroom during the pilot and step 17. If another service owns 80/443, plan to add an API virtual host to that existing proxy; do not start the example Caddy container on those ports.

## 2. Record the Azure rollback target and disable automatic ACA deployment

**Workstation / PowerShell — read-only:**

```powershell
$rg = 'marta-jazz-dev-rg'
$app = 'marta-jazz-dev-ca-server'
az account show --query '{name:name,id:id}' -o table
az containerapp show -g $rg -n $app --query '{revision:properties.latestReadyRevisionName,image:properties.template.containers[0].image,endpoint:properties.configuration.ingress.fqdn}' -o json
az containerapp revision list -g $rg -n $app --query '[].{name:name,active:properties.active}' -o table
```

Save that output in your private operations notes. Also preserve the current effective non-secret settings and secret names/versions; do not export full secret-bearing environment arrays into the repo. Keep the ACA image available in ACR by digest for rollback.

Before merging/pushing migration changes, edit `.github/workflows/server.yml` so its **existing ACA `deploy` job** is disabled, for example with `if: ${{ false }}` at job level. Keep `build-and-push` enabled. Cancel any already running ACA deployment and check that no other workflow or Bicep job can reactivate it. This temporary condition is replaced by the Linode deployment job in step 20.

These are file changes for you to review and commit yourself; this guide does not make commits. Do not disable both client workflows permanently: they are needed for endpoint cutover later.

**Proceed when:** builds can publish images without automatically deploying to ACA. If you cannot verify that, use the GitHub Actions UI to disable the server workflow temporarily and re-enable only after the deployment job has been corrected.

## 3. Back up PostgreSQL outside the VM and verify a restore

Use your existing proven database-backup procedure if available. Preserve DB globals/roles, configuration, and the deployment definition as well as the database. Use a PostgreSQL client compatible with the server major version. A live filesystem copy of a PostgreSQL data directory is not a substitute for a consistent database backup.

For PostgreSQL installed as a host service, this is a **template** using local administrative authentication:

**Linode / Bash:**

```bash
DB_NAME='REPLACE_ME'
BACKUP_DIR="/var/backups/transitjazz/$(date -u +%Y%m%dT%H%M%SZ)"
sudo install -d -o postgres -g postgres -m 0700 "$BACKUP_DIR"
sudo -u postgres pg_dump -Fc -d "$DB_NAME" -f "$BACKUP_DIR/database.dump"
sudo -u postgres pg_dumpall --globals-only -f "$BACKUP_DIR/globals.sql"
sudo -u postgres pg_restore --list "$BACKUP_DIR/database.dump" >/dev/null
```

For Docker-hosted PostgreSQL, use the existing DB container's matching `pg_dump`/`pg_dumpall`, stream their output into protected host files, and use its established authentication. Do not add the DB to the new app Compose project. Inspect named mounts with a narrowly filtered command if their paths are unknown.

Copy the dump, globals and configuration into your existing encrypted off-host backup location. Restore into a **separate scratch database/instance**, never into the live DB; verify expected schemas, tables and representative row counts. `pg_restore --list` only checks that the archive can be read, not that a restore works. Protect the globals file because it can contain password hashes. [PostgreSQL backup guidance](https://www.postgresql.org/docs/current/backup-dump.html).

**Proceed when:** you have an off-host backup and a successful restore test. If the only backup is on this same Linode, finish the off-host copy before resizing.

## 4. Pause the Azure worker before database maintenance

The worker writes statistics into this DB. Pause it before the VM resize; other applications using the DB also need a maintenance window.

**Workstation / PowerShell — changes state:**

```powershell
$active = @(az containerapp revision list -g $rg -n $app --query '[?properties.active].name' -o tsv)
foreach ($revision in $active) {
    az containerapp revision deactivate -g $rg -n $app --revision $revision
    if ($LASTEXITCODE -ne 0) { throw "Could not deactivate revision $revision" }
}
az containerapp revision list -g $rg -n $app --query '[?properties.active].name' -o tsv
```

Expected final output: no active revisions. Confirm old replicas finish stopping and new worker observations/writes cease. Changing minimum replicas to zero alone is insufficient. [Azure revision commands](https://learn.microsoft.com/en-us/cli/azure/containerapp/revision?view=azure-cli-latest).

Do not start the Linode production worker yet. If Azure rejects a state change, resolve that before assuming the worker has stopped.

## 5. Resize the existing Linode

**Owner reports the 2 GB upgrade is already complete.** If step 1 confirms it, skip the resize/shutdown actions below and run only the verification checks. Do not resize again merely to follow this guide. The backup/restore and single-writer checks still apply before application cutover.

In Akamai Cloud Manager, select the **existing database VM**, choose **Resize**, and choose the plan from step 1. A cold resize is appropriate for this maintenance window. Keep automatic disk expansion off unless you also need more disk; expanding a disk complicates a later downsize. Do not use **Rebuild**, which replaces the installation.

Gracefully stop the existing PostgreSQL service/container using its own deployment procedure before the cold shutdown if practical. Preserve that deployment's restart policy. Resize, wait for completion, and boot the VM. Akamai documents that a resize preserves data/configuration and IPs, but verify them afterward. [Resize instructions](https://techdocs.akamai.com/cloud-computing/docs/resize-a-compute-instance).

**Linode / Bash — verify:**

```bash
free -h
df -h
sudo ss -lntp
```

Start/verify PostgreSQL through its original service or Compose project. Connect using its existing trusted administration method and run `SELECT now();`; verify expected database size/tables and other existing consumers. Check the system log for storage or OOM errors.

**Proceed when:** the resized VM and existing DB work normally. If they do not, recover the DB first. Keep ACA paused; the application migration can wait.

## 6. Choose the local database connection

The default in this guide is **Linux host networking for the app and proxy containers**. The app then reaches a host-accessible PostgreSQL listener through `127.0.0.1`. Bind the app itself to `127.0.0.1:8080`; only the proxy exposes it publicly. Host networking shares the host's network namespace and uses no Compose `ports` mappings. [Docker host networking](https://docs.docker.com/engine/network/drivers/host/).

Choose the matching case:

| Current PostgreSQL layout | Action |
| --- | --- |
| Host service listening on loopback TCP | Use `127.0.0.1` and its existing port |
| DB container already publishes a host TCP port | Use that host port; prefer a loopback-bound publication for local access |
| DB container reachable only on a Docker network | In the **existing DB project**, add a loopback-only host publication such as `127.0.0.1:5432:5432` if the port is free; preserve every volume and setting; recreate only through that project's reviewed procedure |
| No suitable host TCP path, or host networking unacceptable | Use a separately designed shared Docker-network connection and proxy configuration; do not substitute `localhost` into bridge-network containers |

Keep the existing password-authenticated app DB role. If a local `pg_hba.conf` rule is needed, scope it to this database, role and loopback address with password authentication; do not add `trust` or a broad public range. An existing DB Docker container may see a bridge gateway source rather than `127.0.0.1`; inspect the actual source and scope that rule accordingly.

Preserve TLS verification when the DB supports it. If its certificate names the existing DNS host, use that hostname and a **container-local** host mapping to `127.0.0.1` rather than weakening `VerifyFull`. If the DB intentionally has no TLS on the same-host loopback path, document that choice and use it only for this local connection. Never disable TLS on a public DB path. Do not change global host DNS, because other DB consumers may rely on it.

Test with `psql` using the application role, an interactive password prompt or protected password file, and the chosen TLS settings. Run only a harmless query such as `SELECT current_database(), current_user;`.

**Proceed when:** the local authenticated connection works without changing DB data. Keep the old Azure DB access rules during the rollback window and inventory Grafana/admin/other users before removing any public access later.

## 7. Prepare the application release for a normal reverse proxy

These are application edits to make and test before building the migration image; creating this guide does not implement them. Inspect the current branch before editing because client-experience observability work is in progress.

In `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs`:

1. Replace the localhost-only CORS policy with an explicit configured origin list (`Cors:AllowedOrigins`), preserving methods/headers/credentials. Include both public brands and their approved Static Web App origins. Keep development localhost origins in development configuration.
2. Configure `ForwardedHeadersOptions` for `XForwardedFor | XForwardedProto`, trusting only the loopback proxy and a single forwarding hop. Call `UseForwardedHeaders()` before rate limiting and other IP/scheme-dependent middleware. The stats and new client-experience limiters need the real client IP.
3. Set an explicit .NET host shutdown timeout, initially 90 seconds. The Compose stop budget below is 120 seconds; verify worker finalization and writer drain finish within it.
4. Disable the anonymous `/test/signalr` broadcast route in production. **Retain `/hubs/worker-transit`**: the embedded worker still uses a real loopback SignalR client. The public proxy will block that hub.

Do not change payload contracts, data-capture semantics or schema as part of relocating the host. Preserve the current wire protocol and MessagePack clients.

Run the affected server/worker tests and Release build using the repository's existing test conventions. Add focused host tests for CORS, trusted forwarded headers and endpoint restrictions. Use disposable PostgreSQL for any integration tests, never the real database. Verify those changes still allow loopback publishing and approved browser joins. Follow the [.NET proxy guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).

**Proceed when:** the tested release has these changes and all required migrations already applied or explicitly reviewed. If the selected release includes unrelated new migrations/features, choose an isolated hosting release or complete that feature's release requirements first.

## 8. Install or verify Docker without disturbing the DB

If the DB already runs in Docker, reuse that Docker installation. Do not uninstall packages, reset the daemon, or run cleanup commands that could affect its containers or volumes. Schedule any necessary engine upgrade with a DB backup and downtime.

If Docker is absent, install Docker Engine and the Compose plugin from the [official Ubuntu instructions](https://docs.docker.com/engine/install/ubuntu/) or the matching distribution page. For the examples below, require Compose **2.30 or newer** so `env_file.format: raw` is supported.

**Linode / Bash:**

```bash
sudo docker version
sudo docker compose version
sudo systemctl enable docker
sudo install -d -o root -g root -m 0755 /opt/transitjazz
sudo install -d -o root -g root -m 0700 /etc/transitjazz
```

Use a dedicated Compose project called `transitjazz-app`; the PostgreSQL project remains separate. Never use `docker system prune --volumes` or `docker compose down -v` as part of this migration.

**Proceed when:** Docker/Compose work and the existing DB still works after any installation changes.

## 9. Publish the image and configure pull-only registry access

Let the corrected server build workflow publish the tested commit to ACR. Do not build on the database VM: compilation competes with PostgreSQL for memory and disk.

**Workstation / PowerShell — resolve an immutable image:**

```powershell
$releaseSha = 'REPLACE_WITH_TESTED_GIT_SHA'
az acr repository show -n chefknife --image "chefknifestudios.martajazz.server.webapi:$releaseSha" --query digest -o tsv
```

Record the resulting `sha256:...`. In Azure Portal/CLI, create an ACR token/scope map with only `content/read` and `metadata/read` on the server repository, and on the data migration repository only if needed. Store its generated password in your secret manager. Avoid the ACR admin credential. [Repository token permissions](https://learn.microsoft.com/en-us/azure/container-registry/container-registry-token-based-repository-permissions).

**Linode / Bash — prompt for the token password:**

```bash
read -r -p 'ACR pull-token name: ' ACR_TOKEN_NAME
read -r -s -p 'ACR pull-token password: ' ACR_TOKEN_PASSWORD
printf '\n'
printf '%s' "$ACR_TOKEN_PASSWORD" | sudo docker login chefknife.azurecr.io --username "$ACR_TOKEN_NAME" --password-stdin
unset ACR_TOKEN_PASSWORD
```

Docker stores this credential for the root Docker client; protect root's Docker config. Use the same `sudo docker` convention in deployment commands. Cache the current and previous images for recovery.

**Proceed when:** pulling the selected digest succeeds. A failed registry login should not cause any database or running-application changes.

## 10. Create the runtime configuration securely

Use `sudoedit /etc/transitjazz/app.env` to create a root-owned mode-0600 file, copying required production values from your trusted Key Vault/ACA configuration into the editor. Avoid printing those values or using shell `source` on this file. The Compose raw format preserves dollar signs and quotes literally; do not wrap whole values in shell quotes. Values must fit the format; use an appropriate mounted configuration file for multiline secrets. [Compose environment-file behavior](https://docs.docker.com/reference/compose-file/services/#env_file).

Start with a **pilot** configuration:

```dotenv
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:8080
WebApi__BaseUrl=http://127.0.0.1:8080
SignalR__HubUrl=http://127.0.0.1:8080/hubs/worker-transit
ConnectionStrings__TransitJazzDB=REPLACE_WITH_LOCAL_CONNECTION_STRING
HistoricalStatistics__Enabled=false
HistoricalStatistics__DryRun=true
HistoricalStatistics__InitialBackfill=false
CityCategoryInsights__Enabled=false
ReportedVehicleHistory__Mode=Disabled
ClientExperience__Enabled=false
ClientExperience__CaptureEnabled=false
Metrics__Enabled=false
Cors__AllowedOrigins__0=https://transitjazz.com
Cors__AllowedOrigins__1=https://www.transitjazz.com
Cors__AllowedOrigins__2=https://martajazz.com
Cors__AllowedOrigins__3=https://www.martajazz.com
Cors__AllowedOrigins__4=https://gentle-moss-037e88c0f.7.azurestaticapps.net
Cors__AllowedOrigins__5=https://icy-plant-0ce425b0f.7.azurestaticapps.net
Logging__Structured__DeploymentRevision=REPLACE_WITH_RELEASE_SHA
```

This is a starting list, not a complete export. Add the current image's required agency keys (`WMATA_API_KEY`, `SOUND_TRANSIT_OBA_API_KEY`, MARTA rail key, and any newly configured keys), city overrides, stats configuration and other required values. Confirm the SWA hostnames still match your deployments. Remove service-discovery overrides such as `services__apiservice__https__0` if they would redirect local HTTP/SignalR calls back to Azure.

Create a separate `/etc/transitjazz/production.env`, also mode 0600, with the same connectivity and the **approved production flags**. Earlier inventory found historical capture enabled/dry-run false, category capture enabled, reported vehicles `Persistence`, and metrics enabled with environment `prod`; recheck before use. Include Grafana publisher/reader endpoints and their distinct tokens. Leave initial backfill false unless a deliberate backfill is planned. Keep new client-experience flags at the deployed release's approved state, not automatically enabled just because the source exists.

**Linode / Bash:**

```bash
sudo chmod 0600 /etc/transitjazz/app.env /etc/transitjazz/production.env
```

Key Vault remains the canonical secret store. The VM uses delivered values; copying `AZURE_CLIENT_ID` does not give it ACA's managed identity. Rotate secrets through a controlled file update and container recreation.

**Proceed when:** both files are complete, protected and kept outside Git. Pilot writers are disabled.

## 11. Create the app Compose project

Save this as `/opt/transitjazz/compose.yaml` using `sudoedit`. The app limits match the working Azure allocation: 512 MiB and 0.25 CPU, with one server container. Validate them against the shared-host budget from step 1; these limits do not reserve memory for PostgreSQL.

```yaml
name: transitjazz-app
services:
  server:
    image: ${SERVER_IMAGE:?Set an immutable server image}
    network_mode: host
    restart: unless-stopped
    env_file:
      - path: /etc/transitjazz/app.env
        format: raw
    mem_limit: 512m
    cpus: 0.25
    stop_grace_period: 120s
    logging:
      driver: json-file
      options:
        max-size: "10m"
        max-file: "5"

  proxy:
    image: ${CADDY_IMAGE:?Set a reviewed Caddy image digest}
    network_mode: host
    restart: unless-stopped
    mem_limit: 256m
    volumes:
      - /opt/transitjazz/Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy_data:/data
      - caddy_config:/config
    logging:
      driver: json-file
      options:
        max-size: "10m"
        max-file: "5"

volumes:
  caddy_data:
  caddy_config:
```

If preserving a database TLS hostname is necessary, add `extra_hosts` under **server** with `"YOUR_DB_CERTIFICATE_HOSTNAME:127.0.0.1"`. That hostname must match the certificate and the connection-string Host. Do not substitute arbitrary names.

If you already have an HTTP proxy, omit the `proxy` service/volumes, use its existing certificate process and apply the routing rules in step 12 there. The server commands still work; skip this guide's proxy-service commands.

Create `/opt/transitjazz/release.env` containing immutable image references:

```dotenv
SERVER_IMAGE=chefknife.azurecr.io/chefknifestudios.martajazz.server.webapi@sha256:REPLACE_ME
CADDY_IMAGE=caddy@sha256:REPLACE_ME
```

Resolve a reviewed Caddy 2 image digest by pulling its selected version and inspecting `RepoDigests`; record that version in operations notes. The app image is x86-64; confirm host architecture before pulling. Keep files root-owned. Validate without revealing secrets:

```bash
cd /opt/transitjazz
sudo docker compose --env-file release.env config --quiet
sudo docker compose --env-file release.env pull
```

**Proceed when:** validation and pulls succeed. No database service or DB volume belongs in this YAML.

## 12. Configure DNS, firewall and HTTPS

Create `/opt/transitjazz/Caddyfile`:

```caddyfile
api.transitjazz.com {
    @internal path /hubs/worker-transit /hubs/worker-transit/* /test/*
    respond @internal 404
    reverse_proxy 127.0.0.1:8080 {
        health_uri /health/ready
        health_interval 10s
        health_timeout 5s
    }
}
```

Caddy handles WebSocket upgrades and certificates; keep `/data` and `/config` persistent. Its admin endpoint also uses loopback by default; check for conflicts on port 2019. Review CAA records if certificate issuance fails. [Caddy proxy documentation](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy).

First verify the domain's authoritative nameservers. Inspection on 2026-11-13 UTC found **Name.com** (`ns1cwy.name.com`, `ns2ckr.name.com`, `ns3cjl.name.com`, `ns4gvx.name.com`) serving `transitjazz.com`; the existing Azure DNS zone is not delegated. In **Name.com DNS for transitjazz.com**, create/update only the `api` A record to `170.187.142.115`, TTL 300 seconds if supported. An Azure DNS edit will not publish this hostname under the current delegation. Do not change nameservers as part of this migration. Keep apex and `www` records for both sites pointed to their Static Web Apps. Do not publish AAAA until IPv6 routing/firewall/proxy behavior is verified. Remove or correct an existing conflicting `api` CNAME/AAAA deliberately, preserving its prior value in operations notes.

In the VM's provider firewall and existing host firewall, allow public TCP 80/443 and established return traffic. Keep SSH key-only and preserve operator access. Do not expose 8080. Preserve the existing restricted DB rules for Azure rollback, Grafana and administrators; do not open 5432 to everyone or prematurely remove its existing rules. Use Lish/console recovery if changing SSH rules.

**Linode / Bash:**

```bash
cd /opt/transitjazz
sudo docker compose --env-file release.env run --rm --no-deps proxy caddy validate --config /etc/caddy/Caddyfile
sudo docker compose --env-file release.env up -d proxy
```

Expected initially: HTTPS works, but proxy requests may return 503 because the server is not running. This is acceptable during setup.

## 13. Start the pilot and verify local delivery

**Linode / Bash:**

```bash
cd /opt/transitjazz
sudo docker compose --env-file release.env up -d --no-deps server
curl --max-time 10 -i http://127.0.0.1:8080/health/ready
sudo docker compose --env-file release.env logs --tail 80 server
```

Cold GTFS loading can take several minutes. Retry readiness periodically for up to approximately 15 minutes; inspect bounded logs rather than restarting repeatedly. Verify all configured cities have usable routes and fresh batches, not just that one route index loaded. Inspect logs locally and redact secrets before sharing any output.

**Workstation / PowerShell:**

```powershell
curl.exe --max-time 15 -i https://api.transitjazz.com/health/ready
curl.exe --max-time 15 -i https://api.transitjazz.com/hubs/worker-transit/negotiate
curl.exe --max-time 15 -i https://api.transitjazz.com/test/signalr
Test-NetConnection REPLACE_WITH_VM_IP -Port 8080
```

Expected: readiness eventually 200, both restricted paths 404, public 8080 unavailable. Verify `/gtfs/routes` and `/gtfs/routes/shapes` with the currently supported city/query parameters from the API. Use the browser app or a real MessagePack client for a city join and fresh batch; an HTTP health probe cannot prove WebSocket delivery.

Check a CORS preflight:

```powershell
curl.exe --max-time 15 -i -X OPTIONS 'https://api.transitjazz.com/hubs/transit/negotiate?negotiateVersion=1' -H 'Origin: https://transitjazz.com' -H 'Access-Control-Request-Method: POST' -H 'Access-Control-Request-Headers: content-type,x-signalr-user-agent'
```

Expected: successful preflight with the exact allowed origin and credentials support. Repeat for MartaJazz and verify an unapproved origin gets no allow-origin grant. CORS is not authentication for the public API.

**If it fails:** check local SignalR URL, local DB connection, missing agency keys, CORS and TLS separately. If the app is OOM-killed, increase its limit only within the DB-safe host budget or resize again. Do not remove all limits to make startup pass.

## 14. Confirm database compatibility without combining migrations

Relocating the same server release requires no database schema migration. Record the selected image's required EF migration level and compare with the existing DB using the established DB administration procedure.

If you selected a newer release that requires migrations, perform that release's reviewed migration step now, with an off-host backup, a schema-owner credential separate from the app role, and the migration image built from the same commit. Run it with the local DB networking chosen above, not an Azure endpoint. Do not start two migration jobs. Never automatically downgrade the database on app rollback.

The current migration Dockerfile passes its connection string to the bundle process; do not publish process diagnostics or verbose output containing credentials. If a migration is incompatible with the old server image, document the required roll-forward/DB restore procedure before continuing. Prefer keeping this host move free of such changes.

**Proceed when:** the app image and schema are compatible and the known Azure rollback release can still run against that schema.

## 15. Switch the Linode container to production settings

Recheck that ACA has no active revisions and no deployment in progress. Stop the pilot before enabling capture.

**Linode / Bash — changes state:**

```bash
cd /opt/transitjazz
sudo docker compose --env-file release.env stop server
sudo install -o root -g root -m 0600 /etc/transitjazz/production.env /etc/transitjazz/app.env
sudo docker compose --env-file release.env up -d --no-deps --force-recreate server
```

Verify readiness again. Confirm exactly one active production worker, fresh city batches, expected stats reads, Grafana production metrics, and successful DB persistence. Record the UTC cutover time. Restarting resets in-memory current-hour observations; inspect the next complete capture hour and any partial/conflicted hour around the cutover. Do not interpret an outage as zero transit activity.

If this fails, stop the Linode server and use step 18. Leave PostgreSQL and the proxy alone unless either is the actual problem.

## 16. Publish the new endpoint to both browser applications

In `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/appsettings.json`, change **both** entries:

```text
AppSettings.ExternalApis[Name=TransitJazzAPI].BaseUri
AppSettings.ExternalApis[Name=TransitJazzSignalR].BaseUri
```

Set both to `https://api.transitjazz.com` with no hub-path suffix. Keep all unrelated keys and feature flags.

The checked-in workflows have different triggers:

| Site | Workflow | Branch to publish |
| --- | --- | --- |
| transitjazz.com | `.github/workflows/client.yml` | `main` |
| martajazz.com | `.github/workflows/client-marta.yml` | `deploy/marta-jazz` |

Review/commit the endpoint change yourself and deliver it to both deployment branches through your usual branch workflow. A push to `main` alone does not deploy MartaJazz. Confirm both jobs succeed and each published `appsettings.json` points to the new hostname. Avoid overwriting site-specific branch changes.

Reload both sites in a fresh browser session. Verify routes, live updates, audio after a user gesture, Stats, city switching and reconnect/rejoin. Check the browser Network panel: HTTPS API calls and the WebSocket must go to `api.transitjazz.com`, not `azurecontainerapps.io`. Old open tabs may retain their original configuration until reloaded; changing DNS for your own domain cannot redirect Azure's generated hostname.

**Proceed when:** both public brands work through Linode and the old Azure worker remains stopped.

## 17. Validate shared-host resource use and recovery

During GTFS cold loading, normal polling, stats queries, a static-feed refresh and PostgreSQL maintenance, record:

- App and DB memory, host available memory/swap activity, CPU/steal and disk latency/free space.
- Worker cycle timing against its configured cadence, per-city freshness and DB failures/queue drops.
- Actual outbound transfer from Linode, including Grafana and other services sharing its transfer pool.
- DB backup completion and off-host retention.

Keep at least the planned memory margin; sustained swap or DB latency regression requires resizing or adjusting the app limit. Add an external availability/freshness check through your existing monitoring, because local checks cannot report a dead host. Keep Docker log rotation from the template and preserve Grafana token separation.

During a maintenance window, reboot the VM once and verify **both** PostgreSQL and the app recover. The DB's existing startup mechanism must remain enabled. The app may initially race DB startup; verify actual recovery rather than assuming `restart: unless-stopped` handles a running-but-unhealthy process. Test a container restart and client rejoin as well.

ACA logs stop at migration. New app logs are accessed through `docker compose logs`; keep the cutover time in the existing logging runbook. Add centralized log shipping later if wanted. [The design](LINODE_SERVER_MIGRATION_DESIGN_DOCUMENT.md) describes that optional Azure integration.

**Proceed when:** the VM survives a reboot, the DB stays healthy under combined load, production capture is verified, and backups run independently of the app Compose project.

## 18. Roll back to Azure if needed

Rollback moves the application back; it does not restore an old database snapshot or undo the VM resize.

**Linode / Bash:**

```bash
cd /opt/transitjazz
sudo docker compose --env-file release.env stop server
sudo docker compose --env-file release.env ps server
```

Confirm the server has stopped writing. Keep PostgreSQL running and preserve its existing external connection/TLS/firewall rules for Azure.

**Workstation / PowerShell:**

```powershell
$rollbackRevision = 'REPLACE_WITH_RECORDED_ACA_REVISION'
az containerapp revision activate -g $rg -n $app --revision $rollbackRevision
az containerapp revision list -g $rg -n $app --query '[].{name:name,active:properties.active}' -o table
```

Restore the original traffic assignment if required by the app's revision mode, then verify that revision serves the recorded ACA endpoint and reaches the DB. Do not assume activation alone changes every traffic rule. Restore both client BaseUri entries to the original ACA hostname and deploy **both branches** from step 16. Reload browsers and verify live delivery/history writes.

The API domain cannot simply CNAME to ACA unless its custom-domain binding and TLS certificate have been configured there. Republishing the known old hostname avoids that unprepared dependency.

If the **database VM** is down, moving the API back to ACA does not restore database access. Recover PostgreSQL from its own procedure and off-host backups. Do not restore an old DB backup merely because the app release failed: that would discard newer committed history.

## 19. Install a small repeatable application deployment command

Complete the first migration manually before automating. The command below pulls before stopping, serializes deployment, changes only the server service, checks readiness, and attempts image/config rollback. It does not apply schema migrations or certify every city healthy.

Create `/usr/local/sbin/transitjazz-deploy` as root, mode 0755. Create the directory/files with Linux LF line endings. This script assumes the filenames/layout above and `curl`/`flock` are installed.

```bash
#!/usr/bin/env bash
set -euo pipefail
[[ $# -eq 1 && "$1" =~ ^sha256:[a-f0-9]{64}$ ]] || exit 64
cd /opt/transitjazz
exec 9>/run/lock/transitjazz-deploy.lock
flock -n 9 || { echo 'Another deployment is running'; exit 75; }
image="chefknife.azurecr.io/chefknifestudios.martajazz.server.webapi@$1"
compose() { docker compose --env-file release.env "$@"; }
ready() {
  local deadline=$((SECONDS + 900))
  while ((SECONDS < deadline)); do
    if curl --fail --silent --max-time 5 http://127.0.0.1:8080/health/ready >/dev/null; then
      return 0
    fi
    sleep 10
  done
  return 1
}
docker pull "$image"
test -s /etc/transitjazz/known-good.env
test -s /etc/transitjazz/known-good.app.env
test "$(grep -c '^SERVER_IMAGE=' release.env)" -eq 1
# Keep the reviewed proxy reference; replace only the server reference.
awk -v image="$image" '/^SERVER_IMAGE=/ { print "SERVER_IMAGE=" image; next } { print }' \
  release.env > release.env.next
docker compose --env-file release.env.next config --quiet
compose stop server
mv release.env.next release.env
if compose up -d --no-deps --force-recreate server && ready; then
  install -m 0600 /etc/transitjazz/known-good.env /etc/transitjazz/previous-good.env
  install -m 0600 /etc/transitjazz/known-good.app.env /etc/transitjazz/previous-good.app.env
  install -m 0600 release.env /etc/transitjazz/known-good.env
  install -m 0600 /etc/transitjazz/app.env /etc/transitjazz/known-good.app.env
  echo 'Candidate ready; complete browser and capture checks.'
  exit 0
fi
echo 'Candidate failed; restoring last ready image and runtime configuration.'
compose stop server
install -m 0600 /etc/transitjazz/known-good.env release.env
install -m 0600 /etc/transitjazz/known-good.app.env /etc/transitjazz/app.env
if compose up -d --no-deps --force-recreate server && ready; then
  echo 'Previous version restored; requested release failed.'
else
  echo 'Rollback is unhealthy; inspect local logs and use the recovery runbook.'
fi
exit 1
```

After step 17 succeeds, initialize its baseline:

```bash
cd /opt/transitjazz
sudo install -m 0600 release.env /etc/transitjazz/known-good.env
sudo install -m 0600 /etc/transitjazz/app.env /etc/transitjazz/known-good.app.env
sudo bash -n /usr/local/sbin/transitjazz-deploy
```

Run a release from a persistent SSH/tmux session:

```bash
sudo /usr/local/sbin/transitjazz-deploy sha256:REPLACE_WITH_64_HEX_DIGEST
```

For a later manual rollback to the previous good version, acquire the same deployment lock in an operator session, stop **server**, restore `/etc/transitjazz/previous-good.env` to `release.env` and `previous-good.app.env` to `app.env`, recreate **server** and check readiness. Save failed-release evidence first. Do not run that concurrently with CI or a schema migration.

If a terminal/job dies during a change, inspect the running image, `release.env`, the known-good files and lock status before retrying. This small script has no durable deployment coordinator. An unexpected shell or host failure after stop may require manual recovery; it must not be reported as success. Keep old images cached and do not automatically prune their layers.

## 20. Connect GitHub Actions to that deployment command

Keep the existing build-and-push job and its SHA-tag output. Replace the disabled **ACA deploy job** with a Linode job after manual deployment/rollback works. Use `server-linode` as a GitHub environment and limit it to your release branch.

On the VM create a dedicated unprivileged `tj-deploy` account with key-only authentication and no Docker-group membership. Allow only the root-owned deploy script through a sudoers entry, validated with `visudo`:

```sudoers
tj-deploy ALL=(root) NOPASSWD: /usr/local/sbin/transitjazz-deploy
```

This grants deployment privilege, so protect it like root. The script rejects anything other than one digest. Do not allow arbitrary Docker, shell or file-editing commands through sudo.

Create a root-owned `/usr/local/bin/transitjazz-ssh-deploy`, mode 0755:

```bash
#!/usr/bin/env bash
set -euo pipefail
command=${SSH_ORIGINAL_COMMAND:-}
[[ "$command" =~ ^deploy\ (sha256:[a-f0-9]{64})$ ]] || exit 64
exec sudo -n /usr/local/sbin/transitjazz-deploy "${BASH_REMATCH[1]}"
```

Put the workflow public key in `tj-deploy`'s `authorized_keys` with these restrictions, followed by the actual key:

```text
restrict,command="/usr/local/bin/transitjazz-ssh-deploy" ssh-ed25519 REPLACE_WITH_PUBLIC_KEY
```

Verify ownership/modes for the SSH directory and file. Store the private key as environment secret `LINODE_DEPLOY_KEY`; store a trusted known-hosts entry as `LINODE_KNOWN_HOSTS`, after comparing the server's host-key fingerprint through your existing trusted SSH/Lish session. Store `LINODE_HOST` as an environment variable. Never use `StrictHostKeyChecking=no`.

GitHub-hosted runners have changing source addresses. Either allow key-only SSH for the restricted workflow account through your firewall, or use an existing private runner/network that has a stable allowed address. If you do not want public SSH and have no private route, keep CI builds plus manual deployments; do not run a general self-hosted CI runner on the database server.

Example replacement job, retaining the workflow's existing `REGISTRY`/`IMAGE_NAME` values and build output:

```yaml
  deploy-linode:
    needs: build-and-push
    runs-on: ubuntu-latest
    environment: server-linode
    timeout-minutes: 45
    concurrency:
      group: transitjazz-production-deploy
      cancel-in-progress: false
    steps:
      - uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ secrets.ACR_USERNAME }}
          password: ${{ secrets.ACR_PASSWORD }}

      - name: Resolve published image digest
        id: image
        env:
          RELEASE_TAG: ${{ needs.build-and-push.outputs.image-tag }}
        run: |
          set -euo pipefail
          ref="$REGISTRY/$IMAGE_NAME:$RELEASE_TAG"
          docker pull "$ref"
          full=$(docker image inspect "$ref" --format '{{index .RepoDigests 0}}')
          digest="${full##*@}"
          [[ "$digest" =~ ^sha256:[a-f0-9]{64}$ ]]
          echo "digest=$digest" >> "$GITHUB_OUTPUT"

      - name: Deploy to Linode
        env:
          DEPLOY_KEY: ${{ secrets.LINODE_DEPLOY_KEY }}
          KNOWN_HOSTS: ${{ secrets.LINODE_KNOWN_HOSTS }}
          LINODE_HOST: ${{ vars.LINODE_HOST }}
          DIGEST: ${{ steps.image.outputs.digest }}
        run: |
          set -euo pipefail
          umask 077
          mkdir -p ~/.ssh
          printf '%s\n' "$DEPLOY_KEY" > ~/.ssh/transitjazz_deploy
          printf '%s\n' "$KNOWN_HOSTS" > ~/.ssh/known_hosts
          trap 'rm -f ~/.ssh/transitjazz_deploy' EXIT
          ssh -i ~/.ssh/transitjazz_deploy \
            -o BatchMode=yes -o StrictHostKeyChecking=yes \
            -o ServerAliveInterval=30 -o ServerAliveCountMax=6 \
            "tj-deploy@$LINODE_HOST" "deploy $DIGEST"
```

The example preserves your current ACR build credentials to keep the migration small; moving CI to Azure OIDC can follow separately. Pin third-party actions according to your repository policy. Add server/worker tests to the build job if not already present. Remove the old ACA update/revision/promotion steps so no later push restarts it. Treat `latest` as optional convenience; deployment uses a digest.

Test an allowed deployment, rejected malformed command, concurrent deployment, failed image pull and failed readiness/rollback. Confirm the DB container/service never stops. CI success proves readiness only; perform the per-city/browser/capture checks from steps 15–17. A lost SSH connection is a failed/unknown deployment, not permission to launch a competing job: inspect the host first.

## 21. Retire the Azure server after the rollback window

Keep the stopped ACA app for a proposed 7–14 day observation period. Confirm its ongoing cost, image availability and configuration. Keep shared Azure services used by the websites or other projects.

Once satisfied:

1. Confirm both clients use `api.transitjazz.com` and all production writers are on Linode.
2. Update Bicep and workflow definitions so a later infrastructure run cannot recreate an active ACA server.
3. Remove only the obsolete app/server-specific resources after checking remaining dependencies. Do not delete the whole resource group: it contains SWAs, DNS and shared services.
4. Review DB public access: remove obsolete ACA access only after inventorying Grafana, admin and other consumers. Keep TLS/authentication for remaining remote connections.
5. Record image/configuration versions, VM plan, backup/restore process, log access, secret-rotation process and actual costs in operations notes.

## Completion checklist

- [ ] PostgreSQL has a tested off-host backup and its original data volumes remain intact.
- [ ] VM size covers DB, app, proxy and OS at peak load; app resource limits are in place.
- [ ] App uses a verified local DB path and loopback worker hub; public 8080/worker/test routes are blocked.
- [ ] Both website branches have published the stable HTTPS API/SignalR endpoint.
- [ ] Exactly one production worker runs; a complete post-cutover statistics hour and normal feed refresh were checked.
- [ ] Metrics, external availability checks, bounded logs and transfer observation work.
- [ ] Manual app rollback and whole-host reboot recovery have been exercised.
- [ ] CI builds/publishes the image; manual or automated CD deploys only to Linode.
- [ ] No destructive app cleanup can remove PostgreSQL volumes; DB recovery remains separate from app rollback.

**Execution status:** the 2 GB host, tested off-host snapshot, Docker installation and public pilot are verified in [migration status](LINODE_MIGRATION_STATUS.md). The owner added the authoritative Name.com API record. Linode production capture remains disabled; Azure remains active. Local server/workflow/client changes await owner review/commit/publication. No production schema, PostgreSQL configuration or firewall change was made, and no Git commit was created.
