param serviceBusId string
param signalRName string
param keyVaultName string // Added Key Vault name as a parameter

// Define standardized secret names
var serviceBusSecretName = 'ServiceBusConnectionString'
var signalRSecretName = 'SignalRConnectionString'

// Retrieve existing Key Vault resource (it's created in main.bicep)
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// Service Bus connection string secret in Key Vault
resource serviceBusConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: serviceBusSecretName
  properties: {
    value: listKeys('${serviceBusId}/AuthorizationRules/RootManageSharedAccessKey', '2022-10-01-preview').primaryConnectionString
  }
}

// SignalR connection string secret in Key Vault
resource signalRConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: signalRSecretName
  properties: {
    // Corrected to use the full resource ID for listKeys on SignalR if signalRName is just the name
    // Assuming signalRName is just the name, and it's in the same resource group
    value: listKeys('${resourceGroup().id}/providers/Microsoft.SignalRService/signalR/${signalRName}', '2023-02-01').primaryConnectionString
  }
}

// Outputs can be the names of the secrets if needed, though main.bicep will use the var names directly
@description('Name of the Service Bus connection string secret in Key Vault')
output sbSecretName string = serviceBusSecretName

@description('Name of the SignalR connection string secret in Key Vault')
output signalRSecretName string = signalRSecretName
