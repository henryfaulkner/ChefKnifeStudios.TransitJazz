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
// Key Vault
// -----------------------------------------------------------------------------

module observabilityKeyVault 'modules/keyVault.bicep' = {
  name: 'observability-kv-deploy'
  scope: rg
  params: {
    name: take('${namePrefix}-kv', 24)
    location: location
    tags: tags
  }
}

// -----------------------------------------------------------------------------
// Outputs
// -----------------------------------------------------------------------------

output resourceGroupName string = rg.name
output staticWebAppDefaultHostname string = swa.outputs.defaultHostname
output dnsZoneNameServers array = dnsZone.outputs.nameServers
