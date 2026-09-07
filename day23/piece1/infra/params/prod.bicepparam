using '../main.bicep'

// PROD — sized and isolated differently from dev (larger SQL tier, zone-redundant Service
// Bus, its own Container Registry, more replicas), but attaches to the SAME shared Container
// Apps environment as dev. This subscription (Azure for Students) enforces a GLOBAL cap of
// exactly one Container Apps Environment total, confirmed by running this template with
// createManagedEnvironment=true against both eastasia ("MaxNumberOfRegionalEnvironmentsInSubExceeded")
// and koreacentral ("MaxNumberOfGlobalEnvironmentsInSubExceeded") — so no region choice avoids
// it. modules/api.bicep still supports createManagedEnvironment=true for a subscription
// without that cap; it's simply not exercised here. See ../README.md for details.
//
// Intended for `az deployment group what-if` only; do not deploy without explicit sign-off.
//
// Before running what-if, set the SQL admin password out-of-band (never commit it):
//   $env:SQL_ADMIN_PASSWORD_PROD = '<a strong password>'   (PowerShell)
//   export SQL_ADMIN_PASSWORD_PROD='<a strong password>'  (bash)

param environmentName = 'prod'
param location = 'eastasia'

// --- API ---
param containerAppName = 'ca-quotesapi-day23-prod'
param createManagedEnvironment = false
param existingManagedEnvironmentId = '/subscriptions/4d89877c-3cf4-491f-b999-03c9ff6bc7c3/resourceGroups/rg-quotes-api/providers/Microsoft.App/managedEnvironments/cae-yayuogblvizdw'
param useContainerRegistry = true
param createContainerRegistry = true
param containerRegistryName = 'acrquotesapiday23prod'
param containerImage = 'acrquotesapiday23prod.azurecr.io/quotes-api:latest'
param containerCpu = '1.0'
param containerMemory = '2.0Gi'
param minReplicas = 2
param maxReplicas = 10
param targetPort = 8080
param externalIngress = true

// --- SQL ---
param sqlServerName = 'sql-day23-prod-shubh2026'
param sqlDatabaseName = 'quotesdb'
param sqlAdministratorLogin = 'quotesapiadmin'
param sqlAdministratorLoginPassword = readEnvironmentVariable('SQL_ADMIN_PASSWORD_PROD')
param sqlSkuName = 'GP_S_Gen5'
param sqlSkuCapacity = 2
param sqlMinCapacity = '1'
param sqlAutoPauseDelayMinutes = -1

// --- Service Bus ---
param serviceBusNamespaceName = 'sb-day23-prod-shubh2026'
param serviceBusSkuName = 'Standard'
param serviceBusZoneRedundant = true
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
