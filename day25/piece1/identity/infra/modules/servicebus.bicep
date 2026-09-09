// Day 25 Piece 1 — Service Bus module.
//
// Basic tier (queues only, no topics needed for this proof-of-concept — cheaper than the
// Standard tier day23/24 use for their topic/subscription fan-out). No connection string or
// SAS key is ever created or output: the API authenticates purely via DefaultAzureCredential
// (system-assigned managed identity in Azure), matching the pattern already proven in
// day22/piece1/QuotesApi's outbox relay. RBAC role assignments are scoped to the single queue,
// not the namespace, so the identity can reach only the one queue it actually uses.
param namespaceName string
param location string
param tags object = {}

@description('Basic tier is sufficient for a single queue; Standard/Premium are not needed here.')
param skuName string = 'Basic'

param queueName string = 'identity-queue'

@description('Principal ID granted Azure Service Bus Data Sender, scoped to the queue.')
param senderPrincipalId string = ''

@description('Principal ID granted Azure Service Bus Data Receiver, scoped to the queue.')
param receiverPrincipalId string = ''

resource namespaceRes 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuName
  }
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: namespaceRes
  name: queueName
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT30S'
  }
}

var serviceBusDataSenderRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
var serviceBusDataReceiverRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0')

resource senderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(senderPrincipalId)) {
  name: guid(queue.id, senderPrincipalId, 'ServiceBusDataSender')
  scope: queue
  properties: {
    roleDefinitionId: serviceBusDataSenderRoleId
    principalId: senderPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource receiverRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(receiverPrincipalId)) {
  name: guid(queue.id, receiverPrincipalId, 'ServiceBusDataReceiver')
  scope: queue
  properties: {
    roleDefinitionId: serviceBusDataReceiverRoleId
    principalId: receiverPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output namespaceName string = namespaceRes.name
output namespaceResourceId string = namespaceRes.id
output serviceBusEndpoint string = '${namespaceRes.name}.servicebus.windows.net'
output queueName string = queue.name
output queueResourceId string = queue.id
