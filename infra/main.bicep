param containerRegistry string
param containerRegistryUsername string
@secure()
param containerRegistryPassword string
param location string = resourceGroup().location
var port = 8080 
var redisAppName = 'redis-app'
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

//---------------------------- Queues ----------------------------
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
//---------------------------- Redis Cache ----------------------------
resource redisContainerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: redisAppName
  location: location
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      // No external ingress needed, internal only
      ingress: {
        external: false
        targetPort: 6379 // Default Redis port
        transport: 'tcp' // Redis uses TCP
      }

    }
    template: {
      containers: [
        {
          name: redisAppName
          // Use redis-stack-server image to include Redis Search for queryIndexes
          image: 'redis/redis-stack-server:latest' 
          resources: {
            cpu: json('0.25') // Define resource requests/limits as needed
            memory: '0.5Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1 // Scale as needed, consider persistence implications
      }
    }
  }
}

//---------------------------- SignalR Service ----------------------------
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

// Secure outputs to use in place of direct listKeys() calls
@description('Service Bus connection string')
@secure()
output sbConnectionString string = listKeys('${serviceBus.id}/AuthorizationRules/RootManageSharedAccessKey', '2022-10-01-preview').primaryConnectionString

@description('SignalR connection string')
@secure()
output signalRConnectionString string = signalR.listKeys().primaryConnectionString

// Use module to get secure values in this deployment
module secretsModule 'secretsModule.bicep' = {
  name: 'secretsModule'
  params: {
    serviceBusId: serviceBus.id
    signalRName: signalR.name
  }
}
 
//---------------------- Dapr Bindings - Service Bus Queues --------------------------------
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
        value: secretsModule.outputs.sbConnectionString
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
        value: secretsModule.outputs.sbConnectionString
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
        value: secretsModule.outputs.sbConnectionString
      }
    ]
    scopes: ['inventory-management', 'reservation-manager']
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
        value: secretsModule.outputs.sbConnectionString
      }
    ]
    scopes: union(backendAppNames, ['reservation-manager']) // Use variable in union
  }
}

// Dapr Binding - SignalR
resource reservationCallbackBinding 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: containerEnv
  name: 'reservationcallback' // Matches the component name used in the code and local YAML
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
        value: secretsModule.outputs.signalRConnectionString
      }
    ]
    scopes: ['reservation-manager'] // Only scope to the app that needs it
  }
}

// Dapr Actor State Store - Redis Container App
resource actorstatestore 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: containerEnv
  name: 'actorstatestore' // Matches local component name
  properties: {
    componentType: 'state.redis'
    version: 'v1'
    metadata: [
      {
        name: 'redisHost'
        // Point to the internal service name and port of the Redis container app
        value: '${redisAppName}:6379' 
      }
      {
        name: 'actorStateStore'
        value: 'true'
      }
    ]
    scopes: union(backendAppNames, ['reservation-manager']) // Apply to relevant apps
  }
  dependsOn: [ // Explicit dependency on the redis container app
    redisContainerApp
  ]
}

// Dapr State Store - Redis Container App
resource statestore 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: containerEnv
  name: 'statestore' // Matches local component name
  properties: {
    componentType: 'state.redis'
    version: 'v1'
    metadata: [
      {
        name: 'redisHost'
        // Point to the internal service name and port of the Redis container app
        value: '${redisAppName}:6379' 
      }
      {
        name: 'queryIndexes' 
        value: '[ { "name": "customerNameIndex", "indexes": [ { "key": "customerName", "type": "TEXT" } ] } ]'
      }
    ]
    scopes: union(backendAppNames, ['reservation-manager']) // Apply to relevant apps
  }
  dependsOn: [ // Explicit dependency on the redis container app
    redisContainerApp
  ]
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
      secrets: [
        {
          name: 'registry-password'
          value: containerRegistryPassword
        }
        {
          name: 'signalr-connection-string-secret'
          value: secretsModule.outputs.signalRConnectionString
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
        appId: reservationManagerAppName
        appPort: port
      }
      ingress: {
        external: true
        targetPort: port 
        transport: 'auto' 
        allowInsecure: true 
        corsPolicy: {
          allowedOrigins: [
            // Allow HTTP origin for the UI app
            'https://${reservationUiAppName}.${containerEnv.properties.defaultDomain}'
          ]
          allowedMethods: [
            'GET'
            'POST'
            'OPTIONS'
          ]
          allowedHeaders: [
            '*'
          ]
          allowCredentials: true
        }
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
        appPort: port 
      }
      ingress: {
        external: true
        targetPort: port 
        transport: 'auto'
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
var reservationUiImage = '${containerRegistry}/sagaway.demo.reservation.ui-new:latest' // Assumed image name

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
              name: 'RESERVATION_MANAGER_URL'
              value: 'https://${reservationManagerAppName}.${containerEnv.properties.defaultDomain}'
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

 