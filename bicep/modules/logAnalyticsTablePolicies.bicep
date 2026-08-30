// Resource-specific Container Apps tables are materialized by the diagnostic route. These
// declarations keep the intended plans/retention reviewable and are applied after routing.

@description('Existing Log Analytics workspace name.')
param workspaceName string

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: workspaceName
}

resource consoleLogs 'Microsoft.OperationalInsights/workspaces/tables@2025-07-01' = {
  parent: workspace
  name: 'ContainerAppConsoleLogs'
  properties: {
    plan: 'Basic'
    totalRetentionInDays: 30
  }
}

resource systemLogs 'Microsoft.OperationalInsights/workspaces/tables@2025-07-01' = {
  parent: workspace
  name: 'ContainerAppSystemLogs'
  properties: {
    plan: 'Analytics'
    retentionInDays: 30
    totalRetentionInDays: 30
  }
}

output consoleTableId string = consoleLogs.id
output systemTableId string = systemLogs.id

