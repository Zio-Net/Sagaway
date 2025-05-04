param location string = resourceGroup().location

var cosmosDbAccountName = '$SagaWay-CosmosDB'
var serviceBusNamespaceName = '$SagaWay-SBNamespace'

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

