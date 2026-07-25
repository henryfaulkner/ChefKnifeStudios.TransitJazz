# Feature Specification: GitHub Actions CI/CD Pipelines

**Feature Branch**: `infra/bicep`
**Created**: 2026-05-23
**Status**: Draft
**Input**: User description: "Create GitHub Actions CI/CD pipelines for client (Blazor WASM → Azure Static Web App) and server (Docker → ACR → Azure Container App) using the prior design doc."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Automated Client Deployment (Priority: P1)

A developer merges a pull request into `main`. Without any manual steps, the Blazor WebAssembly client is built, configured for the production environment, and deployed to the Azure Static Web App. The live site reflects the new changes within minutes.

**Why this priority**: The client is the user-facing surface of the application. Automated deployment removes manual error-prone steps and ensures production is always in sync with `main`.

**Independent Test**: Can be fully tested by pushing a commit to `main` and verifying the Static Web App deployment succeeds and the site reflects the change, independently of the server pipeline.

**Acceptance Scenarios**:

1. **Given** a commit is pushed to `main`, **When** the client workflow triggers, **Then** the Blazor app is built in Release mode with compression disabled and the artifact is uploaded.
2. **Given** the build artifact is ready, **When** the deploy job runs, **Then** `staticwebapp.config.json` is written with `Blazor-Environment: Production` before deployment.
3. **Given** the deploy job runs, **When** it targets the `production` GitHub Environment, **Then** the Static Web App is updated with the new build output.
4. **Given** the build job fails, **When** the deploy job is evaluated, **Then** the deploy job does not run and the existing deployment is unchanged.

---

### User Story 2 - Automated Server Deployment (Priority: P1)

A developer merges a pull request into `main`. Without any manual steps, the server Docker image is built, tagged with the commit SHA and `latest`, pushed to Azure Container Registry, and the Azure Container App is updated to run the new image revision.

**Why this priority**: Equal priority to the client — the server hosts the API and SignalR hub. Automated container deployment ensures server changes reach production consistently and traceably.

**Independent Test**: Can be fully tested by pushing a commit to `main` and verifying the ACR has a new image tagged with the commit SHA, and the Container App is running the updated revision, independently of the client pipeline.

**Acceptance Scenarios**:

1. **Given** a commit is pushed to `main`, **When** the server workflow triggers, **Then** the Docker image is built using the existing `docker-compose.yml`.
2. **Given** the image is built, **When** the push step runs, **Then** the image is tagged with both the full commit SHA and `latest`, and both tags are pushed to `chefknife.azurecr.io`.
3. **Given** the image is pushed, **When** the deploy job runs targeting the `production` GitHub Environment, **Then** the Azure Container App is updated to the commit-SHA-tagged image.
4. **Given** the push job fails, **When** the deploy job is evaluated, **Then** the deploy job does not run and the Container App continues running the previous image.

---

### User Story 3 - Deployment Auditability (Priority: P2)

A developer can open the GitHub Actions tab and see a clear record of every deployment: which commit was deployed, to which environment, at what time, and whether it succeeded or failed. The `production` GitHub Environment provides an approval gate and a deployment history.

**Why this priority**: Auditability and rollback awareness are secondary to the core automation but important for operational confidence.

**Independent Test**: After any deployment, the GitHub Actions UI shows a completed run tied to the triggering commit SHA, and the GitHub Environments page shows a deployment entry for `production`.

**Acceptance Scenarios**:

1. **Given** any workflow run completes, **When** a developer views the Actions tab, **Then** the run is labeled with the triggering commit SHA and branch.
2. **Given** the `production` environment is configured in GitHub, **When** a deploy job runs, **Then** it appears as a deployment record in the GitHub Environments view.

---

### Edge Cases

- What happens when the Static Web App deployment token is expired or revoked? The deploy job fails with an authentication error; no partial deployment occurs.
- What happens when the ACR login fails? The build-and-push job fails before any image is tagged or pushed; the deploy job does not run.
- What happens when the Container App update command targets a non-existent resource group or app name? The deploy job fails with an Azure CLI error; the running revision is unchanged.
- What happens when two commits are pushed to `main` in quick succession? Both workflow runs execute independently; the later run may overwrite the `latest` tag but each run's SHA-tagged image is preserved.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST trigger both the client and server workflows automatically on every push to the `main` branch.
- **FR-002**: The client workflow MUST build the Blazor WebAssembly app in Release configuration with compression disabled.
- **FR-003**: The client workflow MUST write `{"headers":{"Blazor-Environment":"Production"}}` into `staticwebapp.config.json` before deploying to the Static Web App.
- **FR-004**: The client workflow MUST deploy the built artifact to the Azure Static Web App using the `AZURE_STATIC_WEB_APP_TOKEN` secret.
- **FR-005**: The server workflow MUST build the Docker image using `docker-compose.yml` from the repository root.
- **FR-006**: The server workflow MUST tag the built image with both the triggering commit SHA and `latest`.
- **FR-007**: The server workflow MUST push both image tags to `chefknife.azurecr.io`.
- **FR-008**: The server workflow MUST update the Azure Container App to run the commit-SHA-tagged image after a successful push.
- **FR-009**: Both workflows MUST use a `production` GitHub Environment for their deploy jobs, providing a deployment history record.
- **FR-010**: The deploy job in each workflow MUST NOT run if the preceding build/push job fails.
- **FR-011**: All secrets (Static Web App token, ACR credentials, Azure service principal) MUST be stored as GitHub repository secrets, not hardcoded in workflow files.
- **FR-012**: The server workflow MUST authenticate to ACR using dedicated credentials (`ACR_USERNAME` / `ACR_PASSWORD`) stored as GitHub secrets.
- **FR-013**: The server workflow MUST authenticate to Azure for the Container App update using a service principal JSON credential (`AZURE_CREDENTIALS`) stored as a GitHub secret.

### Key Entities

- **Client Workflow** (`client.yml`): The GitHub Actions workflow file that owns the full client CI/CD lifecycle — build, artifact upload, environment header injection, Static Web App deployment.
- **Server Workflow** (`server.yml`): The GitHub Actions workflow file that owns the full server CI/CD lifecycle — Docker build, image tagging, ACR push, Container App update.
- **`production` GitHub Environment**: The named environment in GitHub that both deploy jobs target; provides deployment history and an optional approval gate.
- **GitHub Secrets**: Repository-level secrets holding sensitive credentials (`AZURE_STATIC_WEB_APP_TOKEN`, `ACR_USERNAME`, `ACR_PASSWORD`, `AZURE_CREDENTIALS`).
- **Blazor Artifact** (`blazor-client`): The built `wwwroot` output uploaded by the client build job and downloaded by the client deploy job.
- **Docker Image**: The server container image built from `docker-compose.yml`, tagged with commit SHA and `latest`, and stored in ACR.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A push to `main` results in the client Static Web App being updated within 10 minutes of the push, with zero manual steps required.
- **SC-002**: A push to `main` results in the Azure Container App running the new server image within 15 minutes of the push, with zero manual steps required.
- **SC-003**: 100% of successful deployments are traceable to a specific commit SHA visible in both the GitHub Actions run and the deployed Docker image tag.
- **SC-004**: A build failure in either workflow prevents deployment 100% of the time — no partial or broken deployments reach the production environment.
- **SC-005**: Both workflows are visible in the GitHub Actions tab with pass/fail status after every push to `main`.

## Assumptions

- The `.github/workflows/` directory already exists in the repository (confirmed — it is empty and ready for workflow files).
- The `production` GitHub Environment will be created in the repository settings before the first deployment run.
- The four required secrets (`AZURE_STATIC_WEB_APP_TOKEN`, `ACR_USERNAME`, `ACR_PASSWORD`, `AZURE_CREDENTIALS`) will be added to GitHub repository secrets before the first deployment run.
- The Azure Container App and resource group follow the Bicep naming convention: `marta-jazz-prod-ca-server` in `marta-jazz-prod-rg`.
- ACR admin access or a service principal with `AcrPush` rights on `chefknife.azurecr.io` is available for the `ACR_USERNAME` / `ACR_PASSWORD` secrets.
- The `AZURE_CREDENTIALS` secret is the JSON output of an `az ad sp create-for-rbac` command scoped with at least Contributor rights on the production resource group.
- The existing `docker-compose.yml` at the repository root produces an image named `chefknifestudios.martajazz.server.webapi:latest` when `docker compose build` is run.
- The `staticwebapp.config.json` header injection replaces (not merges with) any existing config file in the artifact output — the artifact contains no pre-existing `staticwebapp.config.json` at the `wwwroot` root level.
- NuGet authentication for the Blazor build uses the default `actions/setup-dotnet` NuGet source; no private feed credentials are required.
