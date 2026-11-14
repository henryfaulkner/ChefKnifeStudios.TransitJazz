# TransitJazz ? retained Azure infrastructure

The API, SignalR hub and worker run on Linode. These templates manage the Azure
resource group, Static Web App, DNS zone/records and Key Vault. The shared ACR is
used by CI and Linode but is not provisioned by this template.

## Layout

```text
main.bicep                 # Subscription-scoped resource group and modules
main.json                  # Generated ARM template
main.dev.bicepparam        # Development parameters
main.prod.bicepparam       # Production parameters
modules/
  staticWebApp.bicep
  dnsZone.bicep
  dnsApexA.bicep
  dnsWwwCname.bicep
  keyVault.bicep
```

Resource names use `marta-jazz-{dev|prod}`; the DNS zone uses `apexDomain`.
`repositoryToken` is supplied securely when provisioning the Static Web App.
Review its build paths and deployment branch before applying a template.
Name.com is authoritative for the live API domain; the existing Azure DNS zone
is not its publication source. See the [migration status](../docs/LINODE_MIGRATION_STATUS.md).

## Validate and deploy

```bash
az bicep build --file bicep/main.bicep
az bicep build-params --file bicep/main.dev.bicepparam --stdout
az bicep build-params --file bicep/main.prod.bicepparam --stdout

az deployment sub validate --location eastus2 --template-file bicep/main.bicep --parameters bicep/main.dev.bicepparam
az deployment sub what-if --location eastus2 --template-file bicep/main.bicep --parameters bicep/main.dev.bicepparam
az deployment sub create --location eastus2 --template-file bicep/main.bicep --parameters bicep/main.dev.bicepparam
```

Removing the Container Apps templates does not delete previously deployed Azure
resources. Retire the stopped server and its dedicated resources through the
migration runbook after checking their dependencies and rollback requirements.

## Application delivery

The [server workflow](../.github/workflows/server.yml) tests and publishes SHA-tagged
API/migration images to ACR, then deploys the API to Linode by immutable digest.
It uses repository secrets `ACR_USERNAME` / `ACR_PASSWORD` and the `server-linode`
environment's `LINODE_HOST`, `LINODE_DEPLOY_KEY` and `LINODE_KNOWN_HOSTS` settings.
See [Linode operations](../deploy/linode/README.md) for runtime configuration and rollback.

The two client workflows use `AZURE_STATIC_WEB_APP_TOKEN_2` (TransitJazz) and
`AZURE_STATIC_WEB_APP_TOKEN` (MartaJazz). Database migrations remain a separate,
reviewed operation; publishing the migration image does not run it.
