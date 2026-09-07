// SQL/database infrastructure module — Azure SQL server + database.
//
// IMPORTANT — this intentionally does NOT model the Quotes API's actual current runtime
// database: Program.cs configures EF Core with `UseSqlite`, writing to a file inside the
// Container App's own filesystem (`/tmp/quotes.db` under Linux). That storage is local to the
// container image, ephemeral across redeploys/restarts, and is not an Azure resource at all —
// there is nothing to represent for it in Bicep, and it must never be mislabeled as "Azure SQL".
//
// This module instead describes the Azure SQL infrastructure the API would need to move to a
// real, durable, shared database — mirroring the general-purpose serverless pattern already
// used elsewhere in this subscription (an existing `day8-index-shubh-2026` SQL server, kept
// untouched by this module; this deploys its own separate server/database rather than
// adopting that one).
@minLength(1)
@maxLength(63)
param sqlServerName string

@minLength(1)
param sqlDatabaseName string

param location string

param tags object = {}

@description('SQL auth administrator login. Not a secret by itself, but keep synthetic/non-personal in checked-in parameter files.')
param administratorLogin string

@secure()
@description('SQL auth administrator password. Must never appear as a literal in a checked-in .bicepparam file — supply via readEnvironmentVariable() at deploy time.')
param administratorLoginPassword string

@description('Also register an Azure AD principal as server administrator alongside SQL auth.')
param enableAadAdmin bool = false
param aadAdminLogin string = ''
param aadAdminObjectId string = ''
param aadAdminTenantId string = subscription().tenantId

@description('vCore/DTU SKU name, e.g. GP_S_Gen5 (General Purpose serverless).')
param skuName string = 'GP_S_Gen5'
param skuTier string = 'GeneralPurpose'
param skuFamily string = 'Gen5'
param skuCapacity int = 1

@description('Serverless minimum vCores, as a decimal string (e.g. "0.5").')
param minCapacity string = '0.5'

@description('Minutes of inactivity before serverless auto-pause; -1 disables auto-pause.')
param autoPauseDelayMinutes int = 60

param maxSizeBytes int = 34359738368

param minimalTlsVersion string = '1.2'
param publicNetworkAccess string = 'Enabled'

@description('Adds the 0.0.0.0-0.0.0.0 firewall rule that permits other Azure services (e.g. this Container App) to reach the server. Azure SQL evaluates this as "allow Azure services", not a literal public IP.')
param allowAzureServicesAccess bool = true

resource sqlServer 'Microsoft.Sql/servers@2022-11-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorLoginPassword
    minimalTlsVersion: minimalTlsVersion
    publicNetworkAccess: publicNetworkAccess
  }
}

resource aadAdmin 'Microsoft.Sql/servers/administrators@2022-11-01-preview' = if (enableAadAdmin) {
  parent: sqlServer
  name: 'ActiveDirectory'
  properties: {
    administratorType: 'ActiveDirectory'
    login: aadAdminLogin
    sid: aadAdminObjectId
    tenantId: aadAdminTenantId
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2022-11-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuTier
    family: skuFamily
    capacity: skuCapacity
  }
  properties: {
    minCapacity: json(minCapacity)
    autoPauseDelay: autoPauseDelayMinutes
    maxSizeBytes: maxSizeBytes
  }
}

resource allowAzureServicesFirewallRule 'Microsoft.Sql/servers/firewallRules@2022-11-01-preview' = if (allowAzureServicesAccess) {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output sqlServerName string = sqlServer.name
output sqlServerResourceId string = sqlServer.id
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabase.name
