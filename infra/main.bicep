param containerRegistry string

param containerRegistryUsername string
@secure()
param containerRegistryPassword string

param location string = resourceGroup().location
param cosmosAccountName string 
param cosmosDbName string 
param cosmosContainerName string 
param actorContainerName string = 'actorStateStore' 
param port int = 8080 
// Queue Names
var billingQueueName = 'billing-queue'
var bookingQueueName = 'booking-queue'
var inventoryQueueName = 'inventory-queue'
var reservationResponseQueueName = 'reservation-response-queue'

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
    scopes: union(backendAppNames, ['reservation-manager']) // Use variable in union
  }
}

// Azure SignalR Service
resource signalR 'Microsoft.SignalRService/signalR@2023-02-01' = {
  name: 'sagaway-signalr-demo' // Choose a unique name
  location: location
  sku: {
    name: 'Free_F1' // Or choose a different SKU like Standard_S1
    tier: 'Free'   // Or 'Standard'
    capacity: 1
  }
  kind: 'SignalR'
  properties: {
    features: [
      {
        flag: 'ServiceMode'
        value: 'Serverless' // Use Serverless mode as it's likely used with Dapr bindings
      }
    ]
    cors: {
      allowedOrigins: [
        '*' // Adjust for production environments
      ]
    }
  }
}

// Dapr Binding - SignalR
resource reservationCallbackBinding 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: containerEnv
  name: 'reservationcallback' // Matches the component name used in the code and local YAML
  // dependsOn removed as Bicep infers it from listKeys usage
  properties: {
    componentType: 'bindings.azure.signalr'
    version: 'v1'
    metadata: [
      {
        name: 'connectionString'
        secretRef: 'signalr-conn-string'
      }
      {
        name: 'hub'
        value: 'reservationcallback' // Matches the hub name used in the code and local YAML
      }
    ] 
    secrets: [
      {
        name: 'signalr-conn-string'
        value: signalR.listKeys().primaryConnectionString  
      }
    ]
    scopes: ['reservation-manager'] // Only scope to the app that needs it
  }
}

// Apps Array (Backend Services Only)
var backendApps = [
  // Removed reservation-manager from here
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

// Variable for backend app names
var backendAppNames = [for app in backendApps: app.name]

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
    scopes: union(backendAppNames, ['reservation-manager']) // Use variable in union
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
    ]
    secrets: [
      {
        name: 'cosmos-master-key'
        value: cosmosdb_account.listKeys().primaryMasterKey
      }
    ]
    scopes: union(backendAppNames, ['reservation-manager']) // Use variable in union
  }
}

// Reservation Manager Container App (Defined Separately)
var reservationManagerAppName = 'reservation-manager'
var reservationManagerImage = '${containerRegistry}/sagaway.demo.reservation.manager:latest'

resource reservationManagerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: reservationManagerAppName
  location: location
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      secrets: union(
        [
          {
            name: 'registry-password'
            value: containerRegistryPassword
          }
        ],
        [
          {
            name: 'signalr-connection-string-secret'
            value: signalR.listKeys().primaryConnectionString
          }
        ]
      )
      registries: [
        {
          server: containerRegistry
          username: containerRegistryUsername
          passwordSecretRef: 'registry-password'
        }
      ]
      dapr: {
        enabled: true
        appId: reservationManagerAppName
        appPort: 80 // Changed from 'port' to 80
      }
      ingress: {
        external: true
        targetPort: 80 // Changed from 'port' to 80
        exposedPort: port // Set exposedPort to the 'port' parameter (8080)
        transport: 'auto'
      }
    }
    template: {
      containers: [
        {
          name: reservationManagerAppName
          image: reservationManagerImage
          env: [
            {
              name: 'Azure__SignalR__ConnectionString'
              secretRef: 'signalr-connection-string-secret'
            }
            // Add ASPNETCORE_URLS to force listening on port 80
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:80' // Changed from 'http://+:8080'
            }
            
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// Backend Container Apps (Loop)
resource backendContainerApps 'Microsoft.App/containerApps@2023-05-01' = [for app in backendApps: {
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
        appPort: port // Ensure this is 80
      }
      ingress: {
        external: true
        targetPort: port // Ensure this is 80
        transport: 'auto'
      }
    }
    template: {
      containers: [
        {
          name: app.name
          image: app.image
          env: [
            // Add ASPNETCORE_URLS to force listening on port 80
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8080'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}]

// Reservation UI Container App (Defined Separately)
var reservationUiAppName = 'reservation-ui'
var reservationUiImage = '${containerRegistry}/sagaway.demo.reservation.ui:latest' // Assumed image name

resource reservationUiApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: reservationUiAppName
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
      // Dapr is disabled for the frontend WASM app
      dapr: {
        enabled: false
      }
      ingress: {
        external: true
        targetPort: 80 // Port Nginx/server in the UI container listens on
        transport: 'auto'
      }
    }
    template: {
      containers: [
        {
          name: reservationUiAppName
          image: reservationUiImage
          env: [
            {
              name: 'API_BASE_URL' // Environment variable for the backend URL
              value: 'https://${reservationManagerApp.properties.configuration.ingress.fqdn}' // Inject backend FQDN
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

