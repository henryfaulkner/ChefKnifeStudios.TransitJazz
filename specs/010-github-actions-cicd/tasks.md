# Tasks: GitHub Actions CI/CD Pipelines

**Input**: Design documents from `specs/010-github-actions-cicd/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: One-time prerequisites that must exist before any workflow can run end-to-end.

- [x] T001 Amend `constitution.md` Principle V — replace "Azure DevOps" with "GitHub Actions" and bump version 3.0.0 → 3.1.0
- [ ] T002 Create `production` GitHub Environment in repository Settings → Environments
- [ ] T003 [P] Add `AZURE_STATIC_WEB_APP_TOKEN` secret to repository Settings → Secrets and variables → Actions
- [ ] T004 [P] Add `ACR_USERNAME` and `ACR_PASSWORD` secrets (from `chefknife` ACR Access keys) to repository Secrets
- [ ] T005 [P] Add `AZURE_CREDENTIALS` secret (service principal JSON scoped to `marta-jazz-prod-rg`) to repository Secrets

**Checkpoint**: Constitution amended. GitHub Environment `production` exists. All four secrets are set.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The `.github/workflows/` directory must exist (already confirmed empty). No additional scaffolding needed — the directory is ready.

> No tasks required here. `.github/workflows/` already exists.

**⚠️ CRITICAL**: Both user story workflows (US1 and US2) depend on Phase 1 completion before they can run end-to-end.

---

## Phase 3: User Story 1 — Automated Client Deployment (Priority: P1) 🎯 MVP

**Goal**: A push to `main` automatically builds the Blazor WASM app, injects the production environment header, and deploys to Azure Static Web App — zero manual steps.

**Independent Test**: Push a commit to `main`. Verify the "Client CI/CD" workflow appears in the GitHub Actions tab, completes successfully, and the live Static Web App reflects the change with `Blazor-Environment: Production` in response headers. See `quickstart.md` Tests 1–4.

### Implementation for User Story 1

- [x] T006 [US1] Create `.github/workflows/client.yml` — trigger on push to `main`, single `build` job: checkout, setup .NET 10, install `wasm-tools`, `dotnet publish` the Blazor WebApp csproj with `-c Release /p:BlazorEnableCompression=false`, upload artifact `blazor-client` from `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/bin/Release/net10.0/publish/wwwroot`
- [x] T007 [US1] Add `deploy` job to `.github/workflows/client.yml` — `needs: build`, `environment: production`, download artifact `blazor-client` to `./client`, write `{"headers":{"Blazor-Environment":"Production"}}` to `./client/staticwebapp.config.json`, deploy via `Azure/static-web-apps-deploy@v1` with `azure_static_web_apps_api_token: ${{ secrets.AZURE_STATIC_WEB_APP_TOKEN }}`, `action: upload`, `app_location: ./client`, `output_location: ""`, `skip_app_build: true`

**Checkpoint**: Push to `main` → "Client CI/CD" workflow succeeds → Static Web App updated → `Blazor-Environment: Production` header present. US1 is independently functional.

---

## Phase 4: User Story 2 — Automated Server Deployment (Priority: P1)

**Goal**: A push to `main` automatically builds the Docker image, tags it with commit SHA and `latest`, pushes both tags to ACR, and updates the Azure Container App to the new image — zero manual steps.

**Independent Test**: Push a commit to `main`. Verify the "Server CI/CD" workflow appears in the GitHub Actions tab, completes successfully, ACR contains a new image tagged with the commit SHA, and the Container App revision is updated. See `quickstart.md` Tests 5–6.

### Implementation for User Story 2

- [x] T008 [US2] Create `.github/workflows/server.yml` — workflow-level `env` block with `REGISTRY: chefknife.azurecr.io`, `IMAGE_NAME: chefknifestudios.martajazz.server.webapi`, `CONTAINER_APP_NAME: marta-jazz-prod-ca-server`, `RESOURCE_GROUP: marta-jazz-prod-rg`; trigger on push to `main`
- [x] T009 [US2] Add `build-and-push` job to `.github/workflows/server.yml` — checkout, `docker/login-action@v3` with `registry: ${{ env.REGISTRY }}`, `username: ${{ secrets.ACR_USERNAME }}`, `password: ${{ secrets.ACR_PASSWORD }}`; `docker compose build` with env vars `CONTAINER_REGISTRY=${{ env.REGISTRY }}` and `CONTAINER_TAG=${{ github.sha }}`; set job output `image-tag: ${{ github.sha }}`; tag and push both `${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ github.sha }}` and `${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:latest`
- [x] T010 [US2] Add `deploy` job to `.github/workflows/server.yml` — `needs: build-and-push`, `environment: production`; `azure/login@v2` with `creds: ${{ secrets.AZURE_CREDENTIALS }}`; `azure/cli@v2` running `az containerapp update --name ${{ env.CONTAINER_APP_NAME }} --resource-group ${{ env.RESOURCE_GROUP }} --image ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ needs.build-and-push.outputs.image-tag }}`

**Checkpoint**: Push to `main` → "Server CI/CD" workflow succeeds → ACR has SHA-tagged image → Container App revision updated. US2 is independently functional.

---

## Phase 5: User Story 3 — Deployment Auditability (Priority: P2)

**Goal**: Every deployment is traceable to a specific commit SHA and appears as a record in the GitHub `production` Environment. Failed builds prevent deploys 100% of the time.

**Independent Test**: After any workflow run, open GitHub Actions tab and verify the run is labeled with the triggering commit SHA. Open GitHub → Environments → `production` and verify a deployment record appears. Introduce a deliberate build error and confirm the deploy job is skipped. See `quickstart.md` Test 7.

### Implementation for User Story 3

> US3 is delivered as a by-product of US1 and US2 implementation:
> - `environment: production` in each deploy job creates the audit trail automatically
> - `needs: build` / `needs: build-and-push` already prevents deploy on build failure
> - Commit SHA tagging in the server workflow provides end-to-end traceability

- [x] T011 [US3] Verify `environment: production` is present in the `deploy` job of `.github/workflows/client.yml` (already set in T007 — confirm and check off)
- [x] T012 [US3] Verify `environment: production` is present in the `deploy` job of `.github/workflows/server.yml` (already set in T010 — confirm and check off)
- [ ] T013 [US3] Run quickstart.md Test 7 (deliberate build failure): introduce a syntax error in the client project, push to `main`, confirm `deploy` job is skipped, revert the error

**Checkpoint**: GitHub Environments page shows `production` deployment records for both client and server. Build failures provably block deploys.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Cleanup, documentation alignment, and validation of the complete pipeline.

- [ ] T014 [P] Run all 7 quickstart.md verification tests end-to-end and confirm each passes
- [x] T015 [P] Update `deploy/client-pipeline.yml` and `deploy/server-pipeline.yml` with a header comment noting they are superseded by `.github/workflows/` equivalents
- [ ] T016 Review `specs/010-github-actions-cicd/checklists/requirements.md` — confirm all items still pass post-implementation

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: No tasks needed — directory already exists
- **Phase 3 (US1)**: Depends on Phase 1 (needs `AZURE_STATIC_WEB_APP_TOKEN` secret and `production` environment)
- **Phase 4 (US2)**: Depends on Phase 1 (needs `ACR_USERNAME`, `ACR_PASSWORD`, `AZURE_CREDENTIALS` secrets and `production` environment)
- **Phase 5 (US3)**: Depends on Phase 3 AND Phase 4 (audit trail requires both workflows deployed)
- **Phase 6 (Polish)**: Depends on all prior phases

### User Story Dependencies

- **US1 (P1)**: Independent after Phase 1 — no dependency on US2
- **US2 (P1)**: Independent after Phase 1 — no dependency on US1
- **US3 (P2)**: Depends on US1 and US2 being complete (validates auditability of both)

### Within Each User Story

- US1: T006 (build job) must complete before T007 (deploy job) is added
- US2: T008 (env + trigger) → T009 (build-and-push job) → T010 (deploy job)
- US3: T011, T012 are verification tasks (parallel) → T013 (failure test, sequential)

### Parallel Opportunities

- Phase 1: T003, T004, T005 can all run in parallel (independent secret additions)
- US1 and US2 workflows can be authored in parallel (different files: `client.yml`, `server.yml`)
- Phase 6: T014, T015 can run in parallel

---

## Parallel Example: US1 and US2

```
# Phase 1 secrets (all parallel):
Task T003: Add AZURE_STATIC_WEB_APP_TOKEN secret
Task T004: Add ACR_USERNAME + ACR_PASSWORD secrets
Task T005: Add AZURE_CREDENTIALS secret

# After Phase 1, author both workflows in parallel:
Task T006+T007: .github/workflows/client.yml  (US1)
Task T008+T009+T010: .github/workflows/server.yml  (US2)
```

---

## Implementation Strategy

### MVP First (US1 — Client Pipeline)

1. Complete Phase 1 (Setup — all 5 tasks)
2. Complete T006, T007 (US1 — client workflow)
3. **STOP and VALIDATE**: Push to `main`, run Tests 1–4 from quickstart.md
4. Client auto-deploys — MVP delivered

### Incremental Delivery

1. Phase 1 complete → environment and secrets ready
2. Add `client.yml` (US1) → push → validate client deploy → Client CI/CD live
3. Add `server.yml` (US2) → push → validate server deploy → Server CI/CD live
4. Verify US3 auditability → run Test 7 → both pipelines auditable
5. Polish (Phase 6) → documentation updated, all quickstart tests green

---

## Notes

- [P] tasks = different files or parallel actions with no shared dependency
- [Story] label maps each task to its user story for traceability
- T001 (constitution amendment) is a prerequisite of the whole feature — do it first
- The `production` GitHub Environment (T002) must exist before the first push to `main` or the deploy jobs will fail
- The server `docker compose build` expects `CONTAINER_REGISTRY` and `CONTAINER_TAG` env vars to be set; `docker-compose.yml` uses these via `${CONTAINER_REGISTRY:-localhost:5000}` and `${CONTAINER_TAG:-latest}` substitution
- Commit after each task or logical group
