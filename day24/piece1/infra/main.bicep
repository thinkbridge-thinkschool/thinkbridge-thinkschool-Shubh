// Day 24 Piece 1 — azd entrypoint, subscription scope.
//
// Unlike Day 23 (which deployed into a pre-existing resource group via `az deployment group`),
// this template is subscription-scoped so that `azd provision` — and the Azure Deployment
// Stack behind it — creates and OWNS the resource group itself, not just the resources inside
// it. That is what gives Day 24 "clean teardown" as a first-class property: deleting the stack
// can delete the resource group and everything in it, with nothing left to clean up by hand.
//
// Each azd environment (dev / prod) maps to its own isolated resource group
// (rg-day24-piece1-<environmentName>), so Day 24 never touches the Day 23 resource groups or
// the shared resources in rg-quotes-api.
targetScope = 'subscription'

@minLength(1)
@maxLength(16)
@description('azd environment name (dev or prod) — sourced from AZURE_ENV_NAME.')
param environmentName string

@description('Azure region for all resources — sourced from AZURE_LOCATION.')
param location string = 'eastasia'

@description('Container CPU cores per replica, as a decimal string.')
param containerCpu string = '0.5'

@description('Container memory per replica, e.g. 1.0Gi.')
param containerMemory string = '1.0Gi'

param minReplicas int = 1
param maxReplicas int = 3

@description('Azure SQL serverless vCore capacity.')
param sqlSkuCapacity int = 1

@description('Azure SQL serverless minimum vCores, as a decimal string.')
param sqlMinCapacity string = '0.5'

@description('Minutes of inactivity before Azure SQL serverless auto-pause; -1 disables auto-pause.')
param sqlAutoPauseDelayMinutes int = 60

@description('Whether the Service Bus namespace is zone-redundant (Standard tier supports this; untested for prod in Day 23).')
param serviceBusZoneRedundant bool = false

@secure()
@description('Azure SQL administrator password. Never set as a literal — supplied via a process environment variable (SQL_ADMIN_PASSWORD) at provision time, never persisted into azd environment state.')
param sqlAdministratorLoginPassword string

var resourceGroupName = 'rg-day24-piece1-${environmentName}'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: {
    app: 'quotes-api'
    exercise: 'day24-piece1'
    environment: environmentName
  }
}

module resources 'resources.bicep' = {
  name: 'resources-${environmentName}'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    containerCpu: containerCpu
    containerMemory: containerMemory
    minReplicas: minReplicas
    maxReplicas: maxReplicas
    sqlSkuCapacity: sqlSkuCapacity
    sqlMinCapacity: sqlMinCapacity
    sqlAutoPauseDelayMinutes: sqlAutoPauseDelayMinutes
    serviceBusZoneRedundant: serviceBusZoneRedundant
    sqlAdministratorLoginPassword: sqlAdministratorLoginPassword
  }
}

output AZURE_RESOURCE_GROUP string = rg.name
output containerAppName string = resources.outputs.containerAppName
output containerAppFqdn string = resources.outputs.containerAppFqdn
output sqlServerName string = resources.outputs.sqlServerName
output sqlServerFqdn string = resources.outputs.sqlServerFqdn
output sqlDatabaseName string = resources.outputs.sqlDatabaseName
output serviceBusNamespaceName string = resources.outputs.serviceBusNamespaceName
output serviceBusEndpoint string = resources.outputs.serviceBusEndpoint
output serviceBusTopicName string = resources.outputs.serviceBusTopicName
