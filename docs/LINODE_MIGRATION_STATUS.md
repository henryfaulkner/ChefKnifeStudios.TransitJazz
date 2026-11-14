# Linode migration status

**Last verified:** 2026-11-14, approximately 11:57 UTC.

The public Linode pilot works at **https://api.transitjazz.com**. Azure still serves the published browser applications and remains the sole production statistics writer. Linode capture and metrics publishing are disabled. The migration is not yet a completed production cutover.

Local API/worker validation passed 572 tests, with 78 API tests skipped. Server Release build and client Release publish succeeded. The client publish artifact contains the new hostname for both API and SignalR; both live sites were separately verified to still use Azure. Existing dependency advisories remain outside this hosting change.

## Verified host and pilot

- Existing VM `expense-tracker-vm`, IPv4 `170.187.142.115`, Ubuntu 24.04 x86-64, 2 GB / 1 shared vCPU. Root key-based SSH works.
- PostgreSQL 16.15 remains its original enabled host service, data directory `/var/lib/postgresql/16/main`, port 5432. Its expense tracker and health hub databases were preserved. No PostgreSQL setting, schema, HBA or firewall rule was changed.
- All four checked-in TransitJazz EF migrations are applied. Local password-authenticated DB access was verified over TLS 1.3 with the existing app role.
- Docker 29.9.3 and Compose v5.7.1 were installed from Docker's Ubuntu repository. PostgreSQL stayed available; no host reboot was performed.
- App Compose project: `/opt/transitjazz`, separate from PostgreSQL. App limit: 512 MiB / 0.25 CPU. Proxy limit: 256 MiB. Runtime files: root-only `/etc/transitjazz/app.env` (pilot) and `production.env` (staged settings, not active).
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

## Migration backup

- Protected VM directory: `/var/backups/transitjazz-migration-20261113`.
- Protected off-host directory: `C:\Users\hfaul\TransitJazz-Backups\20261112-190948`.
- Custom-format dumps: `transitjazz`, `expensetrackerdb`, `expensetrackerdb_dev`, `personal_health_hub`; globals and PostgreSQL configuration are also preserved.
- Every transferred file matched its SHA-256 checksum. All four dumps restored successfully into isolated local PostgreSQL 16 containers with `--no-owner --no-privileges`; the expense databases needed a pgvector-enabled image. Scratch containers and their volumes were removed afterward.
- Off-host backup files are encrypted using Windows CurrentUser DPAPI, with decrypted checksums verified. `RECOVERY.md` beside them explains decryption and its dependency on this Windows profile/DPAPI keys. Plaintext workstation copies were removed; protected VM originals remain.
- This snapshot is not recurring off-host backup automation. Full role/configuration disaster recovery has not been rehearsed.

## Remaining cutover work

1. Owner reviews, commits and publishes the prepared server/workflow/client changes. AGENTS.md prohibits the agent from creating commits. Both client BaseUri entries now point to the verified hostname locally; published client configurations have not been changed by these edits. Include the workflow changes so a push does not automatically redeploy ACA.
2. Publish the committed migration image to ACR, establish pull-only VM registry access, and replace the local pilot reference with that immutable registry digest. Both deployment jobs are disabled in the prepared workflow; build-and-push remains enabled with API/worker tests.
3. Publish the client endpoint change to both `main` and `deploy/marta-jazz` using the owner's branch workflow. Check browser routes, audio gesture, Stats, switching and reconnect on both brands. Pilot delivery can be checked while Azure still captures history.
4. Recheck the active Azure revision and absence of deployment races. Stop Azure's worker, confirm it has stopped, then activate Linode's staged production settings. Never enable two production capture workers. Preserve the Azure rollback image, configuration and DB access.
5. Verify production metrics, DB writes, a full post-cutover capture hour, combined resource use and feed-refresh behavior. Test whole-host reboot recovery during a maintenance window, accounting for the other databases.
6. Initialize known-good files and exercise the deployment command, failure/rollback and concurrency cases. Configure the restricted CI SSH account/environment and enable only Linode CD after those checks. The script is installed but these release checks are not complete.
7. Establish recurring off-host backups/retention and external freshness monitoring. Keep Azure available for rollback during the guide's observation window, then retire only the obsolete app resources.

See the [execution guide](LINODE_MIGRATION_STEP_BY_STEP.md), [deployment files](../deploy/linode/README.md) and [cost reassessment](LINODE_MIGRATION_COST_REASSESSMENT.md). No Git commits were created.
