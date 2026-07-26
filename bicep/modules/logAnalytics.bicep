// =============================================================================
// logAnalytics.bicep — Log Analytics workspace for Container Apps environment
// diagnostics (feature 051, US1). appLogsConfiguration on the Container Apps
// environment has always accepted customerId/sharedKey params (see
// containerAppsEnvironment.bicep's empty()-conditional), but nothing has ever
// supplied them, so appLogsConfiguration resolves to null and container stdout
// is discarded. This module creates the workspace; main.bicep wires its
// customerId + listKeys()-retrieved shared key into the existing cae call.
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

output customerId string = workspace.properties.customerId
output id string = workspace.id
output name string = workspace.name
