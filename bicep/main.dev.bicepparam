using './main.bicep'

param projectName = 'marta-jazz'
param environment = 'dev'
param location = 'eastus2'
param apexDomain = 'martajazz.com'

// Supply only an approved object ID; leave empty during local planning.
param logAnalyticsReaderPrincipalId = ''
param enableLegacyTelemetry = true

param containerRegistryName = 'chefknife'
param containerRegistryResourceGroup = 'general'

param serverImageTag = 'latest'

param repositoryUrl = 'https://github.com/henryfaulkner/ChefKnifeStudios.TransitJazz'
param repositoryToken = ''
