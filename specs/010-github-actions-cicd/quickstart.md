# Quickstart: GitHub Actions CI/CD Pipelines

## Prerequisites

Complete these steps before the first workflow run:

### 1. Create GitHub Environment

In the repository settings → Environments → New environment:
- Name: `production`
- (Optional) Add required reviewers for deployment approval

### 2. Add GitHub Secrets

Navigate to repository settings → Secrets and variables → Actions → New repository secret:

| Secret Name | How to get the value |
|-------------|---------------------|
| `AZURE_STATIC_WEB_APP_TOKEN` | Azure Portal → Static Web App (`marta-jazz-prod-swa`) → Manage deployment token |
| `ACR_USERNAME` | Azure Portal → Container Registry (`chefknife`) → Access keys → Username |
| `ACR_PASSWORD` | Azure Portal → Container Registry (`chefknife`) → Access keys → Password |
| `AZURE_CREDENTIALS` | Run: `az ad sp create-for-rbac --name "github-actions-transitjazz" --role Contributor --scopes /subscriptions/{sub-id}/resourceGroups/marta-jazz-prod-rg --sdk-auth` — paste the full JSON output |

### 3. Ensure ACR admin access is enabled

Azure Portal → Container Registry (`chefknife`) → Access keys → Admin user: **Enabled**

---

## Manual Verification Tests

### Test 1: Client workflow triggers on push to main

1. Push any commit to `main`
2. Open GitHub → Actions tab
3. Confirm the "Client CI/CD" workflow run appears and is associated with the commit SHA
4. **Pass**: Run appears and starts within ~30 seconds of push

---

### Test 2: Blazor app builds successfully

1. Wait for the `build` job to complete in a "Client CI/CD" run
2. Click the run → `build` job → "Publish Blazor app" step
3. Confirm output shows `Build succeeded` and no errors
4. Confirm the `blazor-client` artifact appears in the run summary
4. **Pass**: Artifact uploaded, no build errors

---

### Test 3: Production header injected

1. In a completed "Client CI/CD" run, open the `deploy` job
2. Look at the "Inject production environment header" step output
3. **Pass**: Step runs without error; no output means the `echo` succeeded silently

To verify the header reaches the deployed app:
- Open `https://www.martajazz.com` (or the SWA default URL)
- Open browser DevTools → Network → any request
- Confirm `Blazor-Environment: Production` appears in response headers

---

### Test 4: Static Web App is updated

1. Make a visible UI change (e.g., update the page title or a visible string in `Client.WebApp`)
2. Push to `main`
3. Wait for "Client CI/CD" to complete
4. Open the live site and hard-refresh
5. **Pass**: Change is visible on the live site

---

### Test 5: Server workflow builds and pushes Docker image

1. Push any commit to `main`
2. Open GitHub → Actions tab → "Server CI/CD" run
3. Wait for `build-and-push` job to complete
4. Open Azure Portal → Container Registry (`chefknife`) → Repositories → `chefknifestudios.martajazz.server.webapi`
5. **Pass**: A new tag matching the commit SHA appears in the repository

---

### Test 6: Container App is updated to new image

1. After Test 5 passes, wait for the `deploy` job to complete in the same run
2. Open Azure Portal → Container App (`marta-jazz-prod-ca-server`) → Revisions
3. **Pass**: A new active revision exists with the commit-SHA image tag

---

### Test 7: Failed build prevents deploy

1. Introduce a deliberate build error (e.g., add a syntax error to a `.cs` file in the client project)
2. Push to `main`
3. Observe the "Client CI/CD" run
4. **Pass**: `build` job fails; `deploy` job shows "Skipped" or never starts; Static Web App is unchanged

Revert the deliberate error after verifying.

---

## Rollback Procedure

If a deployment causes production issues:

**Client**: Re-run a previous successful "Client CI/CD" workflow run (GitHub → Actions → select the run → Re-run jobs).

**Server**: Update the Container App to the previous image tag manually:
```bash
az containerapp update \
  --name marta-jazz-prod-ca-server \
  --resource-group marta-jazz-prod-rg \
  --image chefknife.azurecr.io/chefknifestudios.martajazz.server.webapi:{previous-sha}
```
