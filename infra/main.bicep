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

// Azure Cache for Redis
// resource redisCache 'Microsoft.Cache/redis@2023-08-01' = {
//   name: 'sagaway-redis' 
//   location: location
//   properties: {
//     sku: {
//       name: 'Basic'
//       family: 'C'
//       capacity: 0 // Basic C0 (250MB)
//     }
//     enableNonSslPort: false
//     minimumTlsVersion: '1.2'
//   }
// }

// resource redisCache 'Microsoft.Cache/redisEnterprise@2024-09-01-preview' = {
//   name: 'sagaway-redis'
//   location: location
//   sku: {
//     name: 'Balanced_B0'
//   }
//   identity: {
//     type: 'None'
//   }
//   properties: {
//     minimumTlsVersion: '1.2'    
//   }
// }

// resource redisEnterpriseDatabase 'Microsoft.Cache/redisEnterprise/databases@2024-09-01-preview' = {
//   name: 'default'
//   parent: redisCache
//   properties:{
//     clientProtocol: 'Encrypted'
//     port: 10000
//     clusteringPolicy: 'EnterpriseCluster'
//     evictionPolicy: 'NoEviction'
//     persistence:{
//       aofEnabled: false 
//       rdbEnabled: false
//     }
//     modules: [ // Uncomment this section again
//       {
//         name: 'RediSearch'
//       }
//     ]
//   }
// }

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
    isUI: false
  }
  {
    name: 'billing-management'
    image: '${containerRegistry}/sagaway.demo.billing.manager:latest'
    isUI: false
  }
  {
    name: 'inventory-management'
    image: '${containerRegistry}/sagaway.demo.inventory.manager:latest'
    isUI: false
  }
  {
    name: 'booking-management'
    image: '${containerRegistry}/sagaway.demo.booking.manager:latest'
    isUI: false
  }
  {
    name: 'reservation-ui'
    image: '${containerRegistry}/sagaway.demo.reservation.ui:latest'
    isUI: true
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
      {
        name: 'queryIndexes'
        value: '''
          [
            {
              "name": "customerNameIndex",
              "indexes": [
                {
                  "key": "customerName",
                  "type": "TEXT"
                }
              ]
            }
          ]
        '''
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

//use this if you want to use Redis as the state store
//Dapr State Store - Redis
// resource statestore 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
//   parent: containerEnv
//   name: 'statestore'
//   dependsOn: [
//     redisCache
//   ]
//   properties: {
//     componentType: 'state.redis'
//     version: 'v1'
//     metadata: [
//       {
//         name: 'redisHost'
//         // value: '${redisCache.properties.hostName}:${redisCache.properties.sslPort}'
//         value: '${redisCache.properties.hostName}:10000'

//       }
//       {
//         name: 'redisPassword'
//         secretRef: 'redis-password'
//       }
//       {
//         name: 'enableTLS'
//         value: 'true'
//       }
//       {
//         name: 'actorStateStore'
//         value: 'true'
//       }
//       {
//         name: 'queryIndexes'
//         value: '''
//           [
//             {
//               "name": "customerNameIndex",
//               "indexes": [
//                 {
//                   "key": "customerName",
//                   "type": "TEXT"
//                 }
//               ]
//             }
//           ]
//         '''
//       }
//     ]
//     secrets: [
//       {
//         name: 'redis-password'
//         // value: redisCache.listKeys().primaryKey
//         value: listKeys(
//           '${redisCache.id}/databases/default',
//           '2024-05-01-preview'
//         ).primaryKey
        
//       }
//     ]
//     scopes: [for app in apps: app.name]
//   }
// }


// // 3) Dapr state store with your queryIndexes
// resource statestore 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
//   parent: containerEnv
//   name: 'statestore'
//   dependsOn: [ redisEnterpriseDatabase ]
//   properties: {
//     componentType: 'state.redis'
//     version: 'v1'
//     metadata: [
//       {
//         name: 'redisHost'
//         value: '${redisCache.properties.hostName}:10000'
//       }
//       {
//         name: 'enableTLS'
//         value: 'true'
//       }
//       {
//         name: 'actorStateStore'
//         value: 'true'
//       }
//       { 
//         name: 'queryIndexes'
//         value: '''
//           [
//             {
//               "name": "customerNameIndex",
//               "indexes": [
//                 { "key": "customerName", "type": "TEXT" }
//               ]
//             }
//           ]
//         '''
//       }
//     ]
//     secrets: [
//       {
//         name: 'redis-password'
//         value: listKeys(
//           '${redisCache.id}/databases/default',
//           '2024-09-01-preview'
//         ).primaryKey
//       }
//     ]
//     scopes: [ for app in apps: app.name ]
//   }
// }

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
        appPort: app.isUI ? 80 : 8080
        appProtocol: 'http'
      }
      ingress: {
        external: true
        targetPort: app.isUI ? 80 : 8080
        transport: 'auto'
        allowInsecure: true
      }
    }
    template: {
      containers: [
        {
          name: app.name
          image: app.image
          env: [
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8080'
            }
            {
              name: 'PORT'
              value: '8080'
            }
          ]
          probes: [
            {
              type: 'startup'
              httpGet: {
                path: app.isUI ? '/' : '/healthz'
                port: app.isUI ? 80 : 8080
                scheme: 'http'
              }
              initialDelaySeconds: 10
              periodSeconds: 10
              failureThreshold: 3
              timeoutSeconds: 1
            }
            {
              type: 'liveness'
              httpGet: {
                path: app.isUI ? '/' : '/healthz' 
                port: app.isUI ? 80 : 8080
                scheme: 'http'
              }
              initialDelaySeconds: 10
              periodSeconds: 10
              failureThreshold: 3
              timeoutSeconds: 1
            }
            {
              type: 'readiness'
              httpGet: {
                path: app.isUI ? '/' : '/healthz'
                port: app.isUI ? 80 : 8080
                scheme: 'http'
              }
              initialDelaySeconds: 15
              periodSeconds: 10
              failureThreshold: 3
              timeoutSeconds: 1
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: app.isUI ? 3 : 1
      }
    }
  }
}]
