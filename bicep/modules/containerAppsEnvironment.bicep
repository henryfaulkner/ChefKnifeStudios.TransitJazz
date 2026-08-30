// =============================================================================
// containerAppsEnvironment.bicep — Managed environment for Container Apps
// =============================================================================

@description('Name of the Container Apps Environment.')
param name string

@description('Location.')
param location string

@description('Resource tags.')
param tags object

resource env 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    // Application stdout/stderr is routed by an environment diagnostic setting.
    // No workspace shared key or customer-ID flow is accepted by this feature.
    appLogsConfiguration: {
      destination: 'azure-monitor'
    }
    zoneRedundant: false
  }
}

output id string = env.id
output name string = env.name
output defaultDomain string = env.properties.defaultDomain
