param serviceBusId string
param signalRName string
param keyVaultName string

resource keyVault 'Microsoft.KeyVault/vaults@2022-11-01' existing = {
  name: keyVaultName
}

// Get Service Bus connection string
var sbConnectionString = listKeys('${serviceBusId}/AuthorizationRules/RootManageSharedAccessKey', '2022-10-01-preview').primaryConnectionString

// Get SignalR connection string
var signalRConnectionString = listKeys('${resourceGroup().id}/providers/Microsoft.SignalRService/signalR/${signalRName}', '2023-02-01').primaryConnectionString

// Write secrets into Key Vault
resource sbSecret 'Microsoft.KeyVault/vaults/secrets@2022-11-01' = {
  parent: keyVault
  name: 'sb-connection-string'
  properties: {
    value: sbConnectionString
  }
}

resource signalRSecret 'Microsoft.KeyVault/vaults/secrets@2022-11-01' = {
  parent: keyVault
  name: 'signalr-connection-string'
  properties: {
    value: signalRConnectionString
  }
}
