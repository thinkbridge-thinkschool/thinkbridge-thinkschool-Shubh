// Day 32: resources shared by the Dev and Prod azd environments, deployed into one shared
// resource group. Both environments deploy this module with identical inputs, so a second
// deployment is an idempotent no-op rather than a duplicate.
//
// Why shared rather than per-environment:
// - Container Apps Environment: the subscription allows exactly one per region
//   (ManagedEnvironmentCount limit 1 in eastasia), so Dev and Prod run as separate
//   Container Apps inside it.
// - Container Registry: one Basic registry, so Prod runs the exact image digest Dev was
//   verified against.
// - Service Bus: Standard tier (topics are not available on Basic) has a fixed monthly base
//   charge, so one namespace is shared; each environment gets its own topic (see
//   servicebus-topic.bicep), which keeps their messages isolated.
// - Log Analytics: required by the Container Apps Environment; both environments' App
//   Insights components write into it, under one daily ingestion cap.

@description('The location used for all shared resources')
param location string

@description('Tags applied to every shared resource')
param tags object = {}

@description('Daily Log Analytics ingestion cap in GB — the main guard against runaway log cost')
param logAnalyticsDailyQuotaGb string = '0.2'

var abbrs = loadJsonContent('../abbreviations.json')
var resourceToken = uniqueString(subscription().id, resourceGroup().id, location)

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${abbrs.operationalInsightsWorkspaces}day32-${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: json(logAnalyticsDailyQuotaGb)
    }
  }
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: '${abbrs.containerRegistryRegistries}day32${resourceToken}'
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    // Managed Identity pull only (AcrPull role per app) — no admin user credentials.
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

// Consumption-only environment (no workloadProfiles block), so no dedicated compute is
// ever billed; apps pay only while replicas run.
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: '${abbrs.appManagedEnvironments}day32-${resourceToken}'
  location: location
  tags: tags
  properties: {
    zoneRedundant: false
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: '${abbrs.serviceBusNamespaces}day32-${resourceToken}'
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    // Entra ID / Managed Identity only, matching NotificationsModuleExtensions — no SAS keys.
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
  }
}

output logAnalyticsWorkspaceId string = logAnalytics.id
output containerRegistryName string = containerRegistry.name
output containerRegistryLoginServer string = containerRegistry.properties.loginServer
output containerAppsEnvironmentId string = containerAppsEnvironment.id
output serviceBusNamespaceName string = serviceBusNamespace.name
