using './main.bicep'

param projectName = 'marta-jazz'
param environment = 'prod'
param location = 'eastus2'
param apexDomain = 'martajazz.com'

param containerRegistryName = 'chefknife'
param containerRegistryResourceGroup = 'general'

param serverImageTag = ''

param repositoryUrl = 'https://github.com/henryfaulkner/ChefKnifeStudios.TransitJazz'
param repositoryToken = ''

// Supply only an approved object ID through the reviewed deployment process.
param logAnalyticsReaderPrincipalId = ''
param enableLegacyTelemetry = true

param enableWorkerMetrics = false
param grafanaOtlpMetricsEndpoint = ''
// These are Key Vault secret URIs, not ACA secret aliases. The Bicep template
// maps them to the short aliases required by Azure Container Apps.
param grafanaPublisherSecretUri = 'https://transit-jazz-kv.vault.azure.net/secrets/TransitJazzWorkerMetricsPublisherToken'
param grafanaProvisioningSecretUri = 'https://transit-jazz-kv.vault.azure.net/secrets/TransitJazzTerraformProvisionerToken'
