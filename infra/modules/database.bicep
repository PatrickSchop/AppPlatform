// Deployed via a module with an explicit `scope` from app.bicep, because the SQL
// server lives in the shared resource group while the rest of app.bicep deploys into
// the app's own resource group — a plain resource can't span two scopes in one file.

param sqlServerName string
param appName string
param location string
param sqlSkuName string

resource sqlServer 'Microsoft.Sql/servers@2019-06-01-preview' existing = {
  name: sqlServerName
}

resource database 'Microsoft.Sql/servers/databases@2021-11-01' = {
  parent: sqlServer
  name: appName
  location: location
  sku: {
    name: sqlSkuName
  }
  properties: {
    zoneRedundant: false
  }
}

output databaseName string = database.name
