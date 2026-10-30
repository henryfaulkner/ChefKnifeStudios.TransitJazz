# TransitJazz: economical Azure load test for 500 concurrent users

Date: 2026-10-25  
Status: Plan only. No resources provisioned and no load generated.  
Budget assumption: “low on cache” means a tight cash budget. Dollar amounts below are proposed limits in USD, not an approved spend or a live Azure quote.

## Recommendation

Run a small .NET load generator in one temporary Azure Container Instance (ACI), using the same SignalR client, MessagePack protocol, and shared message contracts as the app. Ramp to **500 concurrent connected, city-subscribed sessions**, hold for ten minutes, then disconnect and check recovery. Include realistic startup HTTP requests and occasional Stats interactions. Observe the actual app in two or three local browsers during the peak.

Target **under $5 incremental Azure spend for the first campaign**, with a **$10 planning ceiling** including retries, traffic, logs, and artifacts. Obtain subscription/region prices before execution; if the conservative estimate exceeds $10, shorten or revise the campaign before provisioning. These are spending objectives, not billing-enforced caps.

This measures the backend traffic of 500 active users. It does not emulate 500 rendering engines, audio devices, or independent internet connections. Browser checks cover visible regressions; this short test cannot establish all-day stability or global latency.

## Why this design fits the current app

Repository evidence reviewed:

| Evidence | Consequence for testing |
| --- | --- |
| `Client.Core/Services/SignalRNotificationService.cs` builds a MessagePack `HubConnection`, connects to `/hubs/transit`, joins a city, and receives `List<EventEnvelope>` | Ordinary HTTP request loops do not reproduce the main workload. Keep 500 real SignalR connections open and consume/decode deliveries. |
| `Server.WebAPI/SignalR/TransitHub.cs` exposes `JoinCityV2` and `LeaveCity`, replaying cached data on join | Count successful group joins and received data, not just socket opens. Exercise replay and rejoin. |
| `Client.Shared/ViewModels/ApplicationViewModel.cs` loads city route shapes during initialization | Each new simulated session must perform its startup fetch; steady listeners must not repeatedly download geometry. |
| `Shared/ApiEndpoints.cs` defines `/gtfs/routes/shapes` and POST `/stats/overlay/query` | Use these real endpoints and current DTOs; do not fabricate an HTTP workload. |
| Hidden/muted sessions leave their city group while keeping the connection open | Keep the primary test fully subscribed; test paused sessions separately so they cannot inflate the claimed active-user count. |
| `Server.WebAPI/appsettings.json` sets `CycleIntervalSeconds` to 10 | Measure deployed cadence; use ten seconds only as the initial expectation. |
| `bicep/modules/containerApp.bicep` hosts server/worker together and defaults to one replica | Preserve the deployed topology. Adding replicas changes worker ownership and in-memory group/cache behavior. |
| `specs/059-reported-vehicles/plan.md` describes shared worker/Stats resource limits | Include worker cadence and database impact, not only API latency. |

These are repository facts, not verification of the current Azure deployment. Older estimates in `EGRESS_REDUCTION_SMALL_SCALE.md` predate some implemented changes and must not be used as measured payload sizes or current prices.

## Azure resources and tool choice

Use a dedicated temporary resource group, for example `rg-transitjazz-loadtest-<date>`, containing one Linux ACI container group initially sized at **2 vCPU / 4 GiB**, with restart policy **Never**. Place it in the target application's region. Use outbound HTTPS to the public application ingress; the generator needs no public listening endpoint. Confirm regional availability and quota first.

Build the generator image locally and reuse the existing registry if available. Give the runner only the image-pull permissions it needs. Do not create another paid registry, database, monitoring workspace, NAT gateway, or managed SignalR service just for this test. Retain a small aggregate result file locally; temporary blob storage is optional only if needed to retrieve results reliably.

Prefer an existing representative nonproduction target. If none exists, a short, supervised run against the existing deployment avoids paying for a duplicate environment, but execution must explicitly identify production as the target and schedule a quiet window. Do not silently create a second worker connected to the production database. Record organic traffic separately: the goal is 500 synthetic sessions, with any real visitors additional to that load.

ACI is billed by requested compute/memory and duration; stopped container groups stop their compute meters. Verify the selected region and subscription prices using [ACI pricing](https://azure.microsoft.com/en-us/pricing/details/container-instances/) and the [ACI billing FAQ](https://learn.microsoft.com/en-us/azure/container-instances/container-instances-faq).

**Azure Load Testing alternative:** use it if managed reporting is worth the extra integration and quoted cost. It supports JMeter and Locust; the .NET harness cannot simply be uploaded as a native test. A replacement script would need verified SignalR negotiation, binary MessagePack framing, invocation completion, keepalives, and reconnect semantics. That adds avoidable fidelity work for this app. See [supported frameworks](https://learn.microsoft.com/en-us/azure/load-testing/overview-what-is-azure-load-testing) and [current pricing](https://azure.microsoft.com/en-us/pricing/details/app-testing/). Do not assume historical monthly fees or trial eligibility. A 500-user ten-minute plateau alone represents 83.3 virtual-user hours; ramps and other stages add usage. If choosing the managed service, also configure its [monthly usage limits](https://learn.microsoft.com/en-us/azure/app-testing/load-testing/how-to-manage-usage-limits).

## Workload contract

Implement a small console project under `tools/TransitJazz.LoadTest/`, referencing Shared and matching the app's .NET/SignalR package versions. Use the official [SignalR MessagePack client configuration](https://learn.microsoft.com/en-us/aspnet/core/signalr/messagepackhubprotocol?view=aspnetcore-10.0), rather than implementing a WebSocket protocol.

Each virtual user must:

1. Resolve its configured city using the same slug/group mapping as the client. Verify enabled cities against the deployed app.
2. Fetch `/gtfs/routes/shapes?city=<city>` once per new session, reproducing browser compression, caching, and conditional-request behavior. Validate status and decoded content. Capture one actual browser startup trace first to identify any other required calls and their ordering/concurrency.
3. Register the batch handler before connecting, negotiate normally at `/hubs/transit`, use MessagePack, invoke the current `HubMethods.JoinCity` constant (`JoinCityV2`), and verify the completion. Maintain session-specific cookies where required by ingress affinity.
4. Consume and decode batches continuously, counting records, arrivals, errors, and disconnects without retaining full payload histories. Track replay separately from subsequent live deliveries. A successful join with no live data does not count as an active listener.
5. Rejoin its city after reconnect, matching the real client's automatic-reconnect behavior. Report outages even if retry succeeds; do not let retries conceal failures. Handle initial connection failures explicitly with bounded attempts.
6. Stop and dispose connections promptly on cancellation or the absolute deadline.

Default traffic assumptions, to be replaced with observed usage if available:

| Behavior | Primary run |
| --- | --- |
| City allocation | Evenly distribute 500 users across deployed enabled cities, assigning the remainder deterministically. Record exact counts. This is a coverage scenario, not a claim about actual popularity. |
| Listener activity | All 500 remain subscribed throughout the measured plateau. |
| Stats | 20% of users issue one valid query every 60–120 seconds with seeded jitter; approximately 1.1 requests/second at peak. Use actual DTOs, with a mix of city and All scopes and valid route selections. Run only if the deployed feature is enabled. |
| Route filtering and audio | Follow the captured browser behavior; do not invent server requests for local-only actions. |
| Cache state | Start with new session caches and naturally warm server caches. Record ETags, compression, response sizes, and cache results. Do not clear production caches. |
| Agency feeds | Only the existing worker polls them. The generator never multiplies upstream agency requests by 500. |

Do not repeatedly fetch WASM assets, map tiles, or third-party services during the listening loop. The backend load test excludes bulk static-site and map-provider capacity; document these exclusions and inspect their startup behavior in the browser smoke test.

## Staged execution

First validate locally with one simulated user, then five. Confirm valid geometry, successful join, decoded replay/live batches, Stats response correctness, clean shutdown, and bounded memory. Verify that the load generator itself does not create per-user debug logging or unbounded result lists.

One primary Azure run:

| Stage | Duration | Synthetic users | Purpose |
| --- | --- | --- | --- |
| Baseline | 3 min | 0 | Capture target and upstream feed health before adding traffic. |
| Smoke | 2 min | 10 | Confirm Azure connectivity, protocol, and measurement correctness. |
| First ramp | 2 min | 10 → 100 | Find obvious startup and connection bottlenecks cheaply. |
| Hold | 2 min | 100 | Check generator and target headroom. |
| Second ramp | 3 min | 100 → 250 | Continue only if previous stage passes. |
| Hold | 2 min | 250 | Check worker health and delivery continuity. |
| Final ramp | 3 min | 250 → 500 | Include startup HTTP and replay load. |
| Measured plateau | 10 min | 500 | Establish simultaneous live-user behavior. |
| Drain | 1 min | 500 → 0 | Dispose sessions and measure release. |
| Recovery | 3 min | 0 | Verify application recovery and baseline trends. |

Total scheduled runtime: **31 minutes**. Hard runner deadline: **40 minutes**, including startup allowance. If it cannot reach 500, report the achieved concurrency and failure; do not extend automatically. With linear ramps, the schedule is approximately 129 active-user hours, useful for comparison with managed load-testing pricing; it is not ACI's billing unit.

Only after a passing primary run and a remaining-budget check, consider one short follow-up:

- **Largest-payload city:** ramp to 500 subscribed users on the city with the largest measured delivery volume, hold five minutes, drain. Do not assume a particular city is largest from old documents. This is necessary before claiming 500-user coverage independent of city mix.
- **Lifecycle:** at 500 connections, deliberately disconnect/reconnect 50 with jitter; then pause/resume a separate 50 using LeaveCity/rejoin. Measure recovery and replay. Keep these intervals outside the fully subscribed acceptance plateau.
- **Arrival spike:** separately test 500 fresh starts over one minute only if this burst is a real requirement and budget remains. The gradual ramp does not certify that burst.

Defer a long soak until a concrete failure or operational need justifies the extra spend.

## Measurements and pass/fail rules

Use existing Azure Monitor/Grafana views where available. Do not deploy a new observability stack. Export aggregate client measurements every ten seconds with run ID, stage, city, and bounded labels. Never log a line per vehicle or connection per tick. Record target image/revision, replica count, CPU/memory allocation, database tier, feature flags, city distribution, feed health, and UTC start/end times.

Collect:

- Attempted, connected, joined, actively receiving, reconnecting, and closed users; cumulative disconnects and total disconnected user-seconds.
- HTTP status and p50/p95/p99 per endpoint and stage; connect/join latency; time to first live batch; inter-batch gaps per user/city; decode failures.
- Generator CPU/memory, scheduling delay, network throughput and discarded measurements. If it saturates, the run is inconclusive, not an application failure. Resize or split into two 250-user runners only after revising the estimate.
- Target CPU/memory, restarts, ingress errors, network bytes, .NET GC/heap/thread-pool metrics if already available, worker cycle duration and successful publications per city.
- PostgreSQL CPU/connections/query latency and Stats timeouts/admission failures, plus existing capture/queue metrics where enabled.

Proposed initial acceptance gates (record any adjustment before running):

| Gate | Passing result |
| --- | --- |
| Concurrency | Reach 500 successfully joined users and sustain the scheduled ten-minute plateau; at least 99% connected/subscribed user-time and no user silently stuck without deliveries. Report actual minimum and median concurrency. |
| HTTP reliability | Less than 1% unexpected failures, per endpoint over the plateau; include 429/503, timeouts and failed attempts even if retries succeed. |
| HTTP latency | Route-shape complete-response p95 ≤2 seconds from the same-region runner; Stats p95 ≤2 seconds and no breach of its existing five-second deadline. Report cold/warm results separately. |
| Live delivery | For cities with healthy worker publications, at least 99% of subscribed user-time has a last live batch no older than three configured cycles (initially 30 seconds). Inspect outliers individually. |
| Worker impact | Worker cycle p95 ≤1.2× the pre-load baseline, no sustained backlog or load-correlated publication loss. This exploratory threshold does not replace stricter feature-specific release budgets. |
| Resource headroom | Target and generator sustained CPU <80%, memory <85% of allocation, no restart/OOM, and no persistent resource growth after drain. Short spikes are recorded separately. |
| Browser experience | Two or three real sessions retain live map updates, responsive controls and expected audio during peak load; record devices/browsers and any audio gaps. |

An upstream feed outage makes delivery results for that city inconclusive unless server publication evidence proves otherwise. Inter-arrival gaps alone do not establish end-to-end latency or loss: use existing message timestamps/identifiers and server publication counts where available; otherwise disclose the missing measurement. Empty successful batches must not be confused with transport failure.

Abort automatically if target restarts/OOMs, unexpected HTTP failures exceed 5% for 60 seconds, target CPU exceeds 90% or memory 90% for two minutes, or more than 5% of subscribed users miss three cycles while healthy publications continue. Abort on generator saturation as an invalid run. Stop manually on visible real-user degradation. Wire these rules to accessible metrics before the run; where polling is unavailable, an operator must watch the corresponding signal and stop the runner. Failure to collect required evidence is not a pass.

## Optional second phase: resilience under load with Chaos Studio

Azure Chaos Studio complements the load generator: the generator supplies user traffic; Chaos Studio introduces a supported infrastructure fault. Preserve the clean 500-user baseline before injecting any fault. Report capacity and fault recovery as separate results.

### Support check before selecting an experiment

Documentation checked on 2026-10-25 introduces an important qualification: Microsoft directs new tests to Chaos Studio Workspaces; Workspaces/Scenarios are currently public preview and documented as not intended for production. The current catalog includes PostgreSQL Flexible Server failover in a composite scenario and Azure Load Testing integration in MySQL scenarios. It does not establish a direct Container Apps restart action. Verify the exact action, resource type, region, database configuration/HA prerequisites, permissions, and rollback behavior before selecting anything. Do not assume a PostgreSQL database is an Azure Flexible Server. See the [current Scenario catalog](https://learn.microsoft.com/en-us/azure/chaos-studio/chaos-studio-scenarios).

The [classic fault library](https://learn.microsoft.com/en-us/azure/chaos-studio/chaos-studio-fault-library) documents starting/stopping Azure Load Testing tests, but explicitly says classic entries do not establish Workspaces availability. That integration does not start the custom ACI/.NET harness. Keep the harness independently supervised and coordinate fault start only after it reports stable achieved concurrency. There is no need to adopt Azure Load Testing solely to add a fault.

### Smallest useful experiment

| Candidate | What it establishes | Decision |
| --- | --- | --- |
| Disconnect/reconnect 50 simulated listeners | Client rejoin, replay and delivery recovery | Cheapest first step; use the existing lifecycle scenario. This is client churn, not an infrastructure outage or a Chaos Studio experiment. |
| Brief supported application/network interruption | Recovery of established SignalR sessions after an actual outage | Only use an action proven applicable to the deployed topology. VM/AKS/App Service actions are not interchangeable with Container Apps actions. |
| Supported PostgreSQL interruption/failover | Stats degradation and database recovery while listeners remain active | Nonproduction only for this optional phase; requires a matching database resource and supported action. Do not buy HA or deploy a new database solely to enable the test. |

Prefer one fault on an existing representative nonproduction environment. If no suitable resource/action exists within the budget, retain the client-churn result and defer managed chaos testing. A narrowly scoped native Azure restart could be a separate recovery drill, but must be documented as such rather than claimed as Chaos Studio support. A server restart also restarts the embedded worker and its route-index warmup, so it requires its own realistic readiness deadline.

### Execution and recovery criteria

After the baseline passes, use a separate run with an explicit target resource ID and least-privilege experiment identity. First rehearse at 10–50 users. If that succeeds and budget remains, ramp to 500, observe two stable minutes, inject one reversible fault, restore, and observe recovery for up to five minutes. Limit a duration-based fault to 30–60 seconds where the action supports that duration. Discrete restarts/failovers cannot be assumed to finish or reverse in 60 seconds; establish their bounds before scheduling. Skip any action whose likely recovery cannot fit the run's fixed deadline.

Record fault requested/effective/removed timestamps, service-ready time, disconnected user-seconds, reconnect attempts, first fresh batch after restoration, Stats errors/timeouts, and worker/database recovery. Verify that the fault actually interrupted the intended path; a rule that leaves existing sockets unaffected is not evidence of outage resilience.

Proposed recovery gate: after service readiness is restored, at least 99% of affected listeners rejoin and receive fresh live data within 60 seconds without manual reload. Report total outage from fault onset separately so a long warmup cannot disappear from the result. For database faults, exercise a naturally uncached valid Stats query so cached responses do not hide the outage; expect bounded failure within the existing five-second deadline, truthful unavailable presentation, and successful reads within 60 seconds after database readiness. Check that worker queues stay bounded and recovery does not create duplicate capture ownership or falsely complete statistics.

Keep the baseline abort rules unchanged. For the separate chaos run, identify only the expected, time-bounded interruption signals that will be evaluated as recovery outcomes instead of immediate aborts. Unexpected resource exhaustion, unrelated failures, or expiration of the recovery deadline still stop the experiment. Stopping the load generator does not remove a fault: the independent supervisor must cancel the experiment and verify restoration, with a documented manual restoration path. Do not assume cancellation reverses a discrete action.

### Added cost and cleanup

Chaos Studio charges by action-minute, in addition to generator runtime and affected services. Quote `sum(billable action durations × applicable rate)` and confirm minimums/rounding for the selected actions. See [Chaos Studio pricing](https://azure.microsoft.com/en-us/pricing/details/chaos-studio/). Concurrent actions can increase charges; use one fault and no automatic repeats.

The combined baseline plus optional recovery work must remain within the proposed $10 campaign ceiling. Re-estimate before the optional run, including rehearsal, warmup, recovery, logs and any orchestration charges. If the ceiling cannot cover it, defer this phase. Extend neither the running container's deadline nor the budget silently. Save recovery evidence alongside the baseline report, verify target restoration, and remove test-only Chaos resources and role assignments after export.

## Cost worksheet and guardrails

Fill this out using the actual subscription/region before provisioning:

| Cost | Calculation / control |
| --- | --- |
| ACI | `groups × seconds × (requested_vCPU × price_per_vCPU_second + requested_GiB × price_per_GiB_second)`, plus any applicable meter. Quote at least a full hour initially, despite the 40-minute deadline. |
| Target | Incremental application/database billing, if applicable. Keep replica counts and tiers unchanged; existing fixed spend is not a new test charge. |
| Traffic | Startup geometry + live batches + reconnect replay + protocol overhead; apply the actual region/routing tariffs. Same-region traffic is not automatically a universal zero-cost guarantee. |
| Logging | Estimate added ingestion from server join/request logs and aggregate runner output; avoid verbose SignalR logging. |
| Build/artifacts | Local image build preferred; account for registry storage/build charges and optional short-lived blob storage. |
| Optional chaos phase | Quoted action-minute charges plus additional runner/target runtime and orchestration; included within the campaign ceiling, not an additional allowance. |
| Contingency | Reserve at least half the $10 ceiling for uncertainty or one justified retry. No unattended repeat campaigns. |

Measure compressed geometry bytes and average received bytes/user/second in the smoke test. Project `startup_sessions × geometry_bytes + subscribed_user_seconds × mean_stream_bytes_per_user_second`, including observed replay overhead. For illustration only, 60 KB every ten seconds to 500 users for ten minutes is 1.8 GB before startup/protocol overhead; actual payloads may differ substantially. Do not extrapolate old repository estimates as observations.

Apply a conservative traffic-cost estimate to the remaining stages before proceeding to 500. If the estimate no longer fits, stop and retain the smaller-run findings.

Use both a process-wide cancellation deadline and an external supervisor that stops the specific ACI resource at 40 minutes even if the harness hangs. Configure the supervisor before launch and verify its access. Tags such as `purpose=loadtest`, `run-id`, and `expires-at` help identify resources but do not delete them. Azure budget alerts may arrive after spend and are not a real-time kill switch.

After export, stop the container group, verify the stopped state, and remove the dedicated temporary resource group by its exact recorded ID. Never delete the target application's group or shared registry. Remove only the test image/artifacts that are no longer needed. Check Cost Management after charges appear and record actual incremental spend; immediate portal totals may be incomplete.

## Implementation handoff

1. Capture one browser session and confirm deployed topology, city mapping, endpoints, feature flags, metrics access, and target region. Record all assumptions and the price worksheet.
2. Add the bounded .NET harness, scenario configuration, Dockerfile and result schema. Include `max-users=500`, fixed random seed, absolute duration, target-host allowlist and stage gating.
3. Add a small ACI deployment/cleanup script or Bicep module and an independent deadline supervisor. Keep resource IDs explicit and outputs free of credentials.
4. Verify protocol fidelity with local smoke sessions and verify the runner exits on failure/deadline. Confirm planned aggregate metrics distinguish replay, live traffic and reconnects.
5. Run the staged Azure campaign during the selected window, observing the browser sessions and stopping at any failure gate.
6. Save `docs/load-tests/<run-id>/report.md` with configuration, stage results, latency percentiles, live-delivery evidence, resource charts, cost estimate/actuals, limitations, and pass/fail/inconclusive conclusion. Export compact machine-readable aggregates alongside it.
7. Confirm cleanup. Recommend the smallest evidence-supported fix if needed, then rerun only affected scenarios within a refreshed budget.

Completion means a reviewable result for **500 simultaneous listeners under the recorded city mix and deployment**, plus recovery and cost evidence. The plan itself does not certify capacity. No commits should be created by the implementing agent.
