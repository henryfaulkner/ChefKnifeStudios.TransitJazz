// =============================================================================
// staticWebApp.bicep — Static Web App + custom domains
// =============================================================================

@description('Name of the Static Web App.')
param name string

@description('Location for the SWA (Free SKU is regional, East US 2 supported).')
param location string = 'eastus2'

@description('Resource tags.')
param tags object

@description('Source repository URL (GitHub).')
param repositoryUrl string

@description('Deployment branch.')
param branch string = 'main'

@description('GitHub PAT for SWA deployment.')
@secure()
param repositoryToken string

@description('Custom domains to bind to the SWA (apex + www).')
param customDomains array = []

resource swa 'Microsoft.Web/staticSites@2024-04-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    repositoryUrl: repositoryUrl
    branch: branch
    repositoryToken: repositoryToken
    buildProperties: {
      appLocation: 'src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp'
      apiLocation: ''
      outputLocation: 'wwwroot'
    }
  }
}

// Bind custom domains. Note: apex domain requires the DNS A-alias record
// (handled in the DNS module) to exist before validation succeeds.
resource domains 'Microsoft.Web/staticSites/customDomains@2024-04-01' = [for d in customDomains: {
  parent: swa
  name: d
  properties: {
    // 'dns-txt-token' is also valid; apex with A-alias uses 'cname-delegation' for www
    // and validates apex automatically once the alias record resolves.
    validationMethod: d == customDomains[0] ? 'dns-txt-token' : 'cname-delegation'
  }
}]

output id string = swa.id
output name string = swa.name
output defaultHostname string = swa.properties.defaultHostname
