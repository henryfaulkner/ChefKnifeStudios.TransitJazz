// =============================================================================
// telemetryStorage.bicep — Storage account + blob container for the logging
// sidecar (feature 013), plus a Storage Blob Data Contributor role assignment
// for the server managed identity (so DefaultAzureCredential can write parquet).
// =============================================================================

@description('Storage account name (3-24 chars, lowercase alphanumeric, globally unique).')
@minLength(3)
@maxLength(24)
param storageAccountName string

@description('Location.')
param location string

@description('Resource tags.')
param tags object

@description('Blob container the sidecar writes telemetry parquet into.')
param containerName string = 'parquet'

@description('Principal ID (object ID) of the server managed identity to grant blob write.')
param principalId string

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true // permitted for ops/SAS; the app uses MI + DefaultAzureCredential
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: storage
  name: 'default'
}

resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: containerName
  properties: {
    publicAccess: 'None'
  }
}

// Storage Blob Data Contributor built-in role
var blobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource blobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, principalId, blobDataContributorRoleId)
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributorRoleId)
  }
}

output accountName string = storage.name
output blobServiceUri string = storage.properties.primaryEndpoints.blob
output containerName string = container.name
