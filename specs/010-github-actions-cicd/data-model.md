# Data Model: GitHub Actions CI/CD Pipelines

This feature has no application data model — it is pure CI/CD infrastructure. The relevant "entities" are the configuration objects that the workflows consume.

## Workflow Configuration

### Client Workflow (`client.yml`)

| Field | Value |
|-------|-------|
| Trigger | `push` to `main` |
| Runner | `ubuntu-latest` |
| Build project | `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/ChefKnifeStudios.MartaJazz.Client.WebApp.csproj` |
| Publish configuration | `Release`, `/p:BlazorEnableCompression=false` |
| Artifact output path | `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/bin/Release/net10.0/publish/wwwroot` |
| Artifact name | `blazor-client` |
| Environment header | `{"headers":{"Blazor-Environment":"Production"}}` |
| Deploy target | Azure Static Web App (via `AZURE_STATIC_WEB_APP_TOKEN`) |
| GitHub Environment | `production` |

### Server Workflow (`server.yml`)

| Field | Value |
|-------|-------|
| Trigger | `push` to `main` |
| Runner | `ubuntu-latest` |
| Registry | `chefknife.azurecr.io` |
| Image name | `chefknifestudios.martajazz.server.webapi` |
| Image tags pushed | `{github.sha}`, `latest` |
| Build command | `docker compose build` |
| Container App name | `marta-jazz-prod-ca-server` |
| Resource group | `marta-jazz-prod-rg` |
| GitHub Environment | `production` |

## GitHub Secrets

| Secret | Consumed by | Purpose |
|--------|-------------|---------|
| `AZURE_STATIC_WEB_APP_TOKEN` | `client.yml` deploy job | Authenticates to the Static Web App |
| `ACR_USERNAME` | `server.yml` build-and-push job | ACR admin username |
| `ACR_PASSWORD` | `server.yml` build-and-push job | ACR admin password |
| `AZURE_CREDENTIALS` | `server.yml` deploy job | Azure service principal JSON for CLI login |

## GitHub Environment

| Field | Value |
|-------|-------|
| Name | `production` |
| Used by | Both `client.yml` deploy job and `server.yml` deploy job |
| Protection rules | Optional (approval gate configurable post-setup) |
