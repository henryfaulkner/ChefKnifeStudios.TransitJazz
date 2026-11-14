# Linode migration status

**Last verified:** 2026-11-14, approximately 14:43 UTC.

The Linode API works at **https://api.transitjazz.com** and production capture was activated at **2026-11-14T13:06:22Z**. The owner stopped Azure; a subsequent CLI check confirmed `runningStatus=Stopped` and zero replicas. Linode is the sole running production statistics writer. Both published browser applications now point their API and SignalR settings to Linode. MartaJazz's legacy frontend still needs a protocol upgrade, and browser acceptance remains incomplete. The migration is not yet complete.

Local API/worker validation passed 572 tests, with 78 API tests skipped. Server Release build and client Release publish succeeded. Both live sites' configurations now contain the new hostname for API and SignalR, including compressed responses. Existing dependency advisories remain outside this hosting change.

## Verified host and pilot

- Existing VM `expense-tracker-vm`, IPv4 `170.187.142.115`, Ubuntu 24.04 x86-64, 2 GB / 1 shared vCPU. Root key-based SSH works.
- PostgreSQL 16.15 remains its original enabled host service, data directory `/var/lib/postgresql/16/main`, port 5432. Its expense tracker and health hub databases were preserved. No PostgreSQL setting, schema, HBA or firewall rule was changed.
- All four checked-in TransitJazz EF migrations are applied. Local password-authenticated DB access was verified over TLS 1.3 with the existing app role.
- Docker 29.9.3 and Compose v5.7.1 were installed from Docker's Ubuntu repository. PostgreSQL stayed available; no host reboot was performed.
- App Compose project: `/opt/transitjazz`, separate from PostgreSQL. App limit: 512 MiB / 0.25 CPU. Proxy limit: 256 MiB. Runtime files: root-only `/etc/transitjazz/app.env` and `production.env`; the staged production settings are now active. The pilot settings and release reference were preserved as `pre-cutover.app.env` and `pre-cutover.release.env`.
- Original pilot image was content-addressed locally as `sha256:ad307ef4a927c44001672f04e25ed769ceb8ec913272f8cb3086ee6b67f9b5b4`, built on the workstation from base checkout `1c0a42ca439b4ee26fe6137618ff14efa048ebec` plus uncommitted hosting changes. It was transferred over SSH, not published as a committed release in ACR. It is now retained for recovery; the running registry image is recorded below. Do not identify the pilot as the unchanged base commit.
- Pilot readiness recovered after an explicit container restart. The app subsequently ran for over 32 hours, with no automatic restarts or OOM kills. Recorded app cgroup memory peak was 419815424 bytes (about 400 MiB). At inspection, app usage was about 169 MiB and host available memory about 1.2 GiB. This does not certify production capture memory use or whole-host reboot recovery.
- The public hostname resolves to this VM and Caddy obtained a valid Let's Encrypt certificate. Name.com is authoritative; the nondelegated Azure DNS zone does not publish this domain. A temporary Azure `api` record created during discovery was removed. Existing apex/www records and nameservers were not changed by the agent.
- Public readiness: 200. Public worker hub and test broadcast route: 404. Port 8080 is loopback-only and was unreachable from the workstation.
- Public CORS preflights grant the exact TransitJazz/MartaJazz origins and credentials; an unapproved origin receives no allow-origin grant.
- Both route and shape APIs returned nonempty data for all eight configured cities. A real .NET MessagePack client using WebSockets joined each city and received two distinct batch timestamps through the public HTTPS endpoint on 2026-11-14.

| City | Routes | Shapes |
| --- | ---: | ---: |
| atlanta | 86 | 86 |
| washington-dc | 133 | 133 |
| boston | 207 | 207 |
| new-york-city | 372 | 372 |
| toronto | 230 | 230 |
| philadelphia | 164 | 164 |
| denver | 130 | 130 |
| seattle | 148 | 148 |

Recent bounded log inspection found no Error/Critical entries in the inspected 1000 app entries. Warnings included NO_CROSSINGS and isolated Seattle EMPTY_INPUT/NO_VEHICLES, so a healthy host should not be interpreted as a guarantee of every upstream feed. Fresh batches for all eight cities were separately verified.

## Production capture and successful CI deployment — 2026-11-14

- Activated `Metrics__Enabled=true` with environment `prod`, historical collection enabled with dry-run false, category capture enabled, and reported-vehicle mode `Persistence`. Initial backfill and client-experience capture remain disabled. Existing Grafana publisher/reader credentials were already present; no secret values were printed or checked in.
- Local and public readiness returned 200 after recreation. Public route/shape APIs returned the counts above for all eight cities. At 13:09 UTC, the app used approximately 234 MiB of its 512 MiB limit, with zero automatic restarts and no OOM kill. PostgreSQL and the proxy were not stopped.
- Read-only Repeatable Read queries against `transitjazz` confirmed all four EF migrations. Category rows for the 13:07 UTC minute appeared for all eight cities; writer logs reported successful inserts with zero conflicts. Historical collection is writing, but its initial overlapping lookbacks reported discrepant and NoData rows around the transition. Do not treat those minutes or the interrupted 13:00 UTC hour as complete evidence.
- At 13:12 UTC, all eight cities had a Complete dashboard row for 13:09 UTC, with source cycle timestamps around 13:09:50 UTC, after Azure stopped. This is evidence that Linode metrics are reaching Grafana and being read back by the collector. Category capture had Complete rows through 13:11 UTC with no conflicts. The 13:07–13:08 dashboard minutes were discrepant; retain their quality classification.
- At 13:19 UTC, the new ACR container had persisted metric cycle timestamps around 13:16:49–51 UTC for all eight cities. Category capture had Complete rows through 13:17–18 UTC, with no conflicts in the inspected post-release rows. Historical dashboard discrepancies also predate cutover: the bounded 12:40–12:50 UTC sample contained 71 Discrepant and 9 Complete city-minutes out of 80. Do not attribute the ongoing discrepancy pattern solely to migration; investigate it separately without overwriting confirmed values.
- Independent Grafana queries are currently blocked: the registered connector targets another instance, and `gcx`'s `transitjazz` OAuth session has expired. `gcx config check` confirmed the authentication failure. Run `gcx login` to restore dashboard-query access; do not substitute publisher credentials for interactive access.
- Created the `server-linode` GitHub environment, restricted to branches `main` and `migration/linode-server-hosting`, with `LINODE_HOST` and environment secrets `LINODE_DEPLOY_KEY` / `LINODE_KNOWN_HOSTS`. The host key was obtained through the existing trusted root SSH session. The temporary workstation CI private key was removed after upload and testing.
- Installed the locked-password `tj-deploy` account with a restricted forced-command SSH key, no Docker-group membership, and sudo permission only for the root-owned deployment script. Normal commands, malformed digests and command injection were rejected with status 64. Concurrent deployment was rejected with status 75. A missing ACR digest failed before changing the running production container.
- Created the ACR scope map/token `transitjazz-linode-pull`, granting only `content/read` and `metadata/read` for the server repository. The password was generated with a 365-day lifetime on 2026-11-14; rotate before expiry. Root's mode-0600 Docker configuration now uses that token. Pulling the retained Azure rollback image by digest succeeded.
- Initialized root-only `known-good.env` and `known-good.app.env` after production readiness. The successful CI release updated them to the registry digest below with production configuration; `previous-good.env` / `previous-good.app.env` retain the prior pilot with production configuration. Failed-readiness rollback remains unexercised.
- Cancelled obsolete Azure approval runs `43327159554`, `42547686400` and `42482736237` to remove pending restart paths. The published server workflow builds on both release branches (and manual dispatch), enables `deploy-linode`, and removes the ACA deployment job. Actionlint passed.
- Hosting preparation commit: `6dfef670c72232af992416c09816cdbc0cdf86a8`. The owner explicitly authorized an exception to AGENTS.md's no-commit rule for this migration change and push. CI activation commit `4e0f8f6a0ddf14d246c630cb25ed6db65558d403` was pushed to `migration/linode-server-hosting`; [Actions run 43598654579](https://github.com/henryfaulkner/ChefKnifeStudios.TransitJazz/actions/runs/43598654579) succeeded for both build/push and Linode deployment. CI passed 333 API tests (78 skipped) and 239 worker tests.
- Published API digest: `sha256:e0e6a30cb55b1ac7987a0cd2ca4843d0b56377a5bc1448436517480900ad22f0`. Published migration-image digest: `sha256:c2fee90dc484b4094a2df7ba2a437cbc40af5ea2f571b47ad0c5019c551a6076`. Both have tag `4e0f8f6a0ddf14d246c630cb25ed6db65558d403`. The migration image was published, not executed; no schema change was needed.
- Verified running API reference: `chefknife.azurecr.io/chefknifestudios.martajazz.server.webapi@sha256:e0e6a30cb55b1ac7987a0cd2ca4843d0b56377a5bc1448436517480900ad22f0`, started at 13:15:36 UTC. Readiness returned 200, production flags remained enabled, and there were zero automatic restarts or OOM kills. Startup readiness probes reported the route index loading before readiness recovered. PostgreSQL remains active. Structured logs still carry the old explicit pilot revision label; use the verified image reference and CI run for release identity until that label is corrected during a later deployment.

## Migration backup

- Protected VM directory: `/var/backups/transitjazz-migration-20261113`.
- Protected off-host directory: `C:\Users\hfaul\TransitJazz-Backups\20261112-190948`.
- Custom-format dumps: `transitjazz`, `expensetrackerdb`, `expensetrackerdb_dev`, `personal_health_hub`; globals and PostgreSQL configuration are also preserved.
- Every transferred file matched its SHA-256 checksum. All four dumps restored successfully into isolated local PostgreSQL 16 containers with `--no-owner --no-privileges`; the expense databases needed a pgvector-enabled image. Scratch containers and their volumes were removed afterward.
- Off-host backup files are encrypted using Windows CurrentUser DPAPI, with decrypted checksums verified. `RECOVERY.md` beside them explains decryption and its dependency on this Windows profile/DPAPI keys. Plaintext workstation copies were removed; protected VM originals remain.
- This snapshot is not recurring off-host backup automation. Full role/configuration disaster recovery has not been rehearsed.

## Published client endpoint change — 2026-11-14

- Used the supported SWA CLI production deployment with existing deployment tokens held only in process memory. Neither application deployment changed the running API container or capture settings.
- TransitJazz reused the production `blazor-client` artifact from successful Actions run `43327159822` (source `28f3b4ab69b43d37070618fd4960272ea4774c91`). Its original appsettings matched the live configuration. Updated both endpoint entries and regenerated their gzip/Brotli sidecars before uploading to `transit-jazz-dev-swa`.
- MartaJazz's original artifact had expired. Rebuilt its last successful deployed source `0704b32aa2e04d219628284b8a157c59970a0db5` in an isolated temporary worktree, with only the two source endpoint values changed. Release publish passed with the installed .NET 10.0.13 tooling; uploaded to `marta-jazz-dev-swa`.
- Both SWA deployments succeeded. Verified HTML 200 and both BaseUri values at `transitjazz.com`, `www.transitjazz.com`, `martajazz.com`, and `www.martajazz.com`, requesting identity, gzip and Brotli responses. Linode readiness returned 200.
- Independent SignalR clients using each site's Origin passed CORS negotiation, WebSocket handshake, `JoinCityV2("atlanta")`, and receipt of two distinct live batches. These checks exercise the current protocol; they do not prove the legacy MartaJazz frontend uses it.
- The MartaJazz source still has the old `JoinCity` method and `marta` / `wmata` city names. The current server intentionally exposes `JoinCityV2` as a wire-version gate. Repointing this old application does not upgrade its protocol; do not bypass the gate by changing only its method name. Upgrade that frontend before claiming full MartaJazz browser recovery.
- Browser automation was attempted but its navigation/control calls failed before opening the site. Visual rendering, audio gesture, Stats, city switching and browser reconnect acceptance remain unverified. The inherited workflow-generated SWA configuration also uses the unsupported top-level `headers` property; the CLI reported that validation warning while deployment succeeded, and responses have no `Blazor-Environment` header. Correct it to `globalHeaders` in a later configuration change.
- The migration branch already contains the new client URLs. `main` and `deploy/marta-jazz` were not changed by these manual releases: include the endpoint changes before their next automated deployments to prevent reversion. The temporary MartaJazz source edit and deployment files are under `C:\Users\hfaul\AppData\Local\Temp\transitjazz-client-cutover-zobrute9`.

## Built-in Linode server metrics — 2026-11-14

The owner installed native Grafana Alloy through Linux Server onboarding and verified its
Grafana connection. The service is active and ready; remote-write diagnostics reported zero
failed samples and no pending samples. Grafana queries confirmed host CPU and scrape metrics
under `job="integrations/unix"`, `instance="expense-tracker-vm"`.

The owner selected Grafana's built-in Linux dashboards only. The temporary custom Linode
dashboard was deleted and its repository JSON removed. Standard metrics-only integration
configuration is documented in [the host metrics setup](../deploy/linode/metrics/README.md).
The owner installed and reloaded it after validation. At 14:19-14:20 UTC, Grafana reported
`up=1` and 201 native host series under `integrations/node_exporter`. The native overview's saved
CPU, memory, disk-capacity and network queries all returned data over a 15-minute range at
one-minute resolution. Alloy was ready with zero failed writes and no pending samples.
The owner confirmed the native overview's CPU and memory panels display live metrics.
Log collection, remote configuration and integration alerts were disabled during onboarding.
Grafana CLI OAuth access has been restored.

## Continuous capture and hosting cleanup ? 2026-11-14

The owner accepted the migration and requested removal of the obsolete Azure
server hosting code after the following checks:

- At 14:32 UTC, the registry API container had run continuously since 13:15:36 UTC
  (over 76 minutes), with zero automatic restarts or OOM kills. All production
  capture and metrics flags remained enabled; app memory was about 272.5 MiB / 512 MiB.
- Read-only Repeatable Read queries against Linode `transitjazz.public` verified
  `[13:31, 14:31) UTC`: all eight cities and all 17 observed city/category pairs
  had 60/60 Complete, nonconflicting backing minutes (1,020 category-minute rows).
  This proves a rolling hour of category capture. It does not finalize the
  aligned 14:00?15:00 UTC category, route or reported-vehicle cohorts. The
  interrupted 13:00 UTC cohorts are Partial/conflicted and remain excluded.
- The existing dashboard-minute discrepancy pattern continues and is separate
  from category coverage; its rows must not be relabeled as reconciled data.
- Queried the saved [Linux node overview](https://gallantpuffin3113.grafana.net/d/nodes)
  against `grafanacloud-prom`, `job="integrations/node_exporter"`,
  `instance="expense-tracker-vm"`, for 14:24?14:39 UTC at one-minute resolution.
  Panels 11 (CPU), 15 (memory), 13 (load) and 2 (host uptime) each returned 16 points.
  CPU ranged 2.65?5.47%, memory 42.59?45.25%, and one-minute load peaked at 0.30.
  Saved expressions were used with all environment/site/cluster filters, the
  discovered job/instance, and a four-minute rate interval. This short host
  observation does not establish an hour of historical host telemetry.
- Removed the Container Apps app/environment, server identity/ACR role and
  server-only Log Analytics modules from source and regenerated `bicep/main.json`.
  Removed their deployment parameters, outputs and obsolete CI parameter checks.
  The retained Azure resource definitions compare equal to the previous compiled
  template. Existing Azure resources were not deployed, deleted or reconfigured.
- Removed unused Azure Identity/Blob packages and ACA revision fallbacks. Runtime
  self-call defaults now use loopback; API tooling and skill references use the
  stable Linode hostname. Historical Azure log-query tools and migration evidence
  remain usable during the rollback/retention window.
- Validation: 333 API tests passed (78 skipped), 239 worker tests passed; restored
  dependency graphs contain neither removed SDK. Bicep and both parameter files
  compile, the ARM template excludes the retired server resources, Actionlint
  passes, and managed skill copies were synchronized. Cleanup remains uncommitted.

## Remaining cutover work

1. Include the endpoint change in both release branches before their next automated client deployment. Upgrade MartaJazz's legacy frontend to the current protocol. Check browser routes, audio gesture, Stats, switching and reconnect on both brands; HTTP/configuration and current-protocol SignalR checks have passed.
2. Exercise failed-readiness rollback during a suitable release window. Successful CI deployment, known-good snapshots, lock rejection and failed-pull preservation are verified. Leave Azure stopped. Correct the explicit structured-log revision label during the next planned deployment.
3. Investigate the historical dashboard discrepancy pattern and verify the next uninterrupted full capture hour after the final release. The current 13:00 UTC hour is interrupted; if no further deployment occurs, inspect 14:00–15:00 UTC after it finalizes. A further image deployment resets in-memory observations again. Grafana CLI access is restored and the built-in host overview is displaying live metrics.
4. Observe combined resource use and feed-refresh behavior. Test whole-host reboot recovery during a maintenance window, accounting for the other databases.
5. Establish recurring off-host backups/retention and external freshness monitoring. Preserve Azure's rollback image/configuration during the guide's observation window, then retire only the obsolete app resources.

See the [execution guide](LINODE_MIGRATION_STEP_BY_STEP.md), [deployment files](../deploy/linode/README.md) and [cost reassessment](LINODE_MIGRATION_COST_REASSESSMENT.md). The final documentation receipt uses a CI skip marker to avoid another container restart.
