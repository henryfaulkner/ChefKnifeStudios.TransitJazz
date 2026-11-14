# Linode migration status

**Last verified:** 2026-11-14, approximately 13:12 UTC.

The Linode API works at **https://api.transitjazz.com** and production capture was activated at **2026-11-14T13:06:22Z**. The owner stopped Azure; a subsequent CLI check confirmed `runningStatus=Stopped` and zero replicas. Linode is the sole running production statistics writer. Both published browser applications still point to Azure and require their endpoint release. The migration is not yet complete.

Local API/worker validation passed 572 tests, with 78 API tests skipped. Server Release build and client Release publish succeeded. The client publish artifact contains the new hostname for both API and SignalR; both live sites were separately verified to still use Azure. Existing dependency advisories remain outside this hosting change.

## Verified host and pilot

- Existing VM `expense-tracker-vm`, IPv4 `170.187.142.115`, Ubuntu 24.04 x86-64, 2 GB / 1 shared vCPU. Root key-based SSH works.
- PostgreSQL 16.15 remains its original enabled host service, data directory `/var/lib/postgresql/16/main`, port 5432. Its expense tracker and health hub databases were preserved. No PostgreSQL setting, schema, HBA or firewall rule was changed.
- All four checked-in TransitJazz EF migrations are applied. Local password-authenticated DB access was verified over TLS 1.3 with the existing app role.
- Docker 29.9.3 and Compose v5.7.1 were installed from Docker's Ubuntu repository. PostgreSQL stayed available; no host reboot was performed.
- App Compose project: `/opt/transitjazz`, separate from PostgreSQL. App limit: 512 MiB / 0.25 CPU. Proxy limit: 256 MiB. Runtime files: root-only `/etc/transitjazz/app.env` and `production.env`; the staged production settings are now active. The pilot settings and release reference were preserved as `pre-cutover.app.env` and `pre-cutover.release.env`.
- Pilot image is content-addressed locally as `sha256:ad307ef4a927c44001672f04e25ed769ceb8ec913272f8cb3086ee6b67f9b5b4`, built on the workstation from base checkout `1c0a42ca439b4ee26fe6137618ff14efa048ebec` plus uncommitted hosting changes. It was transferred over SSH, not published as a committed release in ACR. Do not identify it as the unchanged base commit.
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

## Production capture and CI preparation — 2026-11-14

- Activated `Metrics__Enabled=true` with environment `prod`, historical collection enabled with dry-run false, category capture enabled, and reported-vehicle mode `Persistence`. Initial backfill and client-experience capture remain disabled. Existing Grafana publisher/reader credentials were already present; no secret values were printed or checked in.
- Local and public readiness returned 200 after recreation. Public route/shape APIs returned the counts above for all eight cities. At 13:09 UTC, the app used approximately 234 MiB of its 512 MiB limit, with zero automatic restarts and no OOM kill. PostgreSQL and the proxy were not stopped.
- Read-only Repeatable Read queries against `transitjazz` confirmed all four EF migrations. Category rows for the 13:07 UTC minute appeared for all eight cities; writer logs reported successful inserts with zero conflicts. Historical collection is writing, but its initial overlapping lookbacks reported discrepant and NoData rows around the transition. Do not treat those minutes or the interrupted 13:00 UTC hour as complete evidence.
- At 13:12 UTC, all eight cities had a Complete dashboard row for 13:09 UTC, with source cycle timestamps around 13:09:50 UTC, after Azure stopped. This is evidence that Linode metrics are reaching Grafana and being read back by the collector. Category capture had Complete rows through 13:11 UTC with no conflicts. The 13:07–13:08 dashboard minutes were discrepant; retain their quality classification.
- Independent Grafana queries are currently blocked: the registered connector targets another instance, and `gcx`'s `transitjazz` OAuth session has expired. `gcx config check` confirmed the authentication failure. Run `gcx login` to restore dashboard-query access; do not substitute publisher credentials for interactive access.
- Created the `server-linode` GitHub environment, restricted to branches `main` and `migration/linode-server-hosting`, with `LINODE_HOST` and environment secrets `LINODE_DEPLOY_KEY` / `LINODE_KNOWN_HOSTS`. The host key was obtained through the existing trusted root SSH session. The temporary workstation CI private key was removed after upload and testing.
- Installed the locked-password `tj-deploy` account with a restricted forced-command SSH key, no Docker-group membership, and sudo permission only for the root-owned deployment script. Normal commands, malformed digests and command injection were rejected with status 64. Concurrent deployment was rejected with status 75. A missing ACR digest failed before changing the running production container.
- Created the ACR scope map/token `transitjazz-linode-pull`, granting only `content/read` and `metadata/read` for the server repository. The password was generated with a 365-day lifetime on 2026-11-14; rotate before expiry. Root's mode-0600 Docker configuration now uses that token. Pulling the retained Azure rollback image by digest succeeded.
- Initialized root-only `known-good.env` and `known-good.app.env` after production readiness. These currently identify the local pilot image with production configuration. Successful ACR deployment and failed-readiness rollback still require an actual release image.
- Cancelled obsolete Azure approval runs `43327159554`, `42547686400` and `42482736237` to remove pending restart paths. The local server workflow now builds on both release branches (and manual dispatch), enables `deploy-linode`, and removes the ACA deployment job. Actionlint passed. These workflow edits require publication before GitHub uses them.
- The hosting preparation is already committed as `6dfef670c72232af992416c09816cdbc0cdf86a8`. The owner explicitly authorized one exception to AGENTS.md's no-commit rule for this migration commit and push. At this inspection, the new workflow had not yet run and no new image had been published by this continuation.

## Migration backup

- Protected VM directory: `/var/backups/transitjazz-migration-20261113`.
- Protected off-host directory: `C:\Users\hfaul\TransitJazz-Backups\20261112-190948`.
- Custom-format dumps: `transitjazz`, `expensetrackerdb`, `expensetrackerdb_dev`, `personal_health_hub`; globals and PostgreSQL configuration are also preserved.
- Every transferred file matched its SHA-256 checksum. All four dumps restored successfully into isolated local PostgreSQL 16 containers with `--no-owner --no-privileges`; the expense databases needed a pgvector-enabled image. Scratch containers and their volumes were removed afterward.
- Off-host backup files are encrypted using Windows CurrentUser DPAPI, with decrypted checksums verified. `RECOVERY.md` beside them explains decryption and its dependency on this Windows profile/DPAPI keys. Plaintext workstation copies were removed; protected VM originals remain.
- This snapshot is not recurring off-host backup automation. Full role/configuration disaster recovery has not been rehearsed.

## Remaining cutover work

1. Publish the client endpoint change to both `main` and `deploy/marta-jazz` using the owner's branch workflow. Both public `appsettings.json` files still point to the stopped Azure server as of 13:10 UTC. Check browser routes, audio gesture, Stats, switching and reconnect on both brands after publication.
2. Publish this continuation's workflow/documentation edits on `migration/linode-server-hosting` under the owner's one-time commit/push authorization. Watch the server workflow publish its SHA-tagged API/migration images to ACR and deploy the API by digest to Linode. The workflow must be published before these triggers take effect.
3. Verify the first ACR release, its known-good snapshot, and failed-readiness rollback. The SSH environment, pull-only credentials, lock rejection and failed-pull checks are ready; an actual successful CI deployment remains unverified. Leave Azure stopped.
4. Restore Grafana CLI access, inspect new production metrics and historical minute quality, and verify the next uninterrupted full capture hour after the final release. The current 13:00 UTC hour is interrupted, and a further image deployment resets in-memory observations again.
5. Observe combined resource use and feed-refresh behavior. Test whole-host reboot recovery during a maintenance window, accounting for the other databases.
6. Establish recurring off-host backups/retention and external freshness monitoring. Preserve Azure's rollback image/configuration during the guide's observation window, then retire only the obsolete app resources.

See the [execution guide](LINODE_MIGRATION_STEP_BY_STEP.md), [deployment files](../deploy/linode/README.md) and [cost reassessment](LINODE_MIGRATION_COST_REASSESSMENT.md). No Git commits were created.
