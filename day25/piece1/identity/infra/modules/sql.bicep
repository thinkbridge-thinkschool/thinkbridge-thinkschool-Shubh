// Day 25 Piece 1 — Azure SQL module, Microsoft Entra-only authentication.
//
// Unlike day23/24's sql.bicep (SQL-auth administratorLogin + administratorLoginPassword),
// this server is created with NO SQL login and NO password at all: the inline `administrators`
// block on the server resource sets a Microsoft Entra principal as the server admin and sets
// azureADOnlyAuthentication: true, which disables SQL authentication entirely at the server
// level from the moment it is created. There is no "temporarily insecure" window and no
// secret parameter to ever leak into a parameter file or app setting.
@minLength(1)
@maxLength(63)
param sqlServerName string

@minLength(1)
param sqlDatabaseName string

param location string
param tags object = {}

@description('Display name / UPN of the Microsoft Entra principal to set as SQL server admin.')
param aadAdminLogin string

@description('Object ID (SID) of the Microsoft Entra admin principal.')
param aadAdminObjectId string

param aadAdminTenantId string = subscription().tenantId

@description('vCore/DTU SKU name, e.g. GP_S_Gen5 (General Purpose serverless).')
param skuName string = 'GP_S_Gen5'
param skuTier string = 'GeneralPurpose'
param skuFamily string = 'Gen5'
param skuCapacity int = 1

@description('Serverless minimum vCores, as a decimal string.')
param minCapacity string = '0.5'

@description('Minutes of inactivity before serverless auto-pause.')
param autoPauseDelayMinutes int = 60

param maxSizeBytes int = 34359738368
param minimalTlsVersion string = '1.2'

@description('Adds the 0.0.0.0-0.0.0.0 firewall rule that permits other Azure services to reach the server.')
param allowAzureServicesAccess bool = true

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: minimalTlsVersion
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: aadAdminLogin
      sid: aadAdminObjectId
      tenantId: aadAdminTenantId
      azureADOnlyAuthentication: true
    }
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
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

resource allowAzureServicesFirewallRule 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (allowAzureServicesAccess) {
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
