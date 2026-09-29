// Per-environment orchestration (rg-<env>): computes names and stage-specific settings, calls
// one module per concern, and wires their outputs together. Resource definitions live in
// ./modules; see each module's header for what it owns.

@description('The location used for all deployed resources')
param location string = resourceGroup().location

@description('Tags that will be applied to all resources')
param tags object = {}


param quotesApiExists bool

@description('Id of the user or app to assign application roles')
param principalId string

@description('Principal type of user or app')
param principalType string

@description('The signing key for the SelfJwt authentication scheme (Program.cs Jwt:Key). Supplied via azd env, never committed to source; stored only in Key Vault.')
@secure()
param jwtKey string

@description('Name of the azd environment (e.g. day32-dev, day32-prod). Used to build unique, environment-specific resource names so Dev and Prod never collide inside the shared Container Apps Environment.')
param environmentName string

@description('Deployment stage: dev or prod')
@allowed([
  'dev'
  'prod'
])
param deploymentStage string

@description('Display name/UPN of the SQL server\'s Entra admin (object id = principalId)')
param sqlAdminLogin string

@description('Redis sidecar image in the shared registry; empty omits the sidecar (first provision only)')
param redisSidecarImage string = ''

@description('Resource group holding the shared Dev/Prod resources (modules/shared.bicep)')
param sharedResourceGroupName string

param logAnalyticsWorkspaceId string
param containerRegistryName string
param containerRegistryLoginServer string
param containerAppsEnvironmentId string
param serviceBusNamespaceName string

var abbrs = loadJsonContent('./abbreviations.json')
var resourceToken = uniqueString(subscription().id, resourceGroup().id, location)
var isProd = deploymentStage == 'prod'

// Container App names must be unique within the (shared) Container Apps Environment, so the
// name is derived from the azd environment name: quotes-api-day32-dev / quotes-api-day32-prod.
var containerAppName = toLower('quotes-api-${environmentName}')

var serviceBusTopicName = 'quote-events-${deploymentStage}'
var serviceBusSubscriptionName = 'notifications'

// Day 32 approved scale limits: both scale to zero when idle. Prod's minimum is raised to 1
// only temporarily (outside Bicep) during the recorded demo.
var scaleMinReplicas = 0
var scaleMaxReplicas = isProd ? 3 : 2

module identity 'modules/managed-identity.bicep' = {
  name: 'managedIdentity'
  params: {
    name: '${abbrs.managedIdentityUserAssignedIdentities}quotesapi-${environmentName}'
    location: location
  }
}

module appInsights 'modules/app-insights.bicep' = {
  name: 'appInsights'
  params: {
    name: '${abbrs.insightsComponents}${environmentName}'
    location: location
    tags: tags
    logAnalyticsWorkspaceId: logAnalyticsWorkspaceId
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    serverName: '${abbrs.sqlServers}${environmentName}-${resourceToken}'
    location: location
    tags: tags
    deploymentStage: deploymentStage
    adminLogin: sqlAdminLogin
    adminPrincipalId: principalId
    adminPrincipalType: principalType
  }
}

module staticWebApp 'modules/static-web-app.bicep' = {
  name: 'staticWebApp'
  params: {
    name: '${abbrs.webStaticSites}quotes-${environmentName}'
    location: location
    tags: tags
  }
}

module keyVault 'modules/key-vault.bicep' = {
  name: 'keyVault'
  params: {
    // 3-24 characters: kv-day32dev<8> / kv-day32prod<8>.
    name: '${abbrs.keyVaultVaults}${replace(environmentName, '-', '')}${take(resourceToken, 8)}'
    location: location
    tags: tags
    jwtKey: jwtKey
    jwtReaderPrincipalId: identity.outputs.principalId
  }
}

module serviceBusTopic 'modules/servicebus-topic.bicep' = {
  name: 'serviceBusTopic-${environmentName}'
  scope: resourceGroup(sharedResourceGroupName)
  params: {
    namespaceName: serviceBusNamespaceName
    topicName: serviceBusTopicName
    subscriptionName: serviceBusSubscriptionName
    principalId: identity.outputs.principalId
  }
}

module quotesApi 'modules/container-app.bicep' = {
  name: 'containerApp'
  params: {
    name: containerAppName
    location: location
    tags: tags
    exists: quotesApiExists
    containerAppsEnvironmentId: containerAppsEnvironmentId
    sharedResourceGroupName: sharedResourceGroupName
    containerRegistryName: containerRegistryName
    containerRegistryLoginServer: containerRegistryLoginServer
    identityResourceId: identity.outputs.resourceId
    identityPrincipalId: identity.outputs.principalId
    identityClientId: identity.outputs.clientId
    appInsightsName: appInsights.outputs.name
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
    serviceBusNamespaceName: serviceBusNamespaceName
    serviceBusTopicName: serviceBusTopicName
    serviceBusSubscriptionName: serviceBusSubscriptionName
    corsAllowedOrigin: 'https://${staticWebApp.outputs.defaultHostname}'
    jwtSecretUri: keyVault.outputs.jwtSecretUri
    redisSidecarImage: redisSidecarImage
    scaleMinReplicas: scaleMinReplicas
    scaleMaxReplicas: scaleMaxReplicas
  }
}

output AZURE_RESOURCE_QUOTES_API_ID string = quotesApi.outputs.resourceId
output QUOTES_API_NAME string = quotesApi.outputs.name
output QUOTES_API_URL string = 'https://${quotesApi.outputs.fqdn}'
output SQL_SERVER_NAME string = sql.outputs.serverName
output SQL_SERVER_FQDN string = sql.outputs.serverFqdn
output SQL_DATABASE_NAME string = sql.outputs.databaseName
output MANAGED_IDENTITY_NAME string = identity.outputs.name
output STATIC_WEB_APP_NAME string = staticWebApp.outputs.name
output STATIC_WEB_APP_URL string = 'https://${staticWebApp.outputs.defaultHostname}'
output SERVICE_BUS_TOPIC string = serviceBusTopicName
output KEY_VAULT_NAME string = keyVault.outputs.name
