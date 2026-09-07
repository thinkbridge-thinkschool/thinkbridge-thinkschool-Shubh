using '../main.bicep'

// DEV — attaches to the subscription's existing shared Container Apps environment and a
// public placeholder image (no registry needed), and provisions its own isolated SQL
// server/database and Service Bus namespace so nothing already running is touched.
//
// Before deploying, set the SQL admin password out-of-band (never commit it):
//   $env:SQL_ADMIN_PASSWORD_DEV = '<a strong password>'   (PowerShell)
//   export SQL_ADMIN_PASSWORD_DEV='<a strong password>'  (bash)

param environmentName = 'dev'
param location = 'eastasia'

// --- API ---
param containerAppName = 'ca-quotesapi-day23-dev'
param createManagedEnvironment = false
// Existing shared Container Apps environment for this subscription (Azure for Students
// permits exactly one managed environment total — see modules/api.bicep for why this
// attaches rather than creates one).
param existingManagedEnvironmentId = '/subscriptions/4d89877c-3cf4-491f-b999-03c9ff6bc7c3/resourceGroups/rg-quotes-api/providers/Microsoft.App/managedEnvironments/cae-yayuogblvizdw'
param useContainerRegistry = false
param containerImage = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
param containerCpu = '0.5'
param containerMemory = '1.0Gi'
param minReplicas = 1
param maxReplicas = 3
param targetPort = 8080
param externalIngress = true

// --- SQL ---
param sqlServerName = 'sql-day23-dev-shubh2026'
param sqlDatabaseName = 'quotesdb'
param sqlAdministratorLogin = 'quotesapiadmin'
param sqlAdministratorLoginPassword = readEnvironmentVariable('SQL_ADMIN_PASSWORD_DEV')
param sqlSkuName = 'GP_S_Gen5'
param sqlSkuCapacity = 1
param sqlMinCapacity = '0.5'
param sqlAutoPauseDelayMinutes = 60

// --- Service Bus ---
param serviceBusNamespaceName = 'sb-day23-dev-shubh2026'
param serviceBusSkuName = 'Standard'
param serviceBusZoneRedundant = false
param serviceBusTopicName = 'quote-events'
param serviceBusSubscriptions = [
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
