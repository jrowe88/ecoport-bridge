// Key Vault (Standard, RBAC mode) for per-device HMAC secrets
// (secret name `device-{deviceId}`).

@maxLength(24)
param name string

param location string
param tags object = {}

@minValue(7)
@maxValue(90)
param softDeleteRetentionInDays int = 7

resource vault 'Microsoft.KeyVault/vaults@2026-05-15' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: softDeleteRetentionInDays
    // Purge protection is left off for dev. The API rejects an explicit
    // `false`, so the property is omitted rather than set.
    enabledForDeployment: false
    enabledForTemplateDeployment: false
    enabledForDiskEncryption: false
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

output name string = vault.name
output id string = vault.id
output uri string = vault.properties.vaultUri
