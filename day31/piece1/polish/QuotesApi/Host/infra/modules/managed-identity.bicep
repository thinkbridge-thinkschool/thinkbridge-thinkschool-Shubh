// The API's user-assigned managed identity. Every Azure dependency (ACR pull, Azure SQL,
// Service Bus, Key Vault) authenticates as this identity — no passwords or keys.

@description('Name of the user-assigned managed identity')
param name string

@description('Location of the identity')
param location string

module identity 'br/public:avm/res/managed-identity/user-assigned-identity:0.2.1' = {
  name: 'quotesApiidentity'
  params: {
    name: name
    location: location
  }
}

output name string = identity.outputs.name
output resourceId string = identity.outputs.resourceId
output principalId string = identity.outputs.principalId
output clientId string = identity.outputs.clientId
