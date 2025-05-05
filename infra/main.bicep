param containerRegistry string

param containerRegistryUsername string
@secure()
param containerRegistryPassword string

param location string = resourceGroup().location
param cosmosAccountName string 
param cosmosDbName string 
param cosmosContainerName string 
param actorContainerName string = 'actorStateStore' // Added new container name for actor state store

// Container App Environment
resource containerEnv 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: 'sagaway-environment-new'
  location: location
  properties: {
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

// Service Bus Namespace
resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: 'sagaway-service-bus-demo'
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

// Queue Names
var billingQueueName = 'billing-queue'
var bookingQueueName = 'booking-queue'
var inventoryQueueName = 'inventory-queue'
var reservationResponseQueueName = 'reservation-response-queue'

// Queues
resource billingQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBus
  name: billingQueueName
  properties: {}
}

resource bookingQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBus
  name: bookingQueueName
  properties: {}
}

resource inventoryQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBus
  name: inventoryQueueName
  properties: {}
}

resource reservationResponseQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBus
  name: reservationResponseQueueName
  properties: {}
}

// Cosmos DB
resource cosmosdb_account 'Microsoft.DocumentDB/databaseAccounts@2023-11-15' = {
  name: cosmosAccountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
  }
}

resource cosmosdb_sql_database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-11-15' = {
  parent: cosmosdb_account
  name: cosmosDbName
  properties: {
    resource: {
      id: cosmosDbName
    }
  }
}

resource cosmosdb_sql_container 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-11-15' = {
  parent: cosmosdb_sql_database
  name: cosmosContainerName
  properties: {
    resource: {
      id: cosmosContainerName
      partitionKey: {
        paths: ['/partitionKey'] //partitionKey
        kind: 'Hash'
      }
    }
  }
}

// Add actor state store container
resource cosmosdb_actor_container 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-11-15' = {
  parent: cosmosdb_sql_database
  name: actorContainerName
  properties: {
    resource: {
      id: actorContainerName
      partitionKey: {
        paths: ['/partitionKey']
        kind: 'Hash'
      }
    }
  }
}

// Dapr Bindings - Service Bus Queues
resource billingQueueBinding 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: containerEnv
  name: billingQueueName
  dependsOn: [
    billingQueue
    
  ]
  properties: {
    componentType: 'bindings.azure.servicebusqueues'
    version: 'v1'
    metadata: [
      {
        name: 'connectionString'
        secretRef: 'sb-conn-string'
      }
      {
        name: 'queueName'
        value: billingQueueName
      }
    ]
    secrets: [
      {
        name: 'sb-conn-string'
        value: listKeys('${serviceBus.id}/AuthorizationRules/RootManageSharedAccessKey', '2022-10-01-preview').primaryConnectionString
      }
    ]
    scopes: ['billing-management', 'reservation-manager']
  }
}

resource bookingQueueBinding 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: containerEnv
  name: bookingQueueName
  dependsOn: [
    bookingQueue
    
  ]
  properties: {
    componentType: 'bindings.azure.servicebusqueues'
    version: 'v1'
    metadata: [
      {
        name: 'connectionString'
        secretRef: 'sb-conn-string'
      }
      {
        name: 'queueName'
        value: bookingQueueName
      }
    ]
    secrets: [
      {
        name: 'sb-conn-string'
        value: listKeys('${serviceBus.id}/AuthorizationRules/RootManageSharedAccessKey', '2022-10-01-preview').primaryConnectionString
      }
    ]
    scopes: ['booking-management', 'reservation-manager']
  }
}

resource inventoryQueueBinding 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: containerEnv
  name: inventoryQueueName
  dependsOn: [
    inventoryQueue
    
  ]
  properties: {
    componentType: 'bindings.azure.servicebusqueues'
    version: 'v1'
    metadata: [
      {
        name: 'connectionString'
        secretRef: 'sb-conn-string'
      }
      {
        name: 'queueName'
        value: inventoryQueueName
      }
    ]
    secrets: [
      {
        name: 'sb-conn-string'
        value: listKeys('${serviceBus.id}/AuthorizationRules/RootManageSharedAccessKey', '2022-10-01-preview').primaryConnectionString
      }
    ]
    scopes: ['inventory-management', 'reservation-manager']
  }
}

resource reservationResponseQueueBinding 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: containerEnv
  name: reservationResponseQueueName
  dependsOn: [
    reservationResponseQueue

  ]
  properties: {
    componentType: 'bindings.azure.servicebusqueues'
    version: 'v1'
    metadata: [
      {
        name: 'connectionString'
        secretRef: 'sb-conn-string'
      }
      {
        name: 'queueName'
        value: reservationResponseQueueName
      }
    ]
    secrets: [
      {
        name: 'sb-conn-string'
        value: listKeys('${serviceBus.id}/AuthorizationRules/RootManageSharedAccessKey', '2022-10-01-preview').primaryConnectionString
      }
    ]
    scopes: [for app in apps: app.name]
  }
}

// Apps Array
var apps = [
  {
    name: 'reservation-manager'
    image: '${containerRegistry}/sagaway.demo.reservation.manager:latest'

  }
  {
    name: 'billing-management'
    image: '${containerRegistry}/sagaway.demo.billing.manager:latest'

  }
  {
    name: 'inventory-management'
    image: '${containerRegistry}/sagaway.demo.inventory.manager:latest'
  
  }
  {
    name: 'booking-management'
    image: '${containerRegistry}/sagaway.demo.booking.manager:latest'
  }
  
]

// Dapr Actor State Store - CosmosDB
resource actorstatestore 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: containerEnv
  name: 'actorstatestore'
  dependsOn: [cosmosdb_actor_container]
  properties: {
    componentType: 'state.azure.cosmosdb'
    version: 'v1'
    metadata: [
      {
        name: 'url'
        value: cosmosdb_account.properties.documentEndpoint
      }
      {
        name: 'masterkey'
        secretRef: 'cosmos-master-key'
      }
      {
        name: 'database'
        value: cosmosDbName
      }
      {
        name: 'collection'
        value: actorContainerName
      }
      {
        name: 'actorStateStore'
        value: 'true'
      }
    ]
    secrets: [
      {
        name: 'cosmos-master-key'
        value: cosmosdb_account.listKeys().primaryMasterKey
      }
    ]
    scopes: [for app in apps: app.name]
  }
}

// Dapr State Store - CosmosDB (updated to match statestore.yaml structure)
resource statestore 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: containerEnv
  name: 'statestore'
  dependsOn: [cosmosdb_sql_container]
  properties: {
    componentType: 'state.azure.cosmosdb'
    version: 'v1'
    metadata: [
      {
        name: 'url'
        value: cosmosdb_account.properties.documentEndpoint
      }
      {
        name: 'masterkey'
        secretRef: 'cosmos-master-key'
      }
      {
        name: 'database'
        value: cosmosDbName
      }
      {
        name: 'collection'
        value: cosmosContainerName
      }
      // {
      //   name: 'queryIndexes'
      //   value: '''
      //     [
      //       {
      //         "name": "customerNameIndex",
      //         "indexes": [
      //           {
      //             "key": "customerName",
      //             "type": "TEXT"
      //           }
      //         ]
      //       }
      //     ]
      //   '''
      // }
    ]
    secrets: [
      {
        name: 'cosmos-master-key'
        value: cosmosdb_account.listKeys().primaryMasterKey
      }
    ]
    scopes: [for app in apps: app.name]
  }
}


// Container Apps
resource containerApps 'Microsoft.App/containerApps@2023-05-01' = [for app in apps: {
  name: app.name
  location: location
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      secrets: [
        {
          name: 'registry-password'
          value: containerRegistryPassword
        }
      ]
      registries: [
        {
          server: containerRegistry
          username: containerRegistryUsername
          passwordSecretRef: 'registry-password'
        }
      ]
      dapr: {
        enabled: true
        appId: app.name
        appPort: 80 // Changed from 8080
        enableApiLogging: true
      }
      ingress: {
        external: true
        targetPort: 80 // Changed from 8080
        transport: 'auto'
      }
    }
    template: {
      containers: [
        {
          name: app.name
          image: app.image
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}]


