// Day 25 Piece 1 — Key Vault module.
//
// RBAC authorization mode (not legacy access policies), so access is granted via standard
// Azure role assignments like every other resource here. Holds exactly one secret: the
// self-issued JWT signing key, which is the one piece of configuration that genuinely cannot
// be replaced by managed identity (it is a symmetric key the API itself issues and validates
// tokens with, not a credential for reaching another Azure resource). The secret VALUE is
// never passed through Bicep — it is written directly with `az keyvault secret set` after this
// module deploys, so it never appears in a parameter file, deployment history, or this template.
@minLength(3)
@maxLength(24)
param keyVaultName string

param location string
param tags object = {}

param tenantId string = subscription().tenantId

// Linter flags this name as secret-shaped; it is an Azure AD object (principal) ID, not a secret value.
@description('Principal ID granted Key Vault Secrets User (read-only secret access), e.g. the API app\'s managed identity.')
param secretsUserPrincipalId string = ''

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

var keyVaultSecretsUserRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource secretsUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(secretsUserPrincipalId)) {
  name: guid(keyVault.id, secretsUserPrincipalId, 'KeyVaultSecretsUser')
  scope: keyVault
  properties: {
    roleDefinitionId: keyVaultSecretsUserRoleId
    principalId: secretsUserPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
output keyVaultResourceId string = keyVault.id
