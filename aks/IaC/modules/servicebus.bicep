param serviceBusNamespaceName string
param location string
param billingQueueName string
param bookingQueueName string
param inventoryQueueName string
param reservationResponseQueueName string
param testQueueName string
param testResponseQueueName string

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2021-06-01-preview' = {
  name: serviceBusNamespaceName
  location: location
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {}
}


module billingQueueModule 'queue.bicep' = {
    name: 'billingDataQueue'
    params: {
        parentNamespaceName: serviceBusNamespaceName
        queueName: billingQueueName
    }
    dependsOn: [
        serviceBusNamespace
    ]
}


module bookingQueueModule 'queue.bicep' = {
    name: 'bookingDataQueue'
    params: {
        parentNamespaceName: serviceBusNamespaceName
        queueName: bookingQueueName
    }
    dependsOn: [
        serviceBusNamespace
    ]
}

module inventoryQueueModule 'queue.bicep' = {
    name: 'inventoryQueue'
    params: {
        parentNamespaceName: serviceBusNamespaceName
        queueName: inventoryQueueName
    }
    dependsOn: [
        serviceBusNamespace
    ]
}


module reservationResponseQueueModule 'queue.bicep' = {
    name: 'reservationResponseQueue'
    params: {
        parentNamespaceName: serviceBusNamespaceName
        queueName: reservationResponseQueueName
    }
    dependsOn: [
        serviceBusNamespace
    ]
}

module testQueueModule 'queue.bicep' = {
    name: 'testQueue'
    params: {
        parentNamespaceName: serviceBusNamespaceName
        queueName: testQueueName
    }
    dependsOn: [
        serviceBusNamespace
    ]
}


module testResponseQueueModule 'queue.bicep' = {
    name: 'testResponseDataQueue'
    params: {
        parentNamespaceName: serviceBusNamespaceName
        queueName: testResponseQueueName
    }
    dependsOn: [
        serviceBusNamespace
    ]
}
