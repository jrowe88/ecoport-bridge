// Parameters for the dev environment. Personal values come from environment
// variables set by infra/deploy-infra.ps1, so nothing personal is committed.
// The empty fallbacks only exist so the file builds offline; the script
// refuses to deploy without real values.
using 'main.bicep'

param env = 'dev'
param swaLocation = 'centralus'
param adminPrincipalId = readEnvironmentVariable('ECOPORT_ADMIN_PRINCIPAL_ID', '')
param budgetEmail = readEnvironmentVariable('ECOPORT_BUDGET_EMAIL', '')
param budgetStartDate = readEnvironmentVariable('ECOPORT_BUDGET_START_DATE', '')
param budgetAmount = 5
