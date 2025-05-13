param containerRegistry string
param containerRegistryUsername string
@secure()
param containerRegistryPassword string
param location string = resourceGroup().location
param keyVaultName string = 'sagaway-keyvault-demo' // Unique name for Key Vault
var port = 8080 
var redisAppName = 'redis-app'
var billingQueueName = 'billing-queue'
var bookingQueueName = 'booking-queue'
var inventoryQueueName = 'inventory-queue'
var reservationResponseQueueName = 'reservation-response-queue'

var reservationUiAppName = 'reservation-ui'
var reservationUiImage = '${containerRegistry}/sagaway.demo.reservation.ui-new:latest' 

var serviceBusConnectionString = listKeys('${serviceBus.id}/AuthorizationRules/RootManageSharedAccessKey', '2022-10-01-preview').primaryConnectionString
var signalRConnectionString = listKeys('${resourceGroup().id}/providers/Microsoft.SignalRService/signalR/${signalR.name}', '2023-02-01').primaryConnectionString

var reservationManagerAppName = 'reservation-manager'
var reservationManagerImage = '${containerRegistry}/sagaway.demo.reservation.manager:latest'

var logAnalyticsWorkspaceName = 'la-sagaway-${uniqueString(resourceGroup().id)}' // Customize as needed
var applicationInsightsName = 'appi-sagaway-${uniqueString(resourceGroup().id)}' // Customize as needed

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
  
}

resource keyVault 'Microsoft.KeyVault/vaults@2022-11-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    accessPolicies: [] // We'll add policies for the managed identities
    enabledForDeployment: true
    enabledForTemplateDeployment: true
    enableSoftDelete: true
    enablePurgeProtection: true
  }
}

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

// Create connection string secrets in Key Vault
resource sbConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2022-11-01' = {
  parent: keyVault
  name: 'sb-connection-string'
  properties: {
    value: serviceBusConnectionString
  }
}

resource signalRConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2022-11-01' = {
  parent: keyVault
  name: 'signalr-connection-string'
  properties: {
    value: signalRConnectionString
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
        value: serviceBusConnectionString
      }
      {
        name: 'queueName'
        value: billingQueueName
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
        value: serviceBusConnectionString
      }
      {
        name: 'queueName'
        value: bookingQueueName
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
        value: serviceBusConnectionString
      }
      {
        name: 'queueName'
        value: inventoryQueueName
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
        value: serviceBusConnectionString
      }
      {
        name: 'queueName'
        value: reservationResponseQueueName
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
        value: signalRConnectionString
      }
      {
        name: 'hub'
        value: 'reservationcallback' // Matches the hub name used in the code and local YAML
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

//----------------------------- Container App -----------------------------
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
          name: 'signalr-connection-string'
          #disable-next-line use-secure-value-for-secure-inputs
          value: signalRConnectionString
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
        allowInsecure: true // Allow HTTP traffic without redirecting to HTTPS
        corsPolicy: {
          allowedOrigins: [
            // Allow HTTP origin for the UI app
            'http://${reservationUiAppName}.${containerEnv.properties.defaultDomain}'
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
              secretRef: 'signalr-connection-string'
            }
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8080'
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: applicationInsights.properties.ConnectionString
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
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: applicationInsights.properties.ConnectionString
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
              value: 'http://${reservationManagerAppName}.${containerEnv.properties.defaultDomain}'
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



