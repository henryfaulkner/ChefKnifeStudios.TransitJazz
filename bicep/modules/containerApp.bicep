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

@description('Key Vault URI for the TransitJazzDB connection string.')
param transitJazzDbSecretUri string = ''

@description('Grafana Cloud Prometheus range-query endpoint used by the historical collector.')
param grafanaMetricsReaderEndpoint string = ''

@description('Key Vault URI for the dedicated Grafana metrics:read credential.')
param grafanaMetricsReaderSecretUri string = ''

@description('Enable the historical statistics collector.')
param enableHistoricalStatistics bool = false

@description('Enable city/category capture for all configured cities except explicit exclusions after its separate schema and pilot gates pass.')
param enableCityCategoryInsights bool = false

@description('Configured city names to exclude from capture.')
param cityCategoryInsightsDisabledCities array = []

@description('Default maximum observation gap in seconds.')
param cityCategoryInsightsMaxObservationGapSeconds int = 30

@description('Optional per-city observation gap overrides.')
param cityCategoryInsightsCityMaxObservationGapSeconds object = {}

@description('Bounded aggregate envelope queue capacity.')
param cityCategoryInsightsQueueCapacity int = 256

@description('Maximum aggregate rows per database write transaction.')
param cityCategoryInsightsMaxBatchRows int = 128

@description('Database command timeout in seconds.')
param cityCategoryInsightsCommandTimeoutSeconds int = 5

@description('Total bounded transient write attempts.')
param cityCategoryInsightsMaxWriteAttempts int = 3

@description('Bounded writer shutdown drain in seconds.')
param cityCategoryInsightsShutdownDrainSeconds int = 15

var disabledCityEnvironmentVariables = [for (city, i) in cityCategoryInsightsDisabledCities: {
  name: 'CityCategoryInsights__Disabled_${i}'
  value: city
}]

var cityCadenceEnvironmentVariables = [for item in items(cityCategoryInsightsCityMaxObservationGapSeconds): {
  name: 'CityCategoryInsights__CityMaxObservationGapSeconds__${item.key}'
  value: string(item.value)
}]

@description('Keep historical statistics collection read-only.')
param historicalStatisticsDryRun bool = true

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
      secrets: concat(
        secretRefs,
        empty(transitJazzDbSecretUri) ? [] : [
          {
            name: 'transitjazz-db'
            keyVaultUrl: transitJazzDbSecretUri
            identity: managedIdentityId
          }
        ],
        enableHistoricalStatistics && !empty(grafanaMetricsReaderSecretUri) ? [
          {
            name: 'grafana-stats-reader'
            keyVaultUrl: grafanaMetricsReaderSecretUri
            identity: managedIdentityId
          }
        ] : []
      )
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
          env: concat(
            envVars,
            [
              {
                name: 'HistoricalStatistics__Enabled'
                value: string(enableHistoricalStatistics)
              }
              {
                name: 'HistoricalStatistics__DryRun'
                value: string(historicalStatisticsDryRun)
              }
              {
                name: 'HistoricalStatistics__SourceEndpoint'
                value: grafanaMetricsReaderEndpoint
              }
              {
                name: 'CityCategoryInsights__Enabled'
                value: string(enableCityCategoryInsights)
              }
              {
                name: 'CityCategoryInsights__MaxObservationGapSeconds'
                value: string(cityCategoryInsightsMaxObservationGapSeconds)
              }
              {
                name: 'CityCategoryInsights__QueueCapacity'
                value: string(cityCategoryInsightsQueueCapacity)
              }
              {
                name: 'CityCategoryInsights__MaxBatchRows'
                value: string(cityCategoryInsightsMaxBatchRows)
              }
              {
                name: 'CityCategoryInsights__CommandTimeoutSeconds'
                value: string(cityCategoryInsightsCommandTimeoutSeconds)
              }
              {
                name: 'CityCategoryInsights__MaxWriteAttempts'
                value: string(cityCategoryInsightsMaxWriteAttempts)
              }
              {
                name: 'CityCategoryInsights__ShutdownDrainSeconds'
                value: string(cityCategoryInsightsShutdownDrainSeconds)
              }
            ],
            disabledCityEnvironmentVariables,
            cityCadenceEnvironmentVariables,
            empty(transitJazzDbSecretUri) ? [] : [
              {
                name: 'ConnectionStrings__TransitJazzDB'
                secretRef: 'transitjazz-db'
              }
            ],
            enableHistoricalStatistics && !empty(grafanaMetricsReaderSecretUri) ? [
              {
                name: 'HistoricalStatistics__ReaderAuthorization'
                secretRef: 'grafana-stats-reader'
              }
            ] : []
          )
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
