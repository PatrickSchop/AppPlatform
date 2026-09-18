// DO NOT RUN against the live subscription without review.
// The shared resources already exist and are used by StockAnalysis.
// This file documents them and allows recreation in a new subscription.

targetScope = 'resourceGroup'

param location string = 'eastus'

// Resource group
@description('Resource group for all applications. Created once, shared by all apps.')
param resourceGroupName string = 'Applications'

// SQL Server with Entra admin
resource sqlServer 'Microsoft.Sql/servers@2019-06-01-preview' = {
  name: 'pschop-db'
  location: location
  properties: {
    administratorLogin: 'ps-admin'
    administratorLoginPassword: 'ChangeMeToStrongPassword!'  // Replace with secure value
    version: '12.0'
  }
}

// Entra admin on SQL Server
resource sqlAadAdmin 'Microsoft.Sql/servers/administrators@2019-06-01-preview' = {
  parent: sqlServer
  name: 'ActiveDirectory'
  properties: {
    administratorType: 'ActiveDirectory'
    login: 'AzureAD'
    sid: 'TenantIdGuid'  // Replace with actual tenant id
    tenantId: 'TenantIdGuid'  // Replace with actual tenant id
  }
}

// Storage account for SPA hosting and function app storage
resource storageAccount 'Microsoft.Storage/storageAccounts@2021-06-01' = {
  name: 'stockinfostorage'
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    https: true
  }
}

// Static website hosting on storage account
resource staticWebsite 'Microsoft.Storage/storageAccounts/blobServices/containers@2021-06-01' = {
  name: '${storageAccount.name}/default/$web'
  properties: {
    publicAccess: 'Container'
  }
}

// Azure OpenAI account
resource openAiAccount 'Microsoft.CognitiveServices/accounts@2023-05-01' = {
  name: 'ps-openai'
  location: location
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubdomainName: 'ps-openai'
  }
}
