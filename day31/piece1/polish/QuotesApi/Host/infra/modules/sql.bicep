// Entra-ID-only Azure SQL (no SQL logins), same model as the Day 25/29 server: the app
// authenticates with its managed identity via SqlManagedIdentityConnectionInterceptor.
//
// The subscription allows exactly one Azure SQL free-offer database, so Dev gets it
// (serverless, auto-pauses, and pauses rather than bills once the monthly free allowance is
// used up) and Prod gets Basic 5 DTU / 2 GB, which is always on — no resume delay during the
// demo and no startup-migration timeout against a paused database.

@description('Name of the SQL logical server (globally unique)')
param serverName string

@description('Name of the application database')
param databaseName string = 'quotesapi'

@description('Location of the server and database')
param location string

@description('Tags applied to the server and database')
param tags object = {}

@description('Deployment stage: dev (free-offer serverless) or prod (Basic 5 DTU)')
@allowed([
  'dev'
  'prod'
])
param deploymentStage string

@description('Display name/UPN of the Entra admin')
param adminLogin string

@description('Object id of the Entra admin')
param adminPrincipalId string

@description('Principal type of the Entra admin (User, Group or Application)')
param adminPrincipalType string

var isProd = deploymentStage == 'prod'

var databaseSku = isProd
  ? {
      name: 'Basic'
      tier: 'Basic'
      capacity: 5
    }
  : {
      name: 'GP_S_Gen5'
      tier: 'GeneralPurpose'
      family: 'Gen5'
      capacity: 1
    }

var databaseProperties = isProd
  ? {
      maxSizeBytes: 2147483648
      zoneRedundant: false
      requestedBackupStorageRedundancy: 'Local'
    }
  : {
      maxSizeBytes: 34359738368
      zoneRedundant: false
      requestedBackupStorageRedundancy: 'Local'
      useFreeLimit: true
      freeLimitExhaustionBehavior: 'AutoPause'
      autoPauseDelay: 60
      minCapacity: json('0.5')
    }

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: adminLogin
      sid: adminPrincipalId
      tenantId: tenant().tenantId
      principalType: adminPrincipalType
    }
  }
}

// Container Apps consumption has no fixed outbound IPs, so "Allow Azure services" (the
// 0.0.0.0 rule) is what lets the app reach the server; Entra-only auth still applies.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  tags: tags
  sku: databaseSku
  properties: databaseProperties
}

output serverName string = sqlServer.name
output serverFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = database.name
