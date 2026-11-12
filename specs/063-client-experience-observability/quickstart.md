# Quickstart: Client Experience Observability

This is the implementation and verification path for [the spec](spec.md). Planning generated no application code, migration, dashboard deployment, or alert routing change.

## 1. Freeze the measurement contract

Read [capture/intake](contracts/client-experience-v1.md), [operator metrics](contracts/operator-metrics-v1.md), and [data model](data-model.md). Inventory every owner outcome and all 26 notification issue codes against the fixed catalog. Confirm starts, terminal results, deliberate exclusions, observation windows, recovery evidence, and measurement-version rules before writing capture hooks. Keep unsupported freshness and audible-output claims absent.

## 2. Build compatible storage and intake first

Add shared DTOs and fixed codes, raw/daily data models, migration, reviewed correlation views, the bounded anonymous batch endpoint, independent ingest/database probe, fixed-label collection metrics, and hourly correction/retention work. Default intake disabled, require `ConnectionStrings:TransitJazzDB` when enabled, and correct the Stats rate-limit rejection callback so it does not count client-experience rejects as Stats traffic. Check deployed `/api` routing and production CORS/proxy behavior before browser emission.

Use an isolated disposable PostgreSQL database for concurrency, rollback, replay, immutable receipt time, logical conflict, window-overlap, and aggregation tests. The existing WebAPI TestHost and disposable database fixtures provide patterns; an in-memory database cannot prove these properties. Review the migration script and apply it before deploying a server that expects the schema. Follow the existing server workflow's manual migration gate; do not let a Spec Kit hook commit changes.

## 3. Capture the first useful client slice

Share one page ID/outbox between pre-application bootstrap and the application reporter. Register passive capture before `App.razor` starts owner operations, without resolving scoped component dependencies from root DI. Instrument confirmed city route/map readiness, live current-city join, audio unlock/resume (including inline failure), and committed toasts. Capture both successful and bad outcomes and eligible denominators. Keep notification suppression independent. Remove unused warning/error toast bypasses after typed coordinator coverage is in place.

Add requested-state transitions, bounded foreground windows, verified recovery, and suspension gaps. Then cover settings effect versus persistence, failed audio stop, voice readiness, conditional map outcomes, and source navigation. Emit only where current-generation owner evidence is accepted. Never emit per-note, per-vehicle, or per-frame callbacks.

## 4. Prove delivery and safety

Exercise offline enqueue, reload/city change, reconnect, multi-tab writes, storage unavailable, age/capacity discard, retries and `Retry-After`, 413 split, invalid members, and lost acknowledgement. Verify an accepted event keeps its UUID through replay and only durable acknowledgements delete it. Confirm reporter and storage failures do not affect map, audio, controls, toast visibility/reading time, or produce recursive warnings.

Run focused existing client/server tests as changes land, then the affected full suites and Release builds. Representative commands from repository root:

```powershell
dotnet test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.csproj
dotnet build src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj -c Release
dotnet build src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj -c Release
```

## 5. Verify reports and alerts

Seed starts/results, successes without toasts, inline failures, combined/escalated toasts, gaps, skew, delayed replay, orphan/conflicting results, exclusions, and zero-denominator cohorts. Compare reviewed view totals with dashboard panels and alert SQL. Check query plans and insert latency against realistic successful-session volume and 30-day raw / 13-month aggregate volume. Verify a valid zero, `N/A`, query error, missing city series, and insufficient coverage are distinct.

Demonstrate a journey incident, degraded-time incident, fresh low-traffic stop failure, ingestion failure, collection no-data state, synthetic app failure, synthetic runner failure, and adequately observed recovery. Verify existing contact-point routing in staging. Keep diagnostic toast-volume alerts advisory initially. Record dashboard, database, browser, alert, and overhead evidence in `docs/observability/client-experience-runbook.md` before enabling operational routing.

## 6. Stage and roll out

Deploy the compatible server contract and migration before client emission. Verify production CORS/proxy, bootstrap API configuration, read-only dashboard grants, and Grafana Cloud browser-check access, probe cost, and runner-held synthetic routing. Enable capture gradually, watch delay/loss/unmatched attempts and client CPU/memory/queue bytes, and baseline provisional thresholds for at least two representative weeks before formal reliability objectives. Disabling capture stops new reports; disabling intake returns a retriable failure without false acknowledgement; stored history remains for review.

No deployment, alert notification, migration application, or Git commit is performed by this planning phase.
