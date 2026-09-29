// Day 32 (Day 25 zero-secret requirement): minimal per-environment Key Vault holding the
// SelfJwt signing key. The Container App references the secret by URI through its managed
// identity, so the key value never appears in Container App configuration.
//
// - Standard tier, Azure RBAC authorization (no access policies), soft delete on.
// - The API identity gets "Key Vault Secrets User" on the JWT secret ONLY — not the vault,
//   not the resource group — so it can read exactly one secret and nothing else.

@description('Name of the Key Vault (globally unique, 3-24 characters)')
param name string

@description('Location of the Key Vault')
param location string

@description('Tags applied to the Key Vault')
param tags object = {}

@description('Name of the secret holding the JWT signing key')
param jwtSecretName string = 'jwt-signing-key'

@description('The SelfJwt signing key (Program.cs Jwt:Key). Supplied via azd env as a secure parameter; never output.')
@secure()
param jwtKey string

@description('principalId of the managed identity that reads the JWT secret')
param jwtReaderPrincipalId string

var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    // Minimum allowed retention; fixed once the vault exists. Purge protection is left off
    // (it is irreversible and not required for this Dev/Prod exercise).
    softDeleteRetentionInDays: 7
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: false
    publicNetworkAccess: 'Enabled'
  }
}

resource jwtSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: jwtSecretName
  properties: {
    value: jwtKey
    contentType: 'text/plain'
    attributes: {
      enabled: true
    }
  }
}

resource jwtSecretReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(jwtSecret.id, jwtReaderPrincipalId, keyVaultSecretsUserRoleId)
  scope: jwtSecret
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: jwtReaderPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output name string = keyVault.name
// Versionless URI: the Container App always resolves the latest secret version. Consumers of
// this output deploy after this whole module, i.e. after the role assignment above exists.
output jwtSecretUri string = jwtSecret.properties.secretUri
