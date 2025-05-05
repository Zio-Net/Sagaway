param location string = resourceGroup().location

var cosmosDbAccountName = 'sagaway-cosmosdb'
var serviceBusNamespaceName = 'sagaway-sbnamespace'
var signalrName = 'sagaway123-signalr'
var containerRegistryName = 'sagaway-acr'

var billingQueueName = 'billing-queue'
var bookingQueueName = 'booking-queue'
var inventoryQueueName = 'inventory-queue'
var reservationResponseQueueName = 'reservation-response-queue'
var testQueueName = 'test-queue'
var testResponseQueueName = 'test-response-queue'

module cosmosDbModule 'modules/cosmosdb.bicep' = {
  name: 'CosmosDbModule'
  params: {
    cosmosDbAccountName: cosmosDbAccountName
    location: location
  }
}

module serviceBusModule 'modules/servicebus.bicep' = {
  name: 'ServiceBusModule'
  params: {
    serviceBusNamespaceName: serviceBusNamespaceName
    location: location
    billingQueueName: billingQueueName
    bookingQueueName: bookingQueueName
    inventoryQueueName: inventoryQueueName
    reservationResponseQueueName: reservationResponseQueueName
    testQueueName: testQueueName
    testResponseQueueName: testResponseQueueName
  }
}

module AKSModule 'modules/aks.bicep' = {
    name: 'KubernetesServiceModule'
    params: {
        location: location
    }
}

module SignalRModule 'modules/signalr.bicep' = {
    name: 'SignalRModule'
    params: {
        location: location
        signalrName: signalrName
    }
}

module ACRModule 'modules/acr.bicep' = {
    name: 'ContainerRegistryModule'
    params: {
        acrName: containerRegistryName
        location: location
    }
}