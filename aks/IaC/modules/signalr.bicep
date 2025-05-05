param signalrName string
param location string

@minValue(1)
param capacity int = 1

resource signalr 'Microsoft.SignalRService/signalR@2023-08-01-preview' = {
  name: signalrName
  location: location
  sku: {
    name: 'Free_F1'
    tier: 'Free'
    capacity: 1
  }
  kind: 'SignalR'
  properties: {
    features: [
      {
        flag: 'ServiceMode'
        value: 'Default'
      }
    ]
    tls: {
      clientCertEnabled: false
    }
  }
}