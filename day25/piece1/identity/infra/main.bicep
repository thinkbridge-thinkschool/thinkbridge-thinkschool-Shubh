// Day 25 Piece 1 — Identity end-to-end, resource-group scope.
//
// A dedicated, isolated environment (rg-day25-piece1) separate from day23/24's resource
// groups — nothing here touches or depends on the Container Apps / SQL / Service Bus those
// days already deployed. See ../README.md for the full architecture writeup and the reasoning
// behind hosting on App Service (this assignment's explicit ask) instead of extending the
// existing Container Apps setup.
targetScope = 'resourceGroup'

@description('Azure region for all resources.')
param location string = 'eastasia'

var tags = {
  app: 'quotesapi-identity'
  exercise: 'day25-piece1'
}

// ============================== App Service (API host + managed identity) ==============================
param appServicePlanName string = 'asp-day25-shubh2026'
param webAppName string = 'app-day25-identity-shubh2026'
param appServiceSkuName string = 'B1'

// ============================== SQL (Microsoft Entra-only authentication) ==============================
param sqlServerName string = 'sql-day25-shubh2026'
param sqlDatabaseName string = 'identitydb'

@description('UPN of the Microsoft Entra principal to register as SQL server admin (used to grant the API\'s managed identity database access after deployment).')
param aadAdminLogin string

@description('Object ID (SID) of that Microsoft Entra admin principal.')
param aadAdminObjectId string

// ============================== Service Bus ==============================
param serviceBusNamespaceName string = 'sb-day25-shubh2026'
param serviceBusQueueName string = 'identity-queue'

// ============================== Key Vault ==============================
param keyVaultName string = 'kv-day25-shubh2026'

// ============================== Microsoft Entra ID application auth ==============================
@description('Tenant ID for Entra ID token validation (issuer).')
param entraTenantId string = subscription().tenantId

@description('Client ID (Application ID) of the Entra ID app registration created for this API.')
param entraClientId string

@description('Expected audience for validated Entra ID tokens, typically api://<clientId>.')
param entraAudience string

// ============================== App Service module ==============================
// appSettings are built entirely from deterministic resource NAMES (this module's own
// params) — never from another module's output — so there is no dependency cycle between
// appService and the sql/servicebus/keyvault modules below, even though those modules grant
// RBAC to appService's managed identity.
var appSettings = [
  { name: 'Sql__Server', value: '${sqlServerName}${environment().suffixes.sqlServerHostname}' }
  { name: 'Sql__Database', value: sqlDatabaseName }
  { name: 'ServiceBus__Namespace', value: '${serviceBusNamespaceName}.servicebus.windows.net' }
  { name: 'ServiceBus__QueueName', value: serviceBusQueueName }
  { name: 'KeyVault__Uri', value: 'https://${keyVaultName}${environment().suffixes.keyvaultDns}/' }
  { name: 'Entra__TenantId', value: entraTenantId }
  { name: 'Entra__ClientId', value: entraClientId }
  { name: 'Entra__Audience', value: entraAudience }
  { name: 'Jwt__Issuer', value: 'QuotesIdentityApi' }
  { name: 'Jwt__Audience', value: 'QuotesIdentityApiClient' }
  { name: 'Jwt__ExpiresInMinutes', value: '15' }
  // The ONLY genuinely secret piece of configuration — a Key Vault reference, never a literal
  // value. App Service resolves this server-side; the raw secret never appears in app
  // settings, environment variables visible to the running process's config dump, or here.
  { name: 'Jwt__Key', value: '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}${environment().suffixes.keyvaultDns}/secrets/jwt-signing-key/)' }
]

module appService 'modules/appservice.bicep' = {
  name: 'appservice-day25'
  params: {
    appServicePlanName: appServicePlanName
    webAppName: webAppName
    location: location
    tags: tags
    skuName: appServiceSkuName
    appSettings: appSettings
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql-day25'
  params: {
    sqlServerName: sqlServerName
    sqlDatabaseName: sqlDatabaseName
    location: location
    tags: tags
    aadAdminLogin: aadAdminLogin
    aadAdminObjectId: aadAdminObjectId
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  name: 'servicebus-day25'
  params: {
    namespaceName: serviceBusNamespaceName
    location: location
    tags: tags
    queueName: serviceBusQueueName
    senderPrincipalId: appService.outputs.principalId
    receiverPrincipalId: appService.outputs.principalId
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault-day25'
  params: {
    keyVaultName: keyVaultName
    location: location
    tags: tags
    secretsUserPrincipalId: appService.outputs.principalId
  }
}

output webAppName string = appService.outputs.webAppName
output webAppHostName string = appService.outputs.webAppHostName
output webAppPrincipalId string = appService.outputs.principalId
output sqlServerFqdn string = sql.outputs.sqlServerFqdn
output sqlDatabaseName string = sql.outputs.sqlDatabaseName
output serviceBusEndpoint string = serviceBus.outputs.serviceBusEndpoint
output serviceBusQueueName string = serviceBus.outputs.queueName
output keyVaultUri string = keyVault.outputs.keyVaultUri
