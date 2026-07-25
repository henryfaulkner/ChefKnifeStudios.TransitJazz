# Research: GitHub Actions CI/CD Pipelines

## Decision 1: GitHub Actions action versions

**Decision**: Pin all actions to current stable major versions.
- `actions/checkout@v4`, `actions/setup-dotnet@v4`, `actions/upload-artifact@v4`, `actions/download-artifact@v4`
- `docker/login-action@v3`
- `azure/login@v2`, `Azure/static-web-apps-deploy@v1`, `azure/cli@v2`

**Rationale**: Major version pinning catches patch-level security fixes automatically while preventing breaking changes. Appropriate for a personal project.

**Alternatives considered**: SHA pinning — rejected as unnecessary operational overhead.

---

## Decision 2: ACR authentication

**Decision**: ACR admin credentials (`ACR_USERNAME` / `ACR_PASSWORD`) via `docker/login-action@v3`.

**Rationale**: Simplest path. ACR admin access is already required for the existing ADO pipeline. Avoids OIDC federation infrastructure setup.

**Alternatives considered**:
- Workload Identity Federation (OIDC) — zero long-lived secrets, but adds Azure AD federated credential setup overhead.
- Service principal + `az acr login` — works but adds an extra login step and requires `AZURE_CREDENTIALS` for ACR access when separate credentials are cleaner.

---

## Decision 3: Container App update mechanism

**Decision**: `azure/cli@v2` running `az containerapp update`.

**Rationale**: No official first-party GitHub Actions equivalent of `AzureContainerApps@1` with stable quality. `azure/cli@v2` is the canonical approach for Azure CLI in GitHub Actions.

**Alternatives considered**: `azure/container-apps-deploy-action` — less stable, less documented, more opinionated than raw CLI.

---

## Decision 4: Static Web App deploy

**Decision**: `Azure/static-web-apps-deploy@v1` with `skip_app_build: true` and `output_location: ""`.

**Rationale**: The artifact is pre-built. `skip_app_build: true` + empty `output_location` instructs the action to treat the `app_location` directory as the deployable output directly.

**Alternatives considered**: Azure CLI `az staticwebapp` — no direct upload subcommand exists for WASM bundles.

---

## Decision 5: staticwebapp.config.json injection

**Decision**: `echo '{"headers":{"Blazor-Environment":"Production"}}' > ./client/staticwebapp.config.json` after artifact download, before deploy step.

**Rationale**: The `dotnet publish` output does not include a `staticwebapp.config.json` at the root, so a simple write (not merge) is sufficient. Matches existing ADO pipeline behavior exactly.

---

## Decision 6: Constitution amendment

**Decision**: Amend Principle V to replace "Azure DevOps" with "GitHub Actions". Version bump 3.0.0 → 3.1.0.

**Rationale**: Source control and CI/CD on the same platform reduces service connection overhead and secret duplication. Two-artifact requirement (WASM + Docker) is unchanged.

---

## Decision 7: Resource names

**Decision**: Use Bicep-aligned naming: `marta-jazz-prod-rg` / `marta-jazz-prod-ca-server` / `chefknife.azurecr.io` / image `chefknifestudios.martajazz.server.webapi`.

**Rationale**: Legacy ADO pipeline used stale names (`transit-jazz-rg`, `transit-jazz-api`). Bicep `main.bicep` is canonical. Alignment prevents deploy targeting the wrong resource.
