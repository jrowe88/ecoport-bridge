// Device API: Flex Consumption (FC1) plan + Linux Function App,
// .NET 10 isolated, system-assigned identity for Storage and Key Vault.

param planName string
param appName string
param location string
param tags object = {}

param storageAccountName string
param deploymentContainerName string
param keyVaultUri string
param appInsightsName string

@description('Instance memory in MB. Allowed by FC1: 512, 2048, 4096.')
@allowed([
  512
  2048
  4096
])
param instanceMemoryMB int = 512

@description('Ceiling on on-demand instances. Microsoft documents 1 as the current lowest allowed value.')
@minValue(1)
@maxValue(1000)
param maximumInstanceCount int = 1

resource storage 'Microsoft.Storage/storageAccounts@2026-06-01' existing = {
  name: storageAccountName
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource plan 'Microsoft.Web/serverfarms@2025-03-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'functionapp'
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
    zoneRedundant: false
  }
}

resource app 'Microsoft.Web/sites@2025-03-01' = {
  name: appName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    publicNetworkAccess: 'Enabled'
    siteConfig: {
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      http20Enabled: true
      appSettings: [
        // Functions host storage over managed identity (no keys).
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storage.name
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        // Read by EcoPort.Cloud.Device as Storage:TableEndpoint / KeyVault:Uri.
        {
          name: 'Storage__TableEndpoint'
          value: storage.properties.primaryEndpoints.table
        }
        {
          name: 'KeyVault__Uri'
          value: keyVaultUri
        }
      ]
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      scaleAndConcurrency: {
        instanceMemoryMB: instanceMemoryMB
        maximumInstanceCount: maximumInstanceCount
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
  }
}

// FTPS state is not a valid site property on Flex Consumption, so FTP is
// turned off by disallowing basic-auth publishing over FTP. SCM basic auth
// is also off; `az functionapp deployment source config-zip` uses Entra ID.
resource ftpPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2025-03-01' = {
  parent: app
  name: 'ftp'
  properties: {
    allow: false
  }
}

resource scmPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2025-03-01' = {
  parent: app
  name: 'scm'
  properties: {
    allow: false
  }
}

output name string = app.name
output id string = app.id
output defaultHostName string = app.properties.defaultHostName
output principalId string = app.identity.principalId
