// =============================================================================
// main.bicep — Marta Jazz Infrastructure
// Subscription: ChefKnifeStudios
// =============================================================================

targetScope = 'subscription'

// -----------------------------------------------------------------------------
// Parameters
// -----------------------------------------------------------------------------

@description('Project name used as prefix in all resource names.')
param projectName string = 'marta-jazz'

@description('Deployment environment.')
@allowed([
  'dev'
  'prod'
])
param environment string

@description('Primary Azure region for regional resources.')
param location string = 'eastus2'

@description('Custom apex domain for the site (e.g., martajazz.com).')
param apexDomain string = 'martajazz.com'

@description('Existing Container Registry name (shared across environments).')
param containerRegistryName string = 'chefknife'

@description('Resource group of the existing Container Registry.')
param containerRegistryResourceGroup string = 'general'

@description('Container image tag to deploy for the server container app.')
param serverImageTag string = 'latest'

@description('GitHub repository URL for the Static Web App source.')
param repositoryUrl string = 'https://github.com/henryfaulkner/ChefKnifeStudios.TransitJazz'

@description('GitHub Personal Access Token for SWA deployment.')
@secure()
param repositoryToken string

@description('Bind custom domains to the SWA. Set false on first deploy (DNS zone must exist first); set true on subsequent deploys.')
param bindCustomDomains bool = false

// -----------------------------------------------------------------------------
// Variables
// -----------------------------------------------------------------------------

var namePrefix = '${projectName}-${environment}'
var resourceGroupName = '${namePrefix}-rg'

var tags = {
  env: environment
  project: projectName
}

var staticWebAppName = '${namePrefix}-swa'
var staticWebAppResourceId = '/subscriptions/${subscription().subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.Web/staticSites/${staticWebAppName}'

// Storage account names: 3-24 chars, lowercase alphanumeric only, globally unique.
// Derive a stable suffix from the resource group id so dev/prod get distinct names.
var telemetryStorageAccountName = take('mjtel${environment}${uniqueString(subscription().subscriptionId, resourceGroupName)}', 24)
var telemetryContainerName = 'parquet'

// -----------------------------------------------------------------------------
// Resource Group
// -----------------------------------------------------------------------------

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// -----------------------------------------------------------------------------
// DNS Zone (deployed first; SWA validates custom domains against it)
// -----------------------------------------------------------------------------

module dnsZone 'modules/dnsZone.bicep' = {
  name: 'dnsZone-deploy'
  scope: rg
  params: {
    zoneName: apexDomain
    tags: tags
  }
}

// -----------------------------------------------------------------------------
// Static Web App (client-side host)
// -----------------------------------------------------------------------------

module swa 'modules/staticWebApp.bicep' = {
  name: 'swa-deploy'
  scope: rg
  params: {
    name: staticWebAppName
    location: 'eastus2'
    tags: tags
    repositoryUrl: repositoryUrl
    repositoryToken: repositoryToken
    branch: 'main'
    customDomains: bindCustomDomains ? [
      apexDomain
      'www.${apexDomain}'
    ] : []
  }
}

// -----------------------------------------------------------------------------
// DNS records (depend on both DNS zone and SWA; run after both complete)
// -----------------------------------------------------------------------------

module apexA 'modules/dnsApexA.bicep' = {
  name: 'dns-apex-a-deploy'
  scope: rg
  params: {
    zoneName: apexDomain
    staticWebAppResourceId: staticWebAppResourceId
  }
  dependsOn: [
    dnsZone
    swa
  ]
}

module wwwCname 'modules/dnsWwwCname.bicep' = {
  name: 'dns-www-cname-deploy'
  scope: rg
  params: {
    zoneName: apexDomain
    swaDefaultHostname: swa.outputs.defaultHostname
  }
  dependsOn: [
    dnsZone
  ]
}

// -----------------------------------------------------------------------------
// User-assigned Managed Identity (server)
// -----------------------------------------------------------------------------

module serverIdentity 'modules/managedIdentity.bicep' = {
  name: 'server-mi-deploy'
  scope: rg
  params: {
    name: '${namePrefix}-ca-server-mi'
    location: location
    tags: tags
  }
}

// -----------------------------------------------------------------------------
// AcrPull role assignment on the existing ACR for the server MI
// -----------------------------------------------------------------------------

module acrRoleAssignment 'modules/acrRoleAssignment.bicep' = {
  name: 'acr-role-deploy'
  scope: resourceGroup(containerRegistryResourceGroup)
  params: {
    containerRegistryName: containerRegistryName
    principalId: serverIdentity.outputs.principalId
  }
}

// -----------------------------------------------------------------------------
// Telemetry storage (logging sidecar — feature 013): account + container + blob RBAC
// -----------------------------------------------------------------------------

module telemetryStorage 'modules/telemetryStorage.bicep' = {
  name: 'telemetry-storage-deploy'
  scope: rg
  params: {
    storageAccountName: telemetryStorageAccountName
    location: location
    tags: tags
    containerName: telemetryContainerName
    principalId: serverIdentity.outputs.principalId
  }
}

// -----------------------------------------------------------------------------
// Container Apps Environment
// -----------------------------------------------------------------------------

module cae 'modules/containerAppsEnvironment.bicep' = {
  name: 'cae-deploy'
  scope: rg
  params: {
    name: '${namePrefix}-cae'
    location: location
    tags: tags
  }
}

// -----------------------------------------------------------------------------
// Container App — server (API + SignalR Hub + Worker)
// -----------------------------------------------------------------------------

module serverApp 'modules/containerApp.bicep' = {
  name: 'server-ca-deploy'
  scope: rg
  params: {
    name: '${namePrefix}-ca-server'
    location: location
    tags: tags
    environmentId: cae.outputs.id
    managedIdentityId: serverIdentity.outputs.id
    containerRegistryLoginServer: '${containerRegistryName}.azurecr.io'
    image: '${containerRegistryName}.azurecr.io/chefknifestudios.martajazz.server.webapi:latest'
    cpu: '0.5'
    memory: '1Gi'
    minReplicas: 1
    maxReplicas: 1
    targetPort: 8080 // confirmed: Dockerfile EXPOSE 8080
    corsAllowedOrigins: [
      'https://${apexDomain}'
      'https://www.${apexDomain}'
    ]
    envVars: [
      {
        name: 'ASPNETCORE_URLS'
        value: 'http://+:8080'
      }
      {
        name: 'WebApi__BaseUrl'
        value: 'http://localhost:8080'
      }
      // Let DefaultAzureCredential select the user-assigned MI (the app has only
      // this one, but setting AZURE_CLIENT_ID makes credential resolution explicit).
      {
        name: 'AZURE_CLIENT_ID'
        value: serverIdentity.outputs.clientId
      }
      // Logging sidecar (feature 013) — blob target for the telemetry parquet writer.
      {
        name: 'Logging__Telemetry__BlobServiceUri'
        value: telemetryStorage.outputs.blobServiceUri
      }
      {
        name: 'Logging__Telemetry__Container'
        value: telemetryContainerName
      }
      {
        name: 'Logging__Telemetry__Enabled'
        value: 'true'
      }
    ]
  }
  dependsOn: [
    acrRoleAssignment
  ]
}

// -----------------------------------------------------------------------------
// Outputs
// -----------------------------------------------------------------------------

output resourceGroupName string = rg.name
output staticWebAppDefaultHostname string = swa.outputs.defaultHostname
output dnsZoneNameServers array = dnsZone.outputs.nameServers
output serverContainerAppFqdn string = serverApp.outputs.fqdn
output serverManagedIdentityPrincipalId string = serverIdentity.outputs.principalId
output telemetryStorageAccountName string = telemetryStorage.outputs.accountName
output telemetryBlobServiceUri string = telemetryStorage.outputs.blobServiceUri
