#Requires -Version 7.0
<#
.SYNOPSIS
    Deploys the EcoPort Azure infrastructure (infra/bicep/main.bicep) to a resource group.

.DESCRIPTION
    Aborts unless the Azure CLI's current subscription is -SubscriptionId (the
    owner's personal subscription). It never switches subscriptions.

    Creates the resource group if missing, runs `az deployment group what-if`,
    then `az deployment group create` with infra/bicep/<Env>.bicepparam.
    Use -WhatIfOnly to stop after the what-if.

.EXAMPLE
    ./infra/deploy-infra.ps1 -Env dev -ResourceGroup rg-ecoport-dev -Location centralus -WhatIfOnly
#>
[CmdletBinding()]
param(
    [ValidateSet('dev')]
    [string] $Env = 'dev',

    [string] $ResourceGroup = 'rg-ecoport-dev',

    # Region for the resource group and everything except the Static Web App.
    # Must support Flex Consumption: az functionapp list-flexconsumption-locations
    [string] $Location = 'centralus',

    [string] $SubscriptionId = '54c73415-2ebc-4270-8390-46985acfa7a7',

    # Entra object id that gets Key Vault Secrets Officer + Storage Table Data Contributor.
    # Defaults to the signed-in user.
    [string] $AdminPrincipalId,

    # Budget alert e-mail. Defaults to the signed-in user's name.
    [string] $BudgetEmail,

    [switch] $WhatIfOnly
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

$paramFile = Join-Path $PSScriptRoot "bicep/$Env.bicepparam"
if (-not (Test-Path $paramFile)) {
    Write-Host "Parameter file not found: $paramFile" -ForegroundColor Red
    exit 1
}
$deploymentName = "ecoport-$Env"
$budgetName = "budget-ecoport-$Env"

if (-not $AdminPrincipalId) {
    $AdminPrincipalId = (az ad signed-in-user show --query id -o tsv)
    Assert-LastExit 'Reading the signed-in user object id'
    $AdminPrincipalId = $AdminPrincipalId.Trim()
}
if (-not $BudgetEmail) {
    $BudgetEmail = (az account show --query user.name -o tsv)
    Assert-LastExit 'Reading the signed-in user name'
    $BudgetEmail = $BudgetEmail.Trim()
}
if ($BudgetEmail -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$') {
    Write-Host "Budget e-mail '$BudgetEmail' is not an e-mail address; pass -BudgetEmail." -ForegroundColor Red
    exit 1
}

# Resource group.
$rgExists = (az group exists --name $ResourceGroup)
Assert-LastExit 'az group exists'
if ($rgExists.Trim() -ne 'true') {
    Write-Host "Creating resource group $ResourceGroup in $Location"
    az group create --name $ResourceGroup --location $Location --tags app=ecoport env=$Env -o none
    Assert-LastExit 'az group create'
}

# A budget's start date cannot change after creation, so reuse it if the budget exists.
$budgetId = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Consumption/budgets/$budgetName"
$budgetStart = (az rest --method get --url "https://management.azure.com${budgetId}?api-version=2026-06-01" --query properties.timePeriod.startDate -o tsv 2>$null)
if ($LASTEXITCODE -eq 0 -and $budgetStart) {
    $budgetStart = ([datetimeoffset]::Parse($budgetStart.Trim())).UtcDateTime.ToString('yyyy-MM-dd')
} else {
    $budgetStart = (Get-Date).ToUniversalTime().ToString('yyyy-MM-01')
}

# dev.bicepparam reads these with readEnvironmentVariable().
$env:ECOPORT_ADMIN_PRINCIPAL_ID = $AdminPrincipalId
$env:ECOPORT_BUDGET_EMAIL = $BudgetEmail
$env:ECOPORT_BUDGET_START_DATE = $budgetStart

Write-Host "Subscription:   $SubscriptionId"
Write-Host "Resource group: $ResourceGroup ($Location)"
Write-Host "Admin object:   $AdminPrincipalId"
Write-Host "Budget:         USD 5/month from $budgetStart, alerts to $BudgetEmail"

az deployment group what-if `
    --resource-group $ResourceGroup `
    --name $deploymentName `
    --parameters $paramFile
Assert-LastExit 'az deployment group what-if'

if ($WhatIfOnly) {
    Write-Host 'What-if only; nothing deployed.'
    exit 0
}

az deployment group create `
    --resource-group $ResourceGroup `
    --name $deploymentName `
    --parameters $paramFile `
    --query properties.outputs -o json
Assert-LastExit 'az deployment group create'

Write-Host 'Infrastructure deployed. Next: deploy-device-api.ps1, provision-device.ps1, deploy-web.ps1.'
