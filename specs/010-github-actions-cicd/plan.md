# Implementation Plan: GitHub Actions CI/CD Pipelines

**Branch**: `infra/bicep` | **Date**: 2026-05-23 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/010-github-actions-cicd/spec.md`

## Summary

Replace the existing Azure DevOps pipeline definitions in `deploy/` with equivalent GitHub Actions workflows in `.github/workflows/`. The client workflow builds the Blazor WASM app, injects a production environment header, and deploys to Azure Static Web App. The server workflow builds the Docker image via `docker compose`, tags it with both the commit SHA and `latest`, pushes to ACR, and updates the Azure Container App. Both workflows use a `production` GitHub Environment and store all credentials in GitHub Secrets.

This feature also requires a **Constitution Amendment** to Principle V, replacing "Azure DevOps" with "GitHub Actions" as the mandated CI/CD platform.

## Technical Context

**Language/Version**: YAML (GitHub Actions workflow syntax)
**Primary Dependencies**: GitHub Actions marketplace — `actions/checkout@v4`, `actions/setup-dotnet@v4`, `actions/upload-artifact@v4`, `actions/download-artifact@v4`, `docker/login-action@v3`, `azure/login@v2`, `Azure/static-web-apps-deploy@v1`, `azure/cli@v2`
**Storage**: N/A
**Testing**: Manual end-to-end verification (push to `main`, observe Actions tab)
**Target Platform**: GitHub Actions (ubuntu-latest runners)
**Project Type**: CI/CD infrastructure (YAML workflow files)
**Performance Goals**: Client deployment ≤10 min end-to-end; Server deployment ≤15 min end-to-end
**Constraints**: No secrets hardcoded in YAML; all credentials via GitHub Secrets; `production` environment must exist in repo settings before first run
**Scale/Scope**: Two workflows; four GitHub Secrets; one GitHub Environment

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Decoupled Cloud Architecture | ✅ Pass | Workflows deploy independently to SWA (client) and Container App (server) — decoupling preserved |
| **II. No Frontend Secrets** | ✅ Pass | Deployment token and ACR credentials stay in GitHub Secrets, never in WASM bundle |
| III. Two-Pass Real-Time Pipeline | ✅ Pass | No changes to application code; pipeline change only |
| IV. OpenTelemetry Observability | ✅ Pass | No changes to observability stack |
| **V. Azure DevOps CI/CD Pipeline** | ⚠️ **VIOLATION — AMENDMENT REQUIRED** | Principle V mandates Azure DevOps. This feature migrates to GitHub Actions. Amendment must be ratified before or concurrent with implementation. |
| VI. GTFS ID Mapping | ✅ Pass | No data model changes |

### Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| Constitution V amendment (Azure DevOps → GitHub Actions) | Source control already lives on GitHub; GitHub Actions eliminates the cross-platform friction of maintaining Azure DevOps service connections alongside GitHub PRs. Fewer moving parts, tighter integration with the git host. | Keeping Azure DevOps requires maintaining two platforms (GitHub for source + ADO for CI), separate service connections, and separate secrets management. The reduction in operational surface outweighs the cost of the amendment. |

## Project Structure

### Documentation (this feature)

```text
specs/010-github-actions-cicd/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
.github/
└── workflows/
    ├── client.yml       # NEW: Blazor WASM → Static Web App
    └── server.yml       # NEW: Docker → ACR → Container App

deploy/                  # EXISTING (Azure DevOps pipelines — kept for reference, not deleted)
├── client-pipeline.yml
└── server-pipeline.yml

constitution.md          # AMEND: Principle V (Azure DevOps → GitHub Actions)
```

**Structure Decision**: GitHub Actions workflows live at `.github/workflows/` per platform convention. Existing `deploy/` ADO files are retained as reference — not deleted in this feature.

---

## Phase 0: Research

### Decision 1: GitHub Actions action versions

**Decision**: Pin all actions to `@v4` (checkout, setup-dotnet, upload/download-artifact) and `@v3` (docker/login-action) per current stable releases. `azure/login@v2` and `Azure/static-web-apps-deploy@v1` match current stable.

**Rationale**: Pinning to major versions (not SHAs) balances stability with automatic patch-level security updates. The existing ADO pipelines used latest task versions; parity here is sufficient.

**Alternatives considered**: SHA pinning for supply-chain security — rejected as overkill for a personal project with no public consumers.

---

### Decision 2: ACR authentication method

**Decision**: Use ACR admin credentials (`ACR_USERNAME` / `ACR_PASSWORD`) via `docker/login-action@v3` with `registry: chefknife.azurecr.io`.

**Rationale**: The existing ADO pipeline used a service connection backed by service principal credentials. For GitHub Actions, ACR admin credentials stored as GitHub Secrets are the simplest equivalent and do not require OIDC federation setup.

**Alternatives considered**:
- **Workload Identity Federation (OIDC)**: Zero long-lived credentials, but requires configuring a federated identity credential in Azure AD — additional infrastructure overhead for a personal project.
- **Service principal with `azure/login` + `az acr login`**: Requires `AZURE_CREDENTIALS` JSON and an extra step; ACR admin creds are simpler when the registry already has admin access enabled.

---

### Decision 3: Container App update mechanism

**Decision**: Use `azure/cli@v2` to run `az containerapp update --name ... --resource-group ... --image ...` after a successful push.

**Rationale**: The `AzureContainerApps@1` ADO task has no direct GitHub Actions equivalent maintained by Microsoft. `azure/cli@v2` runs any `az` command and is the official approach for Azure CLI operations in GitHub Actions.

**Alternatives considered**: `azure/container-apps-deploy-action` — exists but is less stable and less documented than raw `az` CLI. The CLI approach is explicit and auditable.

---

### Decision 4: Blazor Static Web App deploy action

**Decision**: Use `Azure/static-web-apps-deploy@v1` with `skip_app_build: true`, `app_location: ./client`, and `output_location: ""` (empty string signals pre-built output).

**Rationale**: The artifact already contains the fully built `wwwroot` output. Setting `skip_app_build: true` and empty `output_location` tells the action to deploy the directory as-is, matching the ADO pipeline's behavior of deploying a pre-built artifact.

**Alternatives considered**: Azure CLI `az staticwebapp` — no direct upload command exists; the `static-web-apps-deploy` action is the only supported mechanism.

---

### Decision 5: `staticwebapp.config.json` placement

**Decision**: Write `staticwebapp.config.json` directly into `./client/` (the artifact root, which is `wwwroot`), replacing any existing file.

**Rationale**: The Static Web App action reads `staticwebapp.config.json` from the app root. The existing ADO pipeline writes it there. The artifact output from `dotnet publish` does not include a `staticwebapp.config.json` at the root level, so no merge logic is needed.

**Alternatives considered**: Reading and merging existing config — unnecessary because publish output is clean.

---

### Decision 6: Constitution Principle V amendment wording

**Decision**: Amend Principle V to read: "Source control MUST be managed in GitHub. CI/CD pipelines MUST be managed via **GitHub Actions**. ..." (replacing "Azure DevOps").

**Rationale**: This is a direct platform substitution. The rest of the principle (two build artifacts: WASM + Docker image) is unchanged.

---

### Decision 7: Resource names

**Decision**: Use Bicep-aligned production resource names:
- Resource group: `marta-jazz-prod-rg`
- Container App: `marta-jazz-prod-ca-server`
- ACR: `chefknife.azurecr.io`
- Image name: `chefknifestudios.martajazz.server.webapi`

**Rationale**: The existing ADO pipeline used legacy names (`transit-jazz-rg`, `transit-jazz-api`). The `main.bicep` file establishes the canonical naming convention. The GitHub Actions workflows should use Bicep-aligned names so they match the actual deployed infrastructure.

---

## Phase 1: Design & Contracts

### Workflow: `.github/workflows/client.yml`

```yaml
name: Client CI/CD

on:
  push:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'

      - name: Install WASM tools
        run: dotnet workload install wasm-tools

      - name: Publish Blazor app
        run: |
          dotnet publish \
            src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj \
            -c Release /p:BlazorEnableCompression=false

      - uses: actions/upload-artifact@v4
        with:
          name: blazor-client
          path: src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/bin/Release/net10.0/publish/wwwroot

  deploy:
    needs: build
    runs-on: ubuntu-latest
    environment: production
    steps:
      - uses: actions/download-artifact@v4
        with:
          name: blazor-client
          path: ./client

      - name: Inject production environment header
        run: |
          echo '{"headers":{"Blazor-Environment":"Production"}}' > ./client/staticwebapp.config.json

      - name: Deploy to Static Web App
        uses: Azure/static-web-apps-deploy@v1
        with:
          azure_static_web_apps_api_token: ${{ secrets.AZURE_STATIC_WEB_APP_TOKEN }}
          action: upload
          app_location: ./client
          output_location: ""
          skip_app_build: true
```

---

### Workflow: `.github/workflows/server.yml`

```yaml
name: Server CI/CD

on:
  push:
    branches: [main]

env:
  REGISTRY: chefknife.azurecr.io
  IMAGE_NAME: chefknifestudios.martajazz.server.webapi
  CONTAINER_APP_NAME: marta-jazz-prod-ca-server
  RESOURCE_GROUP: marta-jazz-prod-rg

jobs:
  build-and-push:
    runs-on: ubuntu-latest
    outputs:
      image-tag: ${{ steps.set-tag.outputs.tag }}
    steps:
      - uses: actions/checkout@v4

      - name: Log in to ACR
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ secrets.ACR_USERNAME }}
          password: ${{ secrets.ACR_PASSWORD }}

      - name: Build Docker image
        env:
          CONTAINER_REGISTRY: ${{ env.REGISTRY }}
          CONTAINER_TAG: ${{ github.sha }}
        run: docker compose build

      - name: Set image tag output
        id: set-tag
        run: echo "tag=${{ github.sha }}" >> $GITHUB_OUTPUT

      - name: Tag and push images
        run: |
          docker tag ${{ env.IMAGE_NAME }}:latest ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ github.sha }}
          docker tag ${{ env.IMAGE_NAME }}:latest ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:latest
          docker push ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ github.sha }}
          docker push ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:latest

  deploy:
    needs: build-and-push
    runs-on: ubuntu-latest
    environment: production
    steps:
      - name: Azure login
        uses: azure/login@v2
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}

      - name: Update Container App
        uses: azure/cli@v2
        with:
          azcliversion: latest
          inlineScript: |
            az containerapp update \
              --name ${{ env.CONTAINER_APP_NAME }} \
              --resource-group ${{ env.RESOURCE_GROUP }} \
              --image ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ needs.build-and-push.outputs.image-tag }}
```

---

### Contract: Required GitHub Secrets

| Secret Name | Description | How to Obtain |
|-------------|-------------|---------------|
| `AZURE_STATIC_WEB_APP_TOKEN` | Deployment token for the Azure Static Web App | Azure Portal → Static Web App → Manage deployment token |
| `ACR_USERNAME` | Admin username for `chefknife.azurecr.io` | Azure Portal → Container Registry → Access keys → Username |
| `ACR_PASSWORD` | Admin password for `chefknife.azurecr.io` | Azure Portal → Container Registry → Access keys → Password |
| `AZURE_CREDENTIALS` | JSON service principal with Contributor on `marta-jazz-prod-rg` | `az ad sp create-for-rbac --name "github-actions-transitjazz" --role Contributor --scopes /subscriptions/{sub}/resourceGroups/marta-jazz-prod-rg --sdk-auth` |

---

### Contract: Constitution Amendment (Principle V)

**Current text (Principle V)**:
> Source control MUST be managed in GitHub. CI/CD pipelines MUST be managed via Azure DevOps. The build pipeline MUST produce two distinct artifacts: a compiled WASM artifact deployed to Azure Static Web Apps, and a Background Service Docker Image pushed to the Azure Container Registry (ACR).

**Amended text (Principle V)**:
> Source control MUST be managed in GitHub. CI/CD pipelines MUST be managed via GitHub Actions. The build pipeline MUST produce two distinct artifacts: a compiled WASM artifact deployed to Azure Static Web Apps, and a Background Service Docker Image pushed to the Azure Container Registry (ACR).

**Amendment rationale**: Migrating from Azure DevOps to GitHub Actions eliminates cross-platform service connection maintenance, co-locates CI/CD with source control, and reduces operational surface area. The artifact requirements (WASM + Docker image) are unchanged.

**Version bump**: 3.0.0 → 3.1.0 (minor — new guidance replaces existing without backward-incompatible governance removal)
