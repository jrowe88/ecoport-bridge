#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the Blazor dashboard and the User API and deploys both to the Static Web App.

.DESCRIPTION
    Aborts unless the Azure CLI's current subscription is -SubscriptionId.
    Uses the SWA CLI (`swa`, or `npx @azure/static-web-apps-cli` if `swa` is
    not installed). The deployment token is read with
    `az staticwebapp secrets list` and handed to the SWA CLI through the
    SWA_CLI_DEPLOYMENT_TOKEN environment variable, never written to disk.

    -ApiVersion must match `platform.apiRuntime` in
    web/EcoPort.Web/wwwroot/staticwebapp.config.json (see cloud/README.md).
#>
[CmdletBinding()]
param(
    [ValidateSet('dev')]
    [string] $Env = 'dev',

    [string] $ResourceGroup = 'rg-ecoport-dev',

    [string] $SubscriptionId = '54c73415-2ebc-4270-8390-46985acfa7a7',

    # Managed-functions runtime version for dotnet-isolated.
    [string] $ApiVersion = '10.0'
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
$webProject = Join-Path $repoRoot 'web/EcoPort.Web/EcoPort.Web.csproj'
$apiProject = Join-Path $repoRoot 'cloud/src/EcoPort.Cloud.Api/EcoPort.Cloud.Api.csproj'
foreach ($p in @($webProject, $apiProject)) {
    if (-not (Test-Path $p)) {
        Write-Host "Project not found: $p" -ForegroundColor Red
        exit 1
    }
}

$swaName = (az deployment group show --resource-group $ResourceGroup --name "ecoport-$Env" `
        --query properties.outputs.staticWebAppName.value -o tsv)
Assert-LastExit 'Reading deployment outputs (run deploy-infra.ps1 first)'
$swaName = $swaName.Trim()

$swa = Get-Command swa -ErrorAction SilentlyContinue
if ($swa) {
    $swaCommand = @('swa')
} else {
    $swaCommand = @('npx', '--yes', '@azure/static-web-apps-cli')
}

$work = Join-Path ([IO.Path]::GetTempPath()) "ecoport-web-$([guid]::NewGuid().ToString('N'))"
$webOut = Join-Path $work 'web'
$apiOut = Join-Path $work 'api'

try {
    dotnet publish $webProject -c Release -o $webOut
    Assert-LastExit 'dotnet publish (web)'
    dotnet publish $apiProject -c Release -o $apiOut
    Assert-LastExit 'dotnet publish (api)'

    $token = (az staticwebapp secrets list --name $swaName --resource-group $ResourceGroup --query properties.apiKey -o tsv)
    Assert-LastExit 'az staticwebapp secrets list'
    $env:SWA_CLI_DEPLOYMENT_TOKEN = $token.Trim()

    Write-Host "Deploying to $swaName"
    $swaArgs = @(
        'deploy', (Join-Path $webOut 'wwwroot'),
        '--api-location', $apiOut,
        '--api-language', 'dotnetisolated',
        '--api-version', $ApiVersion,
        '--env', 'production'
    )
    $swaExe = $swaCommand[0]
    $allArgs = @($swaCommand | Select-Object -Skip 1) + $swaArgs
    & $swaExe @allArgs
    Assert-LastExit 'swa deploy'
}
finally {
    Remove-Item Env:SWA_CLI_DEPLOYMENT_TOKEN -ErrorAction SilentlyContinue
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
}

$hostName = (az staticwebapp show --name $swaName --resource-group $ResourceGroup --query defaultHostname -o tsv)
Assert-LastExit 'az staticwebapp show'
Write-Host "Dashboard deployed: https://$($hostName.Trim())"
