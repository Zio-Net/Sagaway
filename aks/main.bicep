param location string = 'westus2'
param clusterName string = 'SagawayAKSCluster'
param acrName string = 'SagawayDemoACR'
param nodeCount int = 2
param nodeVMSize string = 'Standard_B2s'

// Create Azure Container Registry
resource acr 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
  }
}

// Create AKS Cluster (no SSH, clean)
resource aks 'Microsoft.ContainerService/managedClusters@2023-01-01' = {
  name: clusterName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    dnsPrefix: clusterName
    agentPoolProfiles: [
      {
        name: 'nodepool1'
        count: nodeCount
        vmSize: nodeVMSize
        osType: 'Linux'
        mode: 'System'
      }
    ]
    networkProfile: {
      networkPlugin: 'azure'
    }
    enableRBAC: true
  }
}
