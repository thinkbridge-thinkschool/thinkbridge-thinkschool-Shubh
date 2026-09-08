// Service Bus infrastructure module — namespace + topic + subscriptions.
//
// Reused as-is from day23/piece1/infra/modules/servicebus.bicep. Topology mirrors what Day
// 19/20/22's transactional outbox relay actually uses against the real `sb-day19-quotedemo`
// namespace: a single topic `quote-events` with two subscriptions, `sub-a` (competing
// consumers) and `sub-b` (independent consumer), each with MaxDeliveryCount=3 /
// LockDuration=PT30S. Topics require the Standard tier — Basic does not support
// topics/subscriptions at all.
//
// No connection string or SAS key is ever created or output here: the API's outbox relay
// authenticates purely via DefaultAzureCredential, so the only thing this module wires up
// beyond the messaging topology itself is an RBAC role assignment granting a principal
// data-plane access.
param namespaceName string

param location string

param tags object = {}

@description('Standard is required for topics/subscriptions; Basic only supports queues.')
param skuName string = 'Standard'

param zoneRedundant bool = false

param topicName string = 'quote-events'
param topicMaxSizeInMegabytes int = 1024

@description('Array of {name, maxDeliveryCount, lockDuration} objects — one per subscription on the topic.')
param subscriptions array = [
  {
    name: 'sub-a'
    maxDeliveryCount: 3
    lockDuration: 'PT30S'
  }
  {
    name: 'sub-b'
    maxDeliveryCount: 3
    lockDuration: 'PT30S'
  }
]

@description('Principal IDs (e.g. a Container App managed identity) granted Azure Service Bus Data Sender on this namespace — matches the outbox relay, which only ever publishes.')
param senderPrincipalIds array = []

@description('Principal IDs granted Azure Service Bus Data Receiver on this namespace.')
param receiverPrincipalIds array = []

resource namespaceRes 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuName
  }
  properties: {
    zoneRedundant: zoneRedundant
  }
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: namespaceRes
  name: topicName
  properties: {
    maxSizeInMegabytes: topicMaxSizeInMegabytes
  }
}

resource subs 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = [for sub in subscriptions: {
  parent: topic
  name: sub.name
  properties: {
    maxDeliveryCount: sub.maxDeliveryCount
    lockDuration: sub.lockDuration
    deadLetteringOnMessageExpiration: false
  }
}]

var serviceBusDataSenderRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
var serviceBusDataReceiverRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0')

resource senderRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in senderPrincipalIds: {
  name: guid(namespaceRes.id, principalId, 'ServiceBusDataSender')
  scope: namespaceRes
  properties: {
    roleDefinitionId: serviceBusDataSenderRoleId
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]

resource receiverRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in receiverPrincipalIds: {
  name: guid(namespaceRes.id, principalId, 'ServiceBusDataReceiver')
  scope: namespaceRes
  properties: {
    roleDefinitionId: serviceBusDataReceiverRoleId
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]

output namespaceName string = namespaceRes.name
output namespaceResourceId string = namespaceRes.id
output serviceBusEndpoint string = '${namespaceRes.name}.servicebus.windows.net'
output topicName string = topic.name
output subscriptionNames array = [for sub in subscriptions: sub.name]
