#Requires -Version 7.0
<#
.SYNOPSIS
    Registers a device: generates its 32-byte HMAC secret, stores it in Key Vault
    as `device-<DeviceId>` (base64), and inserts the `Devices` table row.

.DESCRIPTION
    Aborts unless the Azure CLI's current subscription is -SubscriptionId.
    The secret only lives in memory and in Key Vault; it is never written to
    disk. The script prints the lines to paste into the gitignored
    firmware/ucm/src/config.rs.

    Refuses to touch a device that already has a secret or a Devices row
    unless -Force is given (which rotates the secret: the device must be
    reflashed with the new config.rs).

.EXAMPLE
    ./infra/provision-device.ps1 -Env dev -DeviceId WH001
#>
[CmdletBinding()]
param(
    [ValidateSet('dev')]
    [string] $Env = 'dev',

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9-]{1,64}$')]
    [string] $DeviceId,

    [string] $ResourceGroup = 'rg-ecoport-dev',

    [string] $SubscriptionId = '54c73415-2ebc-4270-8390-46985acfa7a7',

    [switch] $Force
)

$ErrorActionPreference = 'Stop'

# --- Subscription guard -------------------------------------------------------
$currentSubscription = (az account show --query id -o tsv 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($currentSubscription)) {
    Write-Host 'Could not read the current Azure subscription. Run `az login` first.' -ForegroundColor Red
    exit 1
}
$currentSubscription = $currentSubscription.Trim()
if ($currentSubscription -ne $SubscriptionId) {
    Write-Host 'Subscription mismatch; refusing to continue.' -ForegroundColor Red
    Write-Host "  Expected: $SubscriptionId"
    Write-Host "  Current:  $currentSubscription"
    exit 1
}
# -----------------------------------------------------------------------------

function Assert-LastExit([string] $What) {
    if ($LASTEXITCODE -ne 0) {
        Write-Host "$What failed (exit $LASTEXITCODE)." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

$outputs = (az deployment group show --resource-group $ResourceGroup --name "ecoport-$Env" `
        --query properties.outputs -o json) | ConvertFrom-Json
Assert-LastExit 'Reading deployment outputs (run deploy-infra.ps1 first)'
$vaultName = $outputs.keyVaultName.value
$storageName = $outputs.storageAccountName.value
$functionHost = $outputs.functionAppHostName.value
$secretName = "device-$DeviceId"

# Existing secret?
$null = az keyvault secret show --vault-name $vaultName --name $secretName --query id -o tsv 2>$null
$secretExists = ($LASTEXITCODE -eq 0)

# Existing Devices row?
$null = az storage entity show --auth-mode login --account-name $storageName --table-name Devices `
    --partition-key DEVICE --row-key $DeviceId -o none 2>$null
$rowExists = ($LASTEXITCODE -eq 0)

if (($secretExists -or $rowExists) -and -not $Force) {
    Write-Host "Device $DeviceId already exists (secret: $secretExists, Devices row: $rowExists)." -ForegroundColor Red
    Write-Host 'Use -Force to rotate its secret and replace the row.'
    exit 1
}

# 32 random bytes, base64. Kept in memory only.
$secretB64 = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

# -o none: `secret set` would otherwise echo the value.
az keyvault secret set --vault-name $vaultName --name $secretName --value $secretB64 `
    --content-type 'application/x-ecoport-hmac-key;encoding=base64' -o none
Assert-LastExit 'az keyvault secret set'

$createdAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
$ifExists = if ($Force) { 'replace' } else { 'fail' }
az storage entity insert --auth-mode login --account-name $storageName --table-name Devices `
    --if-exists $ifExists `
    --entity PartitionKey=DEVICE RowKey=$DeviceId CreatedAt=$createdAt 'CreatedAt@odata.type=Edm.DateTime' `
    -o none
Assert-LastExit 'az storage entity insert'

Write-Host "Device $DeviceId provisioned. Paste into firmware/ucm/src/config.rs (gitignored):"
Write-Host ''
Write-Host "pub const DEVICE_ID: &str = `"$DeviceId`";"
Write-Host "pub const DEVICE_SECRET_B64: &str = `"$secretB64`";"
Write-Host "pub const API_BASE_URL: &str = `"https://$functionHost`";"
Write-Host ''
Write-Host 'The secret is not stored anywhere else locally; re-run with -Force to rotate it.'
