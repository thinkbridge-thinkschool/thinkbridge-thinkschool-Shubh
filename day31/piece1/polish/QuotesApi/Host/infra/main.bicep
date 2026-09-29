targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment that can be used as part of naming resource convention')
param environmentName string

@minLength(1)
@description('Primary location for all resources')
param location string


param quotesApiExists bool

@description('Id of the user or app to assign application roles')
param principalId string

@description('Principal type of user or app')
param principalType string

@secure()
param jwtKey string

@description('Deployment stage. Drives replica limits and the SQL database SKU (Dev: Azure SQL free offer; Prod: Basic).')
@allowed([
  'dev'
  'prod'
])
param deploymentStage string

@description('Display name/UPN of the Entra ID user set as the SQL server\'s Entra admin (the object id is principalId).')
param sqlAdminLogin string

@description('Redis sidecar image. Empty on the very first provision, before the image has been imported into the new registry; the sidecar is added once it is set.')
param redisSidecarImage string = ''

@description('Name of the resource group holding resources shared by Dev and Prod')
param sharedResourceGroupName string = 'rg-day32-shared'

// Tags that should be applied to all resources.
//
// Note that 'azd-service-name' tags should be applied separately to service host resources.
// Example usage:
//   tags: union(tags, { 'azd-service-name': <service name in azure.yaml> })
var tags = {
  'azd-env-name': environmentName
}

// Shared resources carry no azd-env-name tag: they belong to neither environment alone.
var sharedTags = {
  purpose: 'day32-shared'
}

// Organize resources in a resource group
resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

resource sharedRg 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: sharedResourceGroupName
  location: location
  tags: sharedTags
}

module shared 'modules/shared.bicep' = {
  scope: sharedRg
  name: 'shared'
  params: {
    location: location
    tags: sharedTags
  }
}

module resources 'resources.bicep' = {
  scope: rg
  name: 'resources'
  params: {
    location: location
    tags: tags
    principalId: principalId
    principalType: principalType
    quotesApiExists: quotesApiExists
    jwtKey: jwtKey
    environmentName: environmentName
    deploymentStage: deploymentStage
    sqlAdminLogin: sqlAdminLogin
    redisSidecarImage: redisSidecarImage
    sharedResourceGroupName: sharedRg.name
    logAnalyticsWorkspaceId: shared.outputs.logAnalyticsWorkspaceId
    containerRegistryName: shared.outputs.containerRegistryName
    containerRegistryLoginServer: shared.outputs.containerRegistryLoginServer
    containerAppsEnvironmentId: shared.outputs.containerAppsEnvironmentId
    serviceBusNamespaceName: shared.outputs.serviceBusNamespaceName
  }
}
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = shared.outputs.containerRegistryLoginServer
output AZURE_CONTAINER_REGISTRY_NAME string = shared.outputs.containerRegistryName
output AZURE_RESOURCE_QUOTES_API_ID string = resources.outputs.AZURE_RESOURCE_QUOTES_API_ID
output QUOTES_API_NAME string = resources.outputs.QUOTES_API_NAME
output QUOTES_API_URL string = resources.outputs.QUOTES_API_URL
output SQL_SERVER_NAME string = resources.outputs.SQL_SERVER_NAME
output SQL_SERVER_FQDN string = resources.outputs.SQL_SERVER_FQDN
output SQL_DATABASE_NAME string = resources.outputs.SQL_DATABASE_NAME
output MANAGED_IDENTITY_NAME string = resources.outputs.MANAGED_IDENTITY_NAME
output STATIC_WEB_APP_NAME string = resources.outputs.STATIC_WEB_APP_NAME
output STATIC_WEB_APP_URL string = resources.outputs.STATIC_WEB_APP_URL
output SERVICE_BUS_NAMESPACE string = shared.outputs.serviceBusNamespaceName
output SERVICE_BUS_TOPIC string = resources.outputs.SERVICE_BUS_TOPIC
output KEY_VAULT_NAME string = resources.outputs.KEY_VAULT_NAME
