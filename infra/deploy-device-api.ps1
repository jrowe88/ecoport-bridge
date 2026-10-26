#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the Device API (EcoPort.Cloud.Device) and zip-deploys it to the Flex Consumption Function App.

.DESCRIPTION
    Aborts unless the Azure CLI's current subscription is -SubscriptionId.
    The Function App name comes from the outputs of the `ecoport-<Env>`
    deployment created by deploy-infra.ps1.
#>
[CmdletBinding()]
param(
    [ValidateSet('dev')]
    [string] $Env = 'dev',

    [string] $ResourceGroup = 'rg-ecoport-dev',

    [string] $SubscriptionId = '54c73415-2ebc-4270-8390-46985acfa7a7'
)

$ErrorActionPreference = 'Stop'

# EcoPort uses its own Azure CLI profile (~/.azure-ecoport) so the personal subscription never
# shares a token cache with other logins on this machine; see infra/README.md. Set
# AZURE_CONFIG_DIR yourself to override.
if (-not $env:AZURE_CONFIG_DIR) { $env:AZURE_CONFIG_DIR = Join-Path $HOME '.azure-ecoport' }

# --- Subscription guard -------------------------------------------------------
$currentSubscription = (az account show --query id -o tsv 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($currentSubscription)) {
    Write-Host 'Could not read the current Azure subscription. Run `az login` first.' -ForegroundColor Red
    exit 1
}
$currentSubscription = $currentSubscription.Trim()
if ($currentSubscription -ne $SubscriptionId) {
    Write-Host 'Subscription mismatch; refusing to deploy.' -ForegroundColor Red
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

$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot 'cloud/src/EcoPort.Cloud.Device/EcoPort.Cloud.Device.csproj'
if (-not (Test-Path $project)) {
    Write-Host "Project not found: $project" -ForegroundColor Red
    exit 1
}

$functionAppName = (az deployment group show --resource-group $ResourceGroup --name "ecoport-$Env" `
        --query properties.outputs.functionAppName.value -o tsv)
Assert-LastExit 'Reading deployment outputs (run deploy-infra.ps1 first)'
$functionAppName = $functionAppName.Trim()

$work = Join-Path ([IO.Path]::GetTempPath()) "ecoport-device-api-$([guid]::NewGuid().ToString('N'))"
$publishDir = Join-Path $work 'publish'
$zipPath = Join-Path $work 'device-api.zip'

try {
    dotnet publish $project -c Release -o $publishDir
    Assert-LastExit 'dotnet publish'

    # ZipFile keeps dot-folders such as .azurefunctions that the isolated worker needs.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $zipPath)

    Write-Host "Deploying to $functionAppName"
    az functionapp deployment source config-zip `
        --resource-group $ResourceGroup `
        --name $functionAppName `
        --src $zipPath `
        -o none
    Assert-LastExit 'az functionapp deployment source config-zip'
}
finally {
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
}

# `az functionapp show` leaves defaultHostName empty for Flex Consumption apps, so use the
# deployment output instead.
$hostName = (az deployment group show --resource-group $ResourceGroup --name "ecoport-$Env" `
        --query properties.outputs.functionAppHostName.value -o tsv)
Assert-LastExit 'Reading deployment outputs'
Write-Host "Device API deployed: https://$("$hostName".Trim())/device/v1/records"
