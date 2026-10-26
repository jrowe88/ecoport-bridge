# EcoPort infrastructure (M1)

Bicep for the Azure side of the M1 bench milestone, plus PowerShell 7 scripts
to deploy it. Everything is paid from a personal subscription, so the settings
below are chosen for the lowest cost that still works.

## Signing in (separate CLI profile)

The scripts use their own Azure CLI profile folder, `~/.azure-ecoport`, unless
`AZURE_CONFIG_DIR` is already set. That keeps the personal subscription's
token cache apart from the work tenant's logins on this machine. With the
shared default profile, `az login` for the personal Microsoft account kept
failing with "User ... does not exist in MSAL token cache".

Sign in once (or when the token expires) from PowerShell:

```powershell
$env:AZURE_CONFIG_DIR = "$HOME\.azure-ecoport"
az login --use-device-code --tenant 97529d9f-70c0-4f1c-81ff-3bedb166389f
```

Run ad-hoc `az` commands for EcoPort with the same `AZURE_CONFIG_DIR`.

## Subscription guard

Every script takes `-SubscriptionId` (default
`54c73415-2ebc-4270-8390-46985acfa7a7`, "jrowe88-iot") and starts by
comparing it with `az account show --query id -o tsv`. On a mismatch it prints
both ids and exits with code 1. The scripts never run `az account set`. If the
guard trips, switch yourself (`az account set --subscription <id>`) after
checking that it is the right one; this machine is also signed in to a work
tenant.

## What gets created

One resource group (default `rg-ecoport-dev`) containing:

| Resource | SKU / plan | Cost-relevant settings |
|---|---|---|
| Storage account `stecoportdev<suffix>` | Standard_LRS, StorageV2, Hot | TLS 1.2, no public blob access, shared-key on (needed to sign the User API's read-only table SAS; the account key itself is never handed out). Tables `Devices`, `Telemetry`, `DeviceEvents`, `CurrentState`; blob container `deployments` |
| Log Analytics `log-ecoport-dev` | PerGB2018 | Daily cap 0.1 GB, retention 30 days |
| Application Insights `appi-ecoport-dev` | Workspace-based | Retention 30 days; sampling stays on in the worker SDK; capped by the workspace cap |
| Key Vault `kv-ecoport-dev-<suffix>` | Standard | RBAC mode, soft delete 7 days, purge protection off |
| Function App plan `asp-ecoport-dev` | Flex Consumption `FC1` | Instance memory 512 MB, maximum instance count 1, no always-ready instances (so the free grant applies) |
| Function App `func-ecoport-dev-<suffix>` | Linux, `dotnet-isolated` 10.0 | System-assigned identity; HTTPS only; FTP and SCM basic-auth publishing off; deployment package in the `deployments` container read with the system identity |
| Static Web App `swa-ecoport-dev-<suffix>` | Free | In `swaLocation` (default `centralus`); app settings `Storage__ConnectionString`, `APPLICATIONINSIGHTS_CONNECTION_STRING` |
| Budget `budget-ecoport-dev` | Resource-group scope | USD 5/month; e-mail at 80 % actual and 100 % forecast |

Role assignments:

- Function App identity: Storage Table Data Contributor, Storage Blob Data
  Owner and Storage Queue Data Contributor on the storage account; Key Vault
  Secrets User on the vault.
- `adminPrincipalId` (you): Key Vault Secrets Officer and Storage Table Data
  Contributor.

Function App settings: `AzureWebJobsStorage__accountName` (identity-based host
storage), `APPLICATIONINSIGHTS_CONNECTION_STRING`, `Storage__TableEndpoint`,
`KeyVault__Uri`.

Deployment outputs (deployment name `ecoport-<env>`): `functionAppName`,
`functionAppHostName`, `staticWebAppName`, `staticWebAppHostName`,
`keyVaultName`, `storageAccountName`. The other scripts read these.

## Files

```text
infra/
  bicep/
    main.bicep            # resource-group scope; wires the modules
    dev.bicepparam        # dev values; personal values from env vars
    modules/
      storage.bicep  monitoring.bicep  keyvault.bicep  functionapp.bicep
      staticwebapp.bicep  roles.bicep  budget.bicep
  deploy-infra.ps1        # RG + what-if + create
  deploy-device-api.ps1   # publish + zip deploy EcoPort.Cloud.Device
  deploy-web.ps1          # publish EcoPort.Web + EcoPort.Cloud.Api, swa deploy
  provision-device.ps1    # device secret in Key Vault + Devices row
```

`dev.bicepparam` reads `ECOPORT_ADMIN_PRINCIPAL_ID`, `ECOPORT_BUDGET_EMAIL`
and `ECOPORT_BUDGET_START_DATE`. `deploy-infra.ps1` sets them from the
signed-in user (or `-AdminPrincipalId` / `-BudgetEmail`), and reuses the start
date of an existing budget because a budget's start date cannot be changed.

## Order of operations

Prerequisites: Azure CLI (logged in to the personal subscription), PowerShell
7, .NET 10 SDK, Node.js (for the SWA CLI via `npx`, or `npm i -g
@azure/static-web-apps-cli`).

1. **Infrastructure** (creates billable resources; confirm first):

   ```powershell
   ./infra/deploy-infra.ps1 -Env dev -ResourceGroup rg-ecoport-dev -Location centralus -WhatIfOnly
   ./infra/deploy-infra.ps1 -Env dev -ResourceGroup rg-ecoport-dev -Location centralus
   ```

   `-Location` must support Flex Consumption
   (`az functionapp list-flexconsumption-locations -o table`).

2. **Device API**: `./infra/deploy-device-api.ps1 -Env dev`

3. **Register the device**: `./infra/provision-device.ps1 -Env dev -DeviceId WH001`.
   It prints three lines for `firmware/ucm/src/config.rs`:

   ```rust
   pub const DEVICE_ID: &str = "WH001";
   pub const DEVICE_SECRET_B64: &str = "<44 base64 chars>";
   pub const API_BASE_URL: &str = "https://func-ecoport-dev-xxxxxx.azurewebsites.net";
   ```

   The secret is only in memory and in Key Vault (`device-WH001`, base64 of 32
   bytes). Re-running for an existing device fails unless `-Force`, which
   rotates the secret. Role assignments from step 1 can take a few minutes to
   apply; if this step gets 403, wait and retry.

4. **Dashboard + User API**: `./infra/deploy-web.ps1 -Env dev`.
   `-ApiVersion` (default `10.0`; SWA lists `dotnet-isolated:10.0`, see cloud/README.md) must match `platform.apiRuntime` in
   `web/EcoPort.Web/wwwroot/staticwebapp.config.json`.

5. **Invite yourself to role `bench`** (every route except the login page
   requires it):

   ```powershell
   $swa = az deployment group show -g rg-ecoport-dev -n ecoport-dev --query properties.outputs.staticWebAppName.value -o tsv
   $hostName = az staticwebapp show -n $swa -g rg-ecoport-dev --query defaultHostname -o tsv
   az staticwebapp users invite -n $swa -g rg-ecoport-dev `
       --authentication-provider GitHub --user-details <github-username> `
       --roles bench --domain $hostName --invitation-expiration-in-hours 24
   ```

   Open the returned invitation URL and sign in with that account. Use
   `--authentication-provider AAD --user-details <email>` for a Microsoft
   account.

## Offline checks

```powershell
az bicep build --file infra/bicep/main.bicep
az bicep lint --file infra/bicep/main.bicep
az bicep build-params --file infra/bicep/dev.bicepparam
```

All three must give no errors and no warnings. CI runs them in
`.github/workflows/infra.yml`, plus a PowerShell parse of the scripts.

## Sources

Checked on 2026-10-25:

- Flex Consumption plan: instance sizes 512 / 2048 / 4096 MB, "The lowest
  maximum scale is currently `1`", .NET 10 isolated supported, always-ready
  default 0 —
  <https://learn.microsoft.com/azure/azure-functions/flex-consumption-plan>
- Flex Consumption how-to: deployment storage authentication types
  (`SystemAssignedIdentity` needs Storage Blob Data Contributor or higher on
  the account), region list via `az functionapp list-flexconsumption-locations` —
  <https://learn.microsoft.com/azure/azure-functions/flex-consumption-how-to>
- Flex Consumption deprecated settings (`FUNCTIONS_WORKER_RUNTIME`,
  `FUNCTIONS_EXTENSION_VERSION`, `ftpsState`, `alwaysOn`, …) —
  <https://learn.microsoft.com/azure/azure-functions/functions-app-settings#flex-consumption-plan-deprecations>
- Azure-Samples Flex Consumption Bicep (FC1 plan, `functionAppConfig` shape,
  `dotnet-isolated` 10.0, identity-based `AzureWebJobsStorage`) —
  <https://github.com/Azure-Samples/azure-functions-flex-consumption-samples/blob/main/IaC/bicep/main.bicep>,
  <https://github.com/Azure-Samples/functions-quickstart-dotnet-azd/blob/main/infra/main.bicep>
- Log Analytics daily cap (`workspaceCapping.dailyQuotaGb`; for
  workspace-based App Insights the lower of the two caps applies) —
  <https://learn.microsoft.com/azure/azure-monitor/logs/daily-cap>
- Log Analytics resource reference (API versions) —
  <https://learn.microsoft.com/azure/templates/microsoft.operationalinsights/workspaces>
- SWA managed functions constraints (HTTP only, no managed identity, no Key
  Vault references; listed .NET runtimes) —
  <https://learn.microsoft.com/azure/static-web-apps/apis-functions>
- API versions: the newest non-preview version for every resource type, as
  reported by the Bicep linter rule `use-recent-api-versions` (Bicep 0.48.1).

Not found in Microsoft docs and taken from secondary sources: the Static Web
Apps region list (`centralus`, `eastus2`, `westus2`, `westeurope`,
`eastasia`, as quoted in Azure's own error message) and a documented minimum
for the Log Analytics daily cap (0.1 GB is used, as planned).
