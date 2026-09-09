// Day 25 Piece 1 — Azure App Service module.
//
// Hosts the identity-hardened API on Azure App Service (Linux), as the assignment
// specifically calls out App Service (day23/24 used Container Apps, which stays untouched).
// System-assigned managed identity is used here (the default preference per the assignment)
// rather than day23/24's user-assigned pattern — there is no cross-module RBAC ordering
// problem in this single-file module that would justify user-assigned, so this deliberately
// follows the simpler default instead of copying the existing precedent.
@minLength(2)
@maxLength(60)
param appServicePlanName string

@minLength(2)
@maxLength(60)
param webAppName string

param location string
param tags object = {}

@description('App Service Plan SKU. F1 (Free) avoids any cost for this proof-of-concept.')
param skuName string = 'F1'

@description('Non-secret application settings, as an array of {name, value}. Secret-shaped values must be passed as Key Vault references (@Microsoft.KeyVault(SecretUri=...)), never as literals.')
param appSettings array = []

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  tags: tags
  sku: {
    name: skuName
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: appSettings
    }
  }
}

output webAppName string = webApp.name
output webAppResourceId string = webApp.id
output webAppHostName string = webApp.properties.defaultHostName
output principalId string = webApp.identity.principalId
output tenantId string = webApp.identity.tenantId
