// Workspace-scoped read access for the named investigator principal.

@description('Existing Log Analytics workspace name.')
param workspaceName string

@description('Object ID of the intended human or agent investigator.')
param principalId string

var logAnalyticsReaderRoleDefinitionId = '73c42c96-874c-492b-b04d-ab87d138a893'

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: workspaceName
}

resource readerRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: workspace
  name: guid(workspace.id, principalId, logAnalyticsReaderRoleDefinitionId)
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', logAnalyticsReaderRoleDefinitionId)
  }
}

output id string = readerRole.id

