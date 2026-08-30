// =============================================================================
// logAnalytics.bicep — Log Analytics workspace for Container Apps environment
// diagnostics. The workspace is referenced by the environment diagnostic setting;
// application code uses the Azure Monitor route and never receives a workspace key.
// =============================================================================

@description('Name of the Log Analytics workspace.')
param name string

@description('Location.')
param location string

@description('Resource tags.')
param tags object

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
  }
}

output id string = workspace.id
output name string = workspace.name
