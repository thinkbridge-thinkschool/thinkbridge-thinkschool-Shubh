// Day 32: one environment's topic + subscription on the shared Service Bus namespace, and
// that environment's managed identity granted send/receive on its own topic only — Dev can
// never read or publish Prod's messages, or vice versa.

@description('Name of the existing shared Service Bus namespace')
param namespaceName string

@description('Topic name for this environment (e.g. quote-events-dev)')
param topicName string

@description('Subscription name the Notifications consumer reads from')
param subscriptionName string

@description('principalId of the environment\'s managed identity')
param principalId string

var serviceBusDataSenderRoleId = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
var serviceBusDataReceiverRoleId = '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: namespaceName
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: topicName
}

resource subscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: topic
  name: subscriptionName
  properties: {
    maxDeliveryCount: 10
  }
}

resource senderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(topic.id, principalId, serviceBusDataSenderRoleId)
  scope: topic
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataSenderRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

// Receiver at topic scope covers the topic's subscriptions.
resource receiverRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(topic.id, principalId, serviceBusDataReceiverRoleId)
  scope: topic
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataReceiverRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
