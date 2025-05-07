param serviceBusId string
param signalRName string

// Service Bus connection string
@description('Service Bus connection string')
@secure()
output sbConnectionString string = listKeys('${serviceBusId}/AuthorizationRules/RootManageSharedAccessKey', '2022-10-01-preview').primaryConnectionString

// SignalR connection string
@description('SignalR connection string')
@secure()
output signalRConnectionString string = listKeys('${resourceGroup().id}/providers/Microsoft.SignalRService/signalR/${signalRName}', '2023-02-01').primaryConnectionString
