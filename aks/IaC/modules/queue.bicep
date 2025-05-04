param queueName string
param parentNamespaceName string

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2021-06-01-preview' existing = {
  name: parentNamespaceName
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2021-06-01-preview' = {
  parent: serviceBusNamespace
  name: queueName
  properties: {
    deadLetteringOnMessageExpiration: false
    defaultMessageTimeToLive: 'PT1H'
    lockDuration: 'PT1M'
    maxDeliveryCount: 10
    requiresDuplicateDetection: false
    requiresSession: false
  }
}