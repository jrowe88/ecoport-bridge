// RBAC for the Function App identity and the admin user.

param storageAccountName string
param keyVaultName string
param functionAppPrincipalId string

@description('Object id of the admin (the owner). Empty skips the admin assignments.')
param adminPrincipalId string = ''

// Built-in role definition ids.
var roles = {
  storageTableDataContributor: '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
  storageBlobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  storageQueueDataContributor: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  keyVaultSecretsOfficer: 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
}

resource storage 'Microsoft.Storage/storageAccounts@2026-06-01' existing = {
  name: storageAccountName
}

resource vault 'Microsoft.KeyVault/vaults@2026-05-15' existing = {
  name: keyVaultName
}

// Function App: tables (Device API data), blob owner (Functions host
// storage + the Flex deployment container), queues (host internals; the
// `process-device` queue later), Key Vault secret reads.
var functionStorageRoles = [
  roles.storageTableDataContributor
  roles.storageBlobDataOwner
  roles.storageQueueDataContributor
]

resource functionStorageAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for roleId in functionStorageRoles: {
    name: guid(storage.id, functionAppPrincipalId, roleId)
    scope: storage
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleId)
      principalId: functionAppPrincipalId
      principalType: 'ServicePrincipal'
    }
  }
]

resource functionVaultAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, functionAppPrincipalId, roles.keyVaultSecretsUser)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Admin: write device secrets and register devices (provision-device.ps1).
resource adminVaultAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(adminPrincipalId)) {
  name: guid(vault.id, adminPrincipalId, roles.keyVaultSecretsOfficer)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsOfficer)
    principalId: adminPrincipalId
    principalType: 'User'
  }
}

resource adminTableAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(adminPrincipalId)) {
  name: guid(storage.id, adminPrincipalId, roles.storageTableDataContributor)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageTableDataContributor)
    principalId: adminPrincipalId
    principalType: 'User'
  }
}
