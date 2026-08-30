# Quickstart: Centralized Structured Logging

## Purpose and safety boundary

This feature moves *new, sparse worker explanations* to Azure Monitor/Log Analytics while keeping Grafana metrics authoritative. It does not authorize a production routing change, a Parquet deletion, an Azure administration skill, or handling credentials in prompts/output. Treat the contracts in this directory as the source of truth for event shape, read-only investigation, routing, and cutover gates.

## Local implementation checks

1. Run the real worker test project, not an orphan test folder:

   ```powershell
   dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.csproj
   ```

2. Run Web API host tests:

   ```powershell
   dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.csproj
   ```

3. Confirm local JSON logging tests cover every v1 event/reason, `CycleId`, no normal-cycle duplicate, coalescing/recovery with a fake clock, redaction of credential-bearing input, and no change to Grafana metric behavior. Do not treat local formatter output as proof of the final Azure `Log` shape.

4. Add the canonical skill under `skills/transitjazz-logs/`, register it, then synchronize generated copies:

   ```powershell
   .\tools\sync-skills.ps1
   ```

Never author generated `.agents/skills` copies directly. Never inspect, echo, or pass legacy MCP configuration/credentials while creating the new skill.

## Pre-routing evidence

Before changing production routing, release owners record:

- the feature 053 carried constitutional prerequisites;
- the intended workspace-scoped `Log Analytics Reader` principal ID;
- current legacy console ingestion, noisy categories/messages, workspace/retention, and a cost/scan estimate;
- an Azure read-only Basic-table compatibility result and its selected interface/fallback;
- an actual, secret-free console JSON canary record to lock final KQL parsing; and
- a safe pre-change timestamped canary.

Use read-only access only. Empty results immediately after a routing change are not proof of failure: diagnostic settings can take up to 90 minutes to activate and logs take several minutes to ingest.

## Infrastructure validation

After implementation changes, generate and validate deployment artifacts from Bicep rather than editing `main.json`:

```powershell
az bicep build --file bicep/main.bicep
az deployment sub validate --location eastus2 --template-file bicep/main.bicep --parameters bicep/main.dev.bicepparam
az deployment sub what-if --location eastus2 --template-file bicep/main.bicep --parameters bicep/main.dev.bicepparam
```

Use the normal reviewed deployment process to apply a change. Verify the managed-environment
diagnostic setting routes exactly `ContainerAppConsoleLogs` and `ContainerAppSystemLogs`, console
is Basic, system is Analytics, both retain 30 days, and the reader principal has only the required
workspace role. Keep `Logging:Telemetry` and Blob access active during dual run.

## Read-only investigation acceptance path

1. Begin with explicit workspace/table/range/selector input when supplied. Otherwise, pass a
   Grafana panel link through `$grafana` and use its effective city/time context.
2. Use `EventId` first, then `CycleId` plus revision, then city and a narrow UTC range.
3. For console logs, query only `ContainerAppConsoleLogs`; use a finite `TimeGenerated` range,
   parse the captured `Log` format, project useful columns, and take 1-100 rows.
4. Report the effective workspace, table, UTC range, KQL, limit, and a concise table. Return JSON
   only if requested.
5. On failure, run `doctor` once and report its first failure plus a secret-free next action. Do
   not retry persistent errors and do not modify Azure resources.

## Cutover checklist

Keep both evidence paths active for seven consecutive days. The gate requires a retained day-one
safe event, input/publish/zero-tone parity, redaction, table/retention/routing proof, Basic query
proof, skill/doctor/Grafana investigation proof, and cost review. Then disable new Parquet writes,
observe one normal centralized-logs-only release, and perform a consumer audit. A later reviewed
cleanup may remove only confirmed Parquet-only code/resources; historical blobs remain until a
separate archival/deletion decision.
