targetScope = 'resourceGroup'

@description('Short app name, lowercase, used to derive every resource name. Capped at 17 chars because it also names a storage account (appName + "storage"), which must stay under Azure\'s 24-char storage account name limit.')
@minLength(3)
@maxLength(17)
param appName string

@description('Existing shared SQL server name.')
param sqlServerName string = 'pschop-db'

@description('Resource group holding the shared SQL server.')
param sharedResourceGroupName string = 'ApplicationsShared'

param location string = resourceGroup().location

@description('SQL database SKU. Basic is the cheap default; GP_S_Gen5_1 auto-pauses.')
param sqlSkuName string = 'Basic'

@description('Custom domain, e.g. recipes.PS.nl. Empty skips domain binding.')
param customDomain string = ''

@description('Azure OpenAI account resource id. Empty skips the role assignment.')
param openAiAccountId string = ''

// User-assigned managed identity for the app
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${appName}'
  location: location
}

// SQL database on the existing server. The server lives in the shared resource group,
// so this is a module deployed with that scope (see modules/database.bicep) — the
// database resource must live there too, since child resources can't live in a
// different resource group than their parent.
module database 'modules/database.bicep' = {
  name: '${appName}-database'
  scope: resourceGroup(sharedResourceGroupName)
  params: {
    sqlServerName: sqlServerName
    appName: appName
    location: location
    sqlSkuName: sqlSkuName
  }
}

// Storage account, owned by this app alone (not shared with other apps): SPA static
// content plus this Function App's own AzureWebJobsStorage. Sharing one account across
// apps' AzureWebJobsStorage is a known anti-pattern (host lease/trigger state collides).
resource storageAccount 'Microsoft.Storage/storageAccounts@2021-06-01' = {
  name: '${appName}storage'
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
  }
}

resource blobContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2021-06-01' = {
  name: '${storageAccount.name}/default/web-${appName}'
}

// Consumption plan for Functions
resource consumptionPlan 'Microsoft.Web/serverfarms@2021-02-01' = {
  name: '${appName}-plan'
  location: location
  kind: 'functionapp'
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
}

// Function App
resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: '${appName}-api'
  location: location
  kind: 'functionapp'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    serverFarmId: consumptionPlan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      functionAppScaleLimit: 200
      appSettings: [
        {
          // Required for Azure to run this as a .NET isolated Functions app at all.
          // Without these two, the platform is silently never invoked: the Function App
          // accepts deployments and reports healthy, but indexes zero functions and every
          // route 404s, with no error surfaced anywhere.
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          // Configuration via __ (double underscore) separators, not colons.
          // This maps to : in IConfiguration on Linux. Using : would silently misconfigure.
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccount.name
        }
        {
          name: 'DEV_ENVIRONMENT'
          value: 'production'
        }
        {
          name: 'azureIdentity__type'
          value: 'userAssigned'
        }
        {
          name: 'azureIdentity__clientId'
          value: identity.properties.clientId
        }
        {
          name: 'database__connectionString'
          value: 'Server=tcp:${sqlServerName}.${environment().suffixes.sqlServerHostname},1433;Database=${appName};Encrypt=True;'
        }
        {
          name: 'database__useManagedIdentity'
          value: 'true'
        }
        {
          name: 'staticContent__blob__uri'
          value: 'https://${storageAccount.name}.blob.${environment().suffixes.storage}/web-${appName}'
        }
        {
          name: 'backgroundTasks__apiBaseUrl'
          value: 'https://${appName}-api.azurewebsites.net'
        }
        {
          name: 'backgroundTasks__checkSchedule'
          value: '0 */5 * * * *'
        }
      ]
    }
  }
}

// Disable Easy Auth explicitly. The platform authenticates in-process (Step 10).
// Easy Auth intercepts before platform code runs and breaks the security model:
// - turns /api/* 401s into 302 redirects (breaks SPA bootstrap)
// - intercepts /configuration.json (breaks role loading)
// See docs/auth-setup.md Part 4a for the three specific failures.
// This declaration reverts any hand-enabled setting if redeploy happens.
resource authSettings 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: functionApp
  name: 'authsettingsV2'
  properties: {
    globalValidation: {
      requireAuthentication: false
      unauthenticatedClientAction: 'AllowAnonymous'
    }
    platform: {
      enabled: false
    }
  }
}

// Role: Storage Blob Data Reader on storage account (for serving SPA)
resource blobReaderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(storageAccount.id, identity.id, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1')
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Role: Storage Blob Data Owner on app's container (for identity-based AzureWebJobsStorage)
resource blobOwnerRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: blobContainer
  name: guid(blobContainer.id, identity.id, '0c867c2a-1d8c-454a-a3db-ab2ea1bdc13b')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Role: Cognitive Services OpenAI User (conditional on openAiAccountId being provided)
resource openAiRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(openAiAccountId)) {
  scope: resourceGroup()
  name: guid(openAiAccountId, identity.id, '13b0f5d9-42f6-4501-bef8-25aaf2c0fc04')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '13b0f5d9-42f6-4501-bef8-25aaf2c0fc04')
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Custom domain binding (conditional on customDomain being provided)
// NOTE: The DNS CNAME record and Azure-managed certificate must exist BEFORE this deploys.
// Use Azure Portal or `az webapp up --name <app>-api --custom-domain <domain>` after deployment.
// This resource is intentionally omitted to avoid certificate creation complexity in bicep.

// Outputs
@description('Function App name')
output functionAppName string = functionApp.name

@description('Function App default hostname')
output functionAppHostName string = functionApp.properties.defaultHostName

@description('Managed identity client id')
output identityClientId string = identity.properties.clientId

@description('Managed identity principal id')
output identityPrincipalId string = identity.properties.principalId

@description('Database name')
output databaseName string = database.outputs.databaseName

@description('Blob container URI')
output blobContainerUri string = '${storageAccount.properties.primaryEndpoints.blob}web-${appName}'
