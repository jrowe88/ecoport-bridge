// EcoPort cloud, M1 (bench reporting). Resource-group scoped.
// Deploy only with infra/deploy-infra.ps1 (it enforces the subscription guard).

targetScope = 'resourceGroup'

@description('Environment name, used in resource names.')
@allowed([
  'dev'
])
param env string = 'dev'

@description('Region for everything except the Static Web App. Must support Flex Consumption.')
param location string = resourceGroup().location

@description('Static Web App region (SWA is only offered in a few regions).')
@allowed([
  'centralus'
  'eastus2'
  'westus2'
  'westeurope'
  'eastasia'
])
param swaLocation string = 'centralus'

@description('Entra object id of the owner; gets Key Vault Secrets Officer and Storage Table Data Contributor.')
param adminPrincipalId string

@description('E-mail address for budget alerts.')
param budgetEmail string

@description('Budget start date: first day of a month, yyyy-MM-dd. Keep the original value on redeploys. Empty = first of the current month.')
param budgetStartDate string = ''

@description('Do not set. Fallback for budgetStartDate (utcNow is only allowed in parameter defaults).')
param currentMonthStart string = utcNow('yyyy-MM-01')

@description('Monthly budget in USD.')
param budgetAmount int = 5

var suffix = take(uniqueString(subscription().id, resourceGroup().id, env), 6)
var tags = {
  app: 'ecoport'
  env: env
}

var names = {
  storage: take('stecoport${env}${suffix}', 24)
  logAnalytics: 'log-ecoport-${env}'
  appInsights: 'appi-ecoport-${env}'
  keyVault: 'kv-ecoport-${env}-${suffix}'
  plan: 'asp-ecoport-${env}'
  functionApp: 'func-ecoport-${env}-${suffix}'
  staticWebApp: 'swa-ecoport-${env}-${suffix}'
  budget: 'budget-ecoport-${env}'
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    name: names.storage
    location: location
    tags: tags
  }
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    logAnalyticsName: names.logAnalytics
    appInsightsName: names.appInsights
    location: location
    tags: tags
    dailyQuotaGb: '0.1'
    retentionInDays: 30
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    name: names.keyVault
    location: location
    tags: tags
  }
}

module functionApp 'modules/functionapp.bicep' = {
  name: 'functionapp'
  params: {
    planName: names.plan
    appName: names.functionApp
    location: location
    tags: tags
    storageAccountName: storage.outputs.name
    deploymentContainerName: storage.outputs.deploymentContainerName
    keyVaultUri: keyVault.outputs.uri
    appInsightsName: monitoring.outputs.appInsightsName
    instanceMemoryMB: 512
    maximumInstanceCount: 1
  }
}

module staticWebApp 'modules/staticwebapp.bicep' = {
  name: 'staticwebapp'
  params: {
    name: names.staticWebApp
    location: swaLocation
    tags: tags
    storageAccountName: storage.outputs.name
    appInsightsName: monitoring.outputs.appInsightsName
  }
}

module roles 'modules/roles.bicep' = {
  name: 'roles'
  params: {
    storageAccountName: storage.outputs.name
    keyVaultName: keyVault.outputs.name
    functionAppPrincipalId: functionApp.outputs.principalId
    adminPrincipalId: adminPrincipalId
  }
}

module budget 'modules/budget.bicep' = {
  name: 'budget'
  params: {
    name: names.budget
    amount: budgetAmount
    startDate: empty(budgetStartDate) ? currentMonthStart : budgetStartDate
    contactEmails: [
      budgetEmail
    ]
  }
}

output functionAppName string = functionApp.outputs.name
output functionAppHostName string = functionApp.outputs.defaultHostName
output staticWebAppName string = staticWebApp.outputs.name
output staticWebAppHostName string = staticWebApp.outputs.defaultHostName
output keyVaultName string = keyVault.outputs.name
output storageAccountName string = storage.outputs.name
