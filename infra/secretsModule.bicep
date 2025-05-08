param serviceBusId string
param signalRName string
param keyVaultName string

// Get the Service Bus connection string
var sbConnectionString = listKeys('${serviceBusId}/AuthorizationRules/RootManageSharedAccessKey', '2022-10-01-preview').primaryConnectionString

// Get the SignalR connection string
var signalRConnectionString = listKeys('${resourceGroup().id}/providers/Microsoft.SignalRService/signalR/${signalRName}', '2023-02-01').primaryConnectionString

// Store connection strings in Key Vault
resource sbConnStringSecret 'Microsoft.KeyVault/vaults/secrets@2022-11-01' = {
  name: '${keyVaultName}/sb-connection-string'
  properties: {
    value: sbConnectionString
  }
}

resource signalRConnStringSecret 'Microsoft.KeyVault/vaults/secrets@2022-11-01' = {
  name: '${keyVaultName}/signalr-connection-string'
  properties: {
    value: signalRConnectionString
  }
}

// Return the secret URIs instead of the actual secrets
output sbConnectionStringReference string = sbConnStringSecret.properties.secretUri
output signalRConnectionStringReference string = signalRConnStringSecret.properties.secretUri
