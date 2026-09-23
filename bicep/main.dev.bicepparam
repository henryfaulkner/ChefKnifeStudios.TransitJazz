using './main.bicep'

param projectName = 'marta-jazz'
param environment = 'dev'
param location = 'eastus2'
param apexDomain = 'martajazz.com'

// Supply only an approved object ID; leave empty during local planning.
param logAnalyticsReaderPrincipalId = ''
param containerRegistryName = 'chefknife'
param containerRegistryResourceGroup = 'general'

param serverImageTag = 'latest'

param transitJazzDbSecretUri = ''
param grafanaMetricsReaderEndpoint = ''
param grafanaMetricsReaderSecretUri = ''
param enableHistoricalStatistics = false
param historicalStatisticsDryRun = true
param historicalStatisticsInitialBackfill = false

param repositoryUrl = 'https://github.com/henryfaulkner/ChefKnifeStudios.TransitJazz'
param repositoryToken = ''
