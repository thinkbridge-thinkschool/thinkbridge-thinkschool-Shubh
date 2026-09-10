targetScope = 'resourceGroup'

@description('Name of the existing Container Registry (deployed in this modules target resource group) to grant pull access on.')
param registryName string

@description('principalId of the identity being granted AcrPull.')
param principalId string

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

// AcrPull built-in role definition ID — the same one the original resources.bicep
// (Day 13) grants its own identity on its own registry. Additive: does not remove or
// alter any existing role assignment on this registry.
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, principalId, '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  scope: registry
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '7f951dda-4ed3-4680-a7ca-43fe172d538d'
    )
  }
}
