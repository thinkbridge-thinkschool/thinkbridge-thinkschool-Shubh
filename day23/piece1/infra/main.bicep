// Day 23 Piece 1 — Quotes API infrastructure as code.
//
// Scope is deliberately 'resourceGroup' (not 'subscription'): this deploys into a resource
// group that already exists (create it once with `az group create`, see ../README.md),
// rather than owning the resource group resource itself. That keeps this template's blast
// radius limited to the resources it declares and matches the `az deployment group ...`
// commands used for validation.
targetScope = 'resourceGroup'

@minLength(1)
@maxLength(16)
@description('Short environment name, e.g. dev or prod. Used to keep resource names and tags distinguishable across environments.')
param environmentName string

@description('Azure region for all resources.')
param location string = 'eastasia'

var tags = {
  app: 'quotes-api'
  exercise: 'day23-piece1'
  environment: environmentName
}

// ============================== API (Azure Container Apps) ==============================
param containerAppName string
param createManagedEnvironment bool = false
param existingManagedEnvironmentId string = ''
param managedEnvironmentName string = ''
param logAnalyticsWorkspaceName string = ''

param useContainerRegistry bool = false
param createContainerRegistry bool = false
param containerRegistryName string = ''
param existingContainerRegistryName string = ''

param containerImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
param containerCpu string = '0.5'
param containerMemory string = '1.0Gi'
param minReplicas int = 1
param maxReplicas int = 10
param targetPort int = 8080
param externalIngress bool = true
param containerEnvVars array = []

@secure()
@description('Optional JWT signing key for the API container (Program.cs Jwt:Key). Leave empty to skip wiring the secret at all.')
param jwtSigningKey string = ''

// ============================== SQL (Azure SQL server/database) ==============================
param sqlServerName string
param sqlDatabaseName string = 'quotesdb'
param sqlAdministratorLogin string = 'quotesapiadmin'

@secure()
param sqlAdministratorLoginPassword string

param sqlEnableAadAdmin bool = false
param sqlAadAdminLogin string = ''
param sqlAadAdminObjectId string = ''

param sqlSkuName string = 'GP_S_Gen5'
param sqlSkuCapacity int = 1
param sqlMinCapacity string = '0.5'
param sqlAutoPauseDelayMinutes int = 60
param sqlMaxSizeBytes int = 34359738368

// ============================== Service Bus (namespace/topic/subscriptions) ==============================
param serviceBusNamespaceName string
param serviceBusSkuName string = 'Standard'
param serviceBusZoneRedundant bool = false
param serviceBusTopicName string = 'quote-events'
param serviceBusSubscriptions array = [
  {
    name: 'sub-a'
    maxDeliveryCount: 3
    lockDuration: 'PT30S'
  }
  {
    name: 'sub-b'
    maxDeliveryCount: 3
    lockDuration: 'PT30S'
  }
]

// ============================== Modules ==============================
module api 'modules/api.bicep' = {
  name: 'api-${environmentName}'
  params: {
    containerAppName: containerAppName
    location: location
    tags: tags
    createManagedEnvironment: createManagedEnvironment
    existingManagedEnvironmentId: existingManagedEnvironmentId
    managedEnvironmentName: managedEnvironmentName
    logAnalyticsWorkspaceName: logAnalyticsWorkspaceName
    useContainerRegistry: useContainerRegistry
    createContainerRegistry: createContainerRegistry
    containerRegistryName: containerRegistryName
    existingContainerRegistryName: existingContainerRegistryName
    containerImage: containerImage
    containerCpu: containerCpu
    containerMemory: containerMemory
    minReplicas: minReplicas
    maxReplicas: maxReplicas
    targetPort: targetPort
    externalIngress: externalIngress
    containerEnvVars: containerEnvVars
    containerSecrets: empty(jwtSigningKey) ? {} : {
      'jwt-key': jwtSigningKey
    }
    containerSecretEnvVars: empty(jwtSigningKey) ? [] : [
      {
        name: 'Jwt__Key'
        secretRef: 'jwt-key'
      }
    ]
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql-${environmentName}'
  params: {
    sqlServerName: sqlServerName
    sqlDatabaseName: sqlDatabaseName
    location: location
    tags: tags
    administratorLogin: sqlAdministratorLogin
    administratorLoginPassword: sqlAdministratorLoginPassword
    enableAadAdmin: sqlEnableAadAdmin
    aadAdminLogin: sqlAadAdminLogin
    aadAdminObjectId: sqlAadAdminObjectId
    skuName: sqlSkuName
    skuCapacity: sqlSkuCapacity
    minCapacity: sqlMinCapacity
    autoPauseDelayMinutes: sqlAutoPauseDelayMinutes
    maxSizeBytes: sqlMaxSizeBytes
  }
}

module servicebus 'modules/servicebus.bicep' = {
  name: 'servicebus-${environmentName}'
  params: {
    namespaceName: serviceBusNamespaceName
    location: location
    tags: tags
    skuName: serviceBusSkuName
    zoneRedundant: serviceBusZoneRedundant
    topicName: serviceBusTopicName
    subscriptions: serviceBusSubscriptions
    // The API's own managed identity is granted send access here — an output from the api
    // module flowing straight into the servicebus module's RBAC input.
    senderPrincipalIds: [
      api.outputs.managedIdentityPrincipalId
    ]
  }
}

// ============================== Outputs ==============================
output containerAppName string = api.outputs.containerAppName
output containerAppResourceId string = api.outputs.containerAppResourceId
output containerAppFqdn string = api.outputs.containerAppFqdn
output containerAppManagedIdentityPrincipalId string = api.outputs.managedIdentityPrincipalId

output sqlServerName string = sql.outputs.sqlServerName
output sqlServerFqdn string = sql.outputs.sqlServerFqdn
output sqlDatabaseName string = sql.outputs.sqlDatabaseName

output serviceBusNamespaceName string = servicebus.outputs.namespaceName
output serviceBusEndpoint string = servicebus.outputs.serviceBusEndpoint
output serviceBusTopicName string = servicebus.outputs.topicName
output serviceBusSubscriptionNames array = servicebus.outputs.subscriptionNames
