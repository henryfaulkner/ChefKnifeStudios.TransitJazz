# Marta Jazz — Azure Bicep IaC

Modular Bicep for the Marta Jazz environment. Subscription target: **ChefKnifeStudios**.

## Layout

```
main.bicep                          # Subscription-scoped orchestrator (creates RG + modules)
main.dev.bicepparam                 # Dev parameters
main.prod.bicepparam                # Prod parameters
modules/
  staticWebApp.bicep                # SWA + custom domains
  dnsZone.bicep                     # Public DNS zone + A-alias apex + www CNAME
  managedIdentity.bicep             # User-assigned MI for the server
  acrRoleAssignment.bicep           # AcrPull on existing chefknife ACR
  containerAppsEnvironment.bicep    # Container Apps Environment (Azure Monitor destination)
  logAnalyticsDiagnosticSettings.bicep # Environment console/system routing
  logAnalyticsTablePolicies.bicep  # Basic/Analytics plans and 30-day retention
  workspaceRoleAssignment.bicep     # Workspace-scoped Log Analytics Reader
  containerApp.bicep                # Server Container App (API/SignalR/Worker)
```

## Resource names (per spec)

| Resource | Name |
|---|---|
| Resource Group | `marta-jazz-{env}-rg` |
| Static Web App | `marta-jazz-{env}-swa` |
| DNS Zone | `martajazz.com` (intentionally breaks convention) |
| Container Registry | `chefknife` (existing, shared) |
| Container Apps Env | `marta-jazz-{env}-cae` |
| Server Managed Identity | `marta-jazz-{env}-ca-server-mi` |
| Server Container App | `marta-jazz-{env}-ca-server` |

Tags applied everywhere: `env:{dev|prod}`, `project:marta-jazz`.

## Gaps to fill before deploy

Search the codebase for `TODO:` — those are the holes. Quick inventory:

**`main.bicep`**
- `containerRegistryResourceGroup` — RG holding the existing `chefknife` ACR
- `serverImageTag` — image tag to deploy (pin in prod)
- `repositoryUrl` / `repositoryToken` — GitHub source for SWA
- Server Container App `targetPort` — confirm the port your .NET app listens on (default 8080)
- Worker Service Container App URL — append to `corsAllowedOrigins` once provisioned
- `envVars` for the server container — connection strings, etc.

**`staticWebApp.bicep`**
- `appLocation`, `apiLocation`, `outputLocation` build properties — match your repo layout

**`containerAppsEnvironment.bicep`**
- Azure Monitor is the configured destination; the environment diagnostic setting owns the workspace route.

**Centralized logging (feature 054)**
- `logAnalyticsReaderPrincipalId` is intentionally empty by default and must be supplied only after approval.
- `enableLegacyTelemetry` remains `true` through the seven-day dual-run evidence window.
- `ContainerAppConsoleLogs` is Basic with 30-day total retention; `ContainerAppSystemLogs` is Analytics with 30-day interactive and total retention.
- Apply table policies after the diagnostic route materializes the resource-specific tables; validate with `az deployment sub validate` and `az deployment sub what-if` before deployment.
- No application SDK, workspace key, connection string, ingress, or secret is added for log delivery.

**`containerApp.bicep`**
- Liveness / readiness probes
- Secret references (Key Vault) if needed
- Scale rules if `min != max` later

**`dnsZone.bicep`**
- `www` CNAME currently points to `{swa-name}.azurestaticapps.net`. Azure sometimes assigns region-suffixed default hostnames — verify after first SWA deploy and update if needed.

## GitHub Actions secrets

The CI/CD workflows require four secrets set in GitHub → Settings → Secrets and variables → Actions:

| Secret | How to obtain |
|---|---|
| `AZURE_STATIC_WEB_APP_TOKEN` | Azure Portal → Static Web App → Manage deployment token |
| `ACR_USERNAME` | Azure Portal → Container Registry `chefknife` → Access keys → Username |
| `ACR_PASSWORD` | Azure Portal → Container Registry `chefknife` → Access keys → Password |
| `AZURE_CREDENTIALS` | See below |

Generate `AZURE_CREDENTIALS` (service principal scoped to the prod resource group):

```bash
az ad sp create-for-rbac --name "github-actions-transitjazz" --role Contributor --scopes /subscriptions/<subscription-id>/resourceGroups/marta-jazz-prod-rg --sdk-auth
```

Paste the full JSON output as the `AZURE_CREDENTIALS` secret value.

Also create a `production` GitHub Environment in repository Settings → Environments — the deploy jobs gate on it.

## Deploy

```bash
# Login & select subscription
az login
az account set --subscription ChefKnifeStudios

# Validate
az deployment sub validate --location eastus2 --template-file main.bicep --parameters main.dev.bicepparam

# What-if
az deployment sub what-if --location eastus2 --template-file main.bicep --parameters main.dev.bicepparam

# Deploy
az deployment sub create --location eastus2 --template-file main.bicep --parameters main.dev.bicepparam
```

## Notes on the design

- **Subscription-scoped main** so the resource group is created as part of the same deployment.
- **DNS apex uses an A-alias `targetResource`** to the SWA — this is required for an apex (root) domain to point at a Static Web App, since CNAMEs aren't allowed at the apex.
- **ACR pull uses managed identity**, not admin user or stored creds. `AcrPull` is granted to the server MI scoped to the existing registry's RG.
- **Session affinity is enabled** on ingress (SignalR-friendly). `allowCredentials: true` is set on CORS to match; if you don't need credentialed CORS, drop it.
- **AcrPull role assignment is a `dependsOn`** of the container app — the app's first pull will fail if the role isn't propagated yet.
- **DNS zone is global**; everything else is in `eastus2`.
- **Custom domain validation order**: the apex uses `dns-txt-token` (default) and `www` uses `cname-delegation`. The apex A-alias must resolve before SWA can validate; if you hit a race, redeploy or trigger a re-validation.
