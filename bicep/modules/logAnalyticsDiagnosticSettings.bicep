// Environment-scoped Azure Monitor route for Container Apps stdout/stderr and system events.

@description('Managed environment name that owns the diagnostic setting.')
param environmentName string

@description('Existing Log Analytics workspace resource ID.')
param workspaceId string

resource environment 'Microsoft.App/managedEnvironments@2025-01-01' existing = {
  name: environmentName
}

resource diagnosticSetting 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'centralized-container-app-logs'
  scope: environment
  properties: {
    workspaceId: workspaceId
    logs: [
      {
        category: 'ContainerAppConsoleLogs'
        enabled: true
      }
      {
        category: 'ContainerAppSystemLogs'
        enabled: true
      }
    ]
  }
}

output id string = diagnosticSetting.id

