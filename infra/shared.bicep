// Shared Azure resources for the AppPlatform: the SQL logical server and its Entra
// admin. Deployed once into the `ApplicationsShared` resource group. Per-app databases
// are created by app.bicep against this server, in that same resource group (Azure
// requires child resources to live in their parent's resource group).
//
// This is live IaC, not just documentation: run `what-if` before any redeploy to
// confirm it matches reality with zero unexpected changes.
//
// Storage and Azure OpenAI used to be documented here as "shared", but in practice
// only one app used them. They're provisioned per-app now (storage by app.bicep;
// OpenAI ad hoc per app, when needed) rather than centrally here.

targetScope = 'resourceGroup'

param location string = resourceGroup().location

@description('SQL server admin login name.')
param sqlAdminLogin string = 'ps-admin'

@secure()
@description('SQL server admin password. Pass at deploy time, never commit a value for this.')
param sqlAdminLoginPassword string

@description('Entra tenant ID for the SQL server AAD admin.')
param entraTenantId string

@description('Object id (SID) of the Entra AAD admin (user, group, or service principal) for the SQL server.')
param entraAdminObjectId string

@description('Display name of the Entra AAD admin, shown in the portal.')
param entraAdminLogin string

// SQL Server with Entra admin
resource sqlServer 'Microsoft.Sql/servers@2019-06-01-preview' = {
  name: 'pschop-db'
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminLoginPassword
    version: '12.0'
  }
}

// Entra admin on SQL Server
resource sqlAadAdmin 'Microsoft.Sql/servers/administrators@2019-06-01-preview' = {
  parent: sqlServer
  name: 'ActiveDirectory'
  properties: {
    administratorType: 'ActiveDirectory'
    login: entraAdminLogin
    sid: entraAdminObjectId
    tenantId: entraTenantId
  }
}

@description('SQL server name, for app.bicep\'s sqlServerName param.')
output sqlServerName string = sqlServer.name
