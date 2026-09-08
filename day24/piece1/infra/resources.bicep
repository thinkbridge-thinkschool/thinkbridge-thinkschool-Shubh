// Day 24 Piece 1 — Quotes API infrastructure, resource-group scope.
//
// Orchestrates the same three modules Day 23 used (api / sql / servicebus), resource-group
// scoped exactly like day23/piece1/infra/main.bicep. This file is invoked as a module FROM
// main.bicep (subscription scope), which creates the isolated resource group first — so azd
// and the Deployment Stack own the resource group itself as well as everything inside it,
// rather than requiring one to be pre-created out of band.
targetScope = 'resourceGroup'

@minLength(1)
@maxLength(16)
param environmentName string

@description('Azure region for all resources.')
param location string

var tags = {
  app: 'quotes-api'
  exercise: 'day24-piece1'
  environment: environmentName
}

// ============================== API (Azure Container Apps) ==============================
// Azure for Students enforces a hard, subscription-wide cap of exactly one Container Apps
// managed environment (confirmed during Day 23 — see day23/piece1/infra/README.md). It already
// exists as cae-yayuogblvizdw in rg-quotes-api. Both dev and prod attach to it as an external
// resource ID (read-only property reference, not a declared/managed Bicep resource) so the
// Deployment Stack never tries to own, modify, or delete it.
param containerAppName string = 'ca-quotesapi-day24-${environmentName}'
param existingManagedEnvironmentId string = '/subscriptions/4d89877c-3cf4-491f-b999-03c9ff6bc7c3/resourceGroups/rg-quotes-api/providers/Microsoft.App/managedEnvironments/cae-yayuogblvizdw'

// A public, anonymously-pullable placeholder image is used in both dev and prod. Day 24 is
// infra/IaC only (no application build/push pipeline in scope), and Day 23's own prod
// parameter file already documents that pointing a Container App at an image that was never
// pushed to a registry risks the initial revision failing — so this avoids that failure mode
// entirely while still exercising the real Container Apps resource end to end.
param containerImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
param containerCpu string = '0.5'
param containerMemory string = '1.0Gi'
param minReplicas int = 1
param maxReplicas int = 3
param targetPort int = 8080
param externalIngress bool = true

// ============================== SQL (Azure SQL server/database) ==============================
param sqlServerName string = 'sql-day24-${environmentName}-shubh2026'
param sqlDatabaseName string = 'quotesdb'
param sqlAdministratorLogin string = 'quotesapiadmin'

@secure()
param sqlAdministratorLoginPassword string

param sqlSkuName string = 'GP_S_Gen5'
param sqlSkuCapacity int = 1
param sqlMinCapacity string = '0.5'
param sqlAutoPauseDelayMinutes int = 60

// ============================== Service Bus (namespace/topic/subscriptions) ==============================
param serviceBusNamespaceName string = 'sb-day24-${environmentName}-shubh2026'
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
    createManagedEnvironment: false
    existingManagedEnvironmentId: existingManagedEnvironmentId
    useContainerRegistry: false
    containerImage: containerImage
    containerCpu: containerCpu
    containerMemory: containerMemory
    minReplicas: minReplicas
    maxReplicas: maxReplicas
    targetPort: targetPort
    externalIngress: externalIngress
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
    skuName: sqlSkuName
    skuCapacity: sqlSkuCapacity
    minCapacity: sqlMinCapacity
    autoPauseDelayMinutes: sqlAutoPauseDelayMinutes
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
