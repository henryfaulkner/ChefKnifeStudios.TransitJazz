// =============================================================================
// containerApp.bicep — Container App (API + SignalR Hub + Worker)
// MI-authenticated pull from ACR, ingress, CORS, session affinity.
// =============================================================================

@description('Name of the Container App.')
param name string

@description('Location.')
param location string

@description('Resource tags.')
param tags object

@description('Container Apps Environment resource ID.')
param environmentId string

@description('User-assigned Managed Identity resource ID (for ACR pull).')
param managedIdentityId string

@description('ACR login server, e.g., chefknife.azurecr.io.')
param containerRegistryLoginServer string

@description('Full image reference including tag.')
param image string

@description('CPU cores (e.g., 0.5).')
param cpu string

@description('Memory (e.g., 1Gi).')
param memory string = '1Gi'

@description('Minimum replicas.')
param minReplicas int = 1

@description('Maximum replicas.')
param maxReplicas int = 1

@description('Container target port for ingress.')
param targetPort int = 8080

@description('CORS allowed origins.')
param corsAllowedOrigins array = []

@description('Environment variables for the container.')
param envVars array = []

@description('Key Vault-backed Container App secret definitions.')
param secretRefs array = []

resource app 'Microsoft.App/containerApps@2025-01-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityId}': {}
    }
  }
  properties: {
    environmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true                  // "Accept traffic from anywhere"
        targetPort: targetPort
        transport: 'auto'
        allowInsecure: false
        stickySessions: {
          affinity: 'sticky'            // Session affinity = true
        }
        ipSecurityRestrictions: []      // "Allow all traffic (default)"
        corsPolicy: {
          allowedOrigins: corsAllowedOrigins
          allowedHeaders: [ '*' ]
          allowCredentials: true        // SignalR with session affinity typically wants this
          allowedMethods: [
            'GET'
            'POST'
            'PUT'
            'DELETE'
            'PATCH'
            'OPTIONS'
          ]
        }
      }
      registries: [
        {
          server: containerRegistryLoginServer
          identity: managedIdentityId
        }
      ]
      secrets: secretRefs
    }
    template: {
      containers: [
        {
          name: 'server'
          image: image
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: envVars
          // The worker must finish loading its static route index before this revision
          // receives ingress traffic. The 11-minute allowance covers slow GTFS downloads
          // without treating a legitimate cold load as a liveness failure.
          probes: [
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: targetPort
              }
              initialDelaySeconds: 60
              periodSeconds: 60
              timeoutSeconds: 5
              failureThreshold: 10
              successThreshold: 1
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
}

output id string = app.id
output name string = app.name
output fqdn string = app.properties.configuration.ingress.fqdn
