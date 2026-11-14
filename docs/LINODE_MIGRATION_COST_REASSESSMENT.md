# Azure and Linode hosting: cost findings and tradeoffs

**Date:** 2026-11-12
**Status:** Decision support; neither option is selected by this assessment. No infrastructure changes were performed for it.

This assessment records the owner's billing screenshots and the subsequent discussion. Read it alongside the [migration design](LINODE_SERVER_MIGRATION_DESIGN_DOCUMENT.md) and [execution guide](LINODE_MIGRATION_STEP_BY_STEP.md). It replaces their earlier approximately $30/month egress assumption for decision-making; that estimate has been withdrawn. Their implementation instructions remain separate from this assessment.

## Evidence and limits

| Finding | Evidence and qualification |
| --- | --- |
| Server charges were $49.03 for July–September 2026 | Owner-provided Azure Cost Analysis screenshot for `marta-jazz-dev-ca-server`; average $16.34/month. This is a historical baseline, not a current-month forecast. |
| The displayed server charges are compute charges | Expanded meters show active CPU $33.79, active memory $8.46, idle memory $4.52, and idle CPU $2.25. Displayed rounded components total $49.02; the resource total is $49.03. |
| No egress charge has been demonstrated | A separate screenshot filtered to `Standard Bandwidth Usage` at resource-group scope shows no charge. This specific filter does not prove that every possible transfer meter at subscription scope is zero. The earlier design also records no material Bandwidth charge in its dated investigation. |
| Current configured server allocation is small | Screenshot shows 0.25 vCPU and 0.5 GiB RAM. Owner reports minimum and maximum replicas both set to one, with a continuously running worker. Allocation is not a measurement of peak demand, restarts, or future capacity. |
| PostgreSQL already runs on Linode | Owner-confirmed layout. The current Linode plan screenshot shows Nanode 1 GB at $5/month. |
| Proposed migration uses the existing VM | Resize that VM and add the combined API/SignalR/worker container. Retain Azure website hosting, DNS, registry, Key Vault, and other required services initially. |
| Operational tolerance is relatively high | Owner describes a solo hobby project and accepts substantial maintenance downtime. Outage tolerance does not make missing historical transit observations recoverable. |

These findings do not establish current outbound byte volume, combined app/database peak resource use, or the cost of future traffic. No fresh billing API query, VM inspection, or load test was performed for this assessment.

## Why one replica can have varying compute costs

On Azure Container Apps Consumption, a minimum replica can switch between active and cheaper idle billing. A worker can remain running while waiting between cycles. Startup, request processing, and CPU/network activity affect billing state. Charges are based on allocated resources at the applicable rate, rather than a continuously proportional bill for actual CPU utilization. Fixed replica count therefore does not guarantee an identical monthly bill; active/idle time, month length, configuration changes, and subscription free allowances can affect it. The screenshot contains both active and idle meters. See [Azure billing](https://learn.microsoft.com/azure/container-apps/billing) and [Container Apps pricing](https://azure.microsoft.com/en-us/pricing/details/container-apps/).

More users can increase active time, request counts, and outbound bytes. Once a fixed-size replica is active throughout the month, further utilization does not by itself continually increase its allocated-resource compute rate. Capacity may eventually require a larger allocation or more replicas. Egress and request charges remain separate considerations.

## Cost comparison at the historical workload

Compare costs that change. Existing $5/month Linode compute is already paid under the Azure option, so the migration cost is the resize increment. Other retained Azure services are excluded from both sides.

| Option | Azure app/month | Linode compute/month | Combined subtotal/month | Savings versus staying/month |
| --- | ---: | ---: | ---: | ---: |
| Stay on Azure; keep current Linode | $16.34 | $5.00 | $21.34 | — |
| Move app; resize Linode to 2 GB / 1 shared vCPU | $0.00 | $12.00 | $12.00 | $9.34 |
| Move app; resize Linode to 4 GB / 2 shared vCPUs | $0.00 | $24.00 | $24.00 | -$2.66 |
| Move app; resize Linode to 8 GB / 4 shared vCPUs | $0.00 | $48.00 | $48.00 | -$26.66 |

Assumptions: the full historical Azure app charge disappears; full-month listed Linode pricing applies; no additional transfer, backup, storage, or monitoring charges. Taxes, migration effort, temporary overlap, and operator time are excluded. Annualized 2 GB savings are approximately $112 under those assumptions. These are not total project bills.

The 2 GB plan lists 2 TB included transfer; larger plans and regions have different allowances. Included transfer is finite and may be shared with other account traffic. Verify the chosen region, SKU, and account pool before purchase. [Akamai pricing](https://www.akamai.com/cloud/pricing/cpu).

Azure's general internet-egress pricing includes a first 100 GB/month free allowance, with billing-scope and service rules applying. Do not assign a separate allowance to each app. No future egress savings can be quantified without measuring traffic and the allowance actually available. [Azure bandwidth pricing](https://azure.microsoft.com/en-us/pricing/details/bandwidth/).

## Operational advantages and disadvantages

| Consideration | Stay on Azure Container Apps | Move onto the existing Linode DB VM |
| --- | --- | --- |
| Present cost | Keeps the historical app expense and existing DB VM expense. No migration effort. | Lower listed compute subtotal if 2 GB suffices; 4 GB or 8 GB costs more at the historical baseline. |
| Traffic growth | Transfer can become chargeable; fixed replica capacity still needs monitoring. | Included transfer gives a larger stated traffic budget, subject to pool usage and overage. Memory or CPU may force a resize before transfer becomes limiting. |
| Hosting responsibility | Retains managed container hosting, existing deployment integration, identity-based secret access, and log collection. | Requires proxy/TLS configuration, app recovery, registry credentials, secret delivery, log collection, and host capacity management. Existing Linux/DB administration is already the owner's responsibility. |
| Database connectivity | Keeps a cross-cloud network path and its authentication/firewall dependencies. App and DB compute remain separate. | A local DB path removes that network hop, but must preserve suitable authentication and configuration. |
| Resource isolation | Worker/app load does not directly consume DB host RAM, CPU, or disk. | Shared resources can improve utilization; startup, feed refresh, and DB maintenance can also interfere with one another. |
| Failure and recovery | DB host failure already disrupts DB-dependent capabilities, while app hosting remains separate. | Host failure or reboot takes down app and DB together. Azure app rollback cannot repair a failed database host. Separate DB backups and recovery remain necessary. |
| Deployments | Existing pipeline and rollback mechanism remain; no endpoint cutover. | A simple container deployment is possible, but migration needs workflow changes, endpoint publication to both sites, and tested app rollback. DB volumes must remain outside app cleanup. |
| Scaling | Replica/resource settings can be changed later. Multiple production workers need design review; hosting features do not solve writer coordination. | VM resizing is straightforward conceptually but may interrupt both workloads. Splitting services or adding hosts later introduces additional work. |
| Vendor dependencies | Current cross-cloud arrangement remains. | Consolidates application compute with its DB, but retains several Azure dependencies; this is not a complete Azure exit. |
| Operator time | Preserves current routines and leaves more time for product work. | Provides more direct control and operational learning, with additional maintenance and recovery work. Its value depends on the owner's priorities. |

## Conditions that favor each option

Staying is favored when current costs are acceptable, product development is the priority, app/DB isolation is useful, or shared-host sizing would require a plan that exceeds the historical combined cost.

Migrating is favored when measured combined demand fits an economical plan, consolidation and direct control are desirable, downtime is acceptable, and the owner is willing to maintain the app hosting stack. Sustained chargeable Azure egress could strengthen its economics, but has not been established here.

Neither the earlier 8 GB headroom recommendation nor the later 2 GB pilot suggestion is a measured sizing result. The app's 0.5 GiB allocation and DB's current 1 GB host make 2 GB a candidate to evaluate, not a capacity guarantee.

Before choosing, inspect current-month billing and subscription transfer meters; measure app and DB peaks during startup, feed refresh, normal operation, and DB maintenance; verify off-host restore and recovery; and price any added backups/monitoring. If migrating, validate resource limits, one production writer, client reconnect, and full capture-hour behavior before treating the pilot as successful.

The evidence supports modest potential savings on a 2 GB shared host and a trade of managed hosting for direct operational control. It does not establish an urgent cost problem, guaranteed 2 GB capacity, or an objectively superior hosting option.
