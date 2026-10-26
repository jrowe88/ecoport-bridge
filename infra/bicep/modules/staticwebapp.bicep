// Static Web App (Free): Blazor WASM dashboard + managed-functions User API.
// Managed functions cannot use a managed identity, so the User API gets a
// storage connection string as an app setting.

param name string

@description('SWA is only offered in a few regions.')
@allowed([
  'centralus'
  'eastus2'
  'westus2'
  'westeurope'
  'eastasia'
])
param location string

param tags object = {}
param storageAccountName string
param appInsightsName string

resource storage 'Microsoft.Storage/storageAccounts@2026-06-01' existing = {
  name: storageAccountName
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource swa 'Microsoft.Web/staticSites@2025-03-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    // Deployed from a local build with `swa deploy` (infra/deploy-web.ps1),
    // not linked to a repository.
    allowConfigFileUpdates: true
    stagingEnvironmentPolicy: 'Disabled'
  }
}

resource appSettings 'Microsoft.Web/staticSites/config@2025-03-01' = {
  parent: swa
  name: 'appsettings'
  properties: {
    // Read by EcoPort.Cloud.Api as Storage:ConnectionString.
    Storage__ConnectionString: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
    APPLICATIONINSIGHTS_CONNECTION_STRING: appInsights.properties.ConnectionString
  }
}

output name string = swa.name
output defaultHostName string = swa.properties.defaultHostname
