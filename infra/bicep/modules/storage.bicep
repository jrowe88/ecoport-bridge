// Storage account for the EcoPort tables, the Functions host and the
// Flex Consumption deployment container.

@description('Storage account name (3-24 lowercase letters and digits).')
@minLength(3)
@maxLength(24)
param name string

param location string
param tags object = {}

@description('Blob container that holds the Function App deployment package.')
param deploymentContainerName string = 'deployments'

@description('Tables created up front (Device API also creates them on first use).')
param tableNames array = [
  'Devices'
  'Telemetry'
  'DeviceEvents'
  'CurrentState'
]

resource storage 'Microsoft.Storage/storageAccounts@2026-06-01' = {
  name: name
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    // Shared-key access stays on only because SWA managed functions (the User
    // API) cannot use a managed identity and need a connection string.
    allowSharedKeyAccess: true
    defaultToOAuthAuthentication: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2026-06-01' = {
  parent: storage
  name: 'default'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2026-06-01' = {
  parent: blobService
  name: deploymentContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2026-06-01' = {
  parent: storage
  name: 'default'
}

resource tables 'Microsoft.Storage/storageAccounts/tableServices/tables@2026-06-01' = [
  for tableName in tableNames: {
    parent: tableService
    name: tableName
  }
]

output name string = storage.name
output id string = storage.id
output blobEndpoint string = storage.properties.primaryEndpoints.blob
output tableEndpoint string = storage.properties.primaryEndpoints.table
output deploymentContainerName string = deploymentContainer.name
