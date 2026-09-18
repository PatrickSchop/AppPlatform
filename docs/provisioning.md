# Provisioning and Deployment Guide

This guide walks through provisioning a new app on the PS.AppPlatform, from `dotnet new tinyapp` to running in Azure.

**Prerequisites:**
- `.NET 10 SDK` and `Azure Functions Core Tools`
- `Azure CLI` authenticated to your subscription
- Contributor access to create resource groups, and read access to the `ApplicationsShared`
  resource group (the shared SQL server every app's database lives on)
- An Entra tenant with admin rights (for app registration)

## Step 1: Generate the app from template

```powershell
dotnet new tinyapp -n <AppName> -pv 0.1.0 --platform-version 0.1.0 --app-role <app>.user
cd <AppName>
```

## Step 2: Provision Azure resources

Each app gets its own resource group, named after the app. Create it, then deploy the
per-app infrastructure into it.

```powershell
az group create --name <AppName> --location westeurope

az deployment group create `
  --resource-group <AppName> `
  --template-file infra/app.bicep `
  --parameters appName=<app>

# Save the outputs (you'll need them next)
az deployment group show `
  --name app `
  --resource-group <AppName> `
  --query properties.outputs
```

The deployment creates, all inside `<AppName>`:
- User-assigned managed identity (`id-<app>`)
- Storage account (`<app>storage`) with a blob container (`web-<app>`) for SPA static
  content and this app's own `AzureWebJobsStorage`
- Function App (`<app>-api`)
- Role assignments for managed identity access

It also creates a SQL database (`<app>`) on the shared server in `ApplicationsShared` —
that one resource lives in the shared resource group, not the app's own, since Azure
requires a database to live in the same resource group as its server.

## Step 3: Create database user

The managed identity needs database-level permissions. Run this as an Entra SQL admin:

```powershell
sqlcmd -S '<server>.database.windows.net' -d '<app>' -U '<admin-email>' -P '<password>' -i 'infra/sql-user.sql'

# Edit the script to replace <identity-name> with id-<app> before running
```

## Step 4: Deploy the Function App

Push to GitHub and the CI/CD workflow will:
1. Build the app
2. Run tests
3. Deploy to the Function App
4. Run database migrations (`dotnet run -- --migrate`)

```powershell
git push origin main
```

The workflow uses your existing GitHub Actions deployment credentials configured in the platform.

## Step 5: Register Entra app and configure auth

See `docs/auth-setup.md` for creating the app registration and App Roles. You'll need:
- Tenant ID
- Client ID (app id)
- App Role name matching the `-ar` parameter from Step 1

## Step 6 (Optional): Custom domain

If you want a custom domain (e.g., `<app>.PS.nl`):

1. Create DNS records:
   - `CNAME` record pointing to `<app>-api.azurewebsites.net`
   - `asuid` TXT record with the asuid value from the Function App (Azure Portal > Custom domains > Verify)

2. Redeploy with the domain:

```powershell
az deployment group create `
  --resource-group <AppName> `
  --template-file infra/app.bicep `
  --parameters appName=<app> customDomain=<app>.PS.nl
```

## Teardown

To completely remove the app and reclaim resources:

```powershell
# The database is the one thing outside the app's own resource group
az sql db delete --resource-group ApplicationsShared --server pschop-db --name <app> --yes

az group delete --name <AppName> --yes
```

A documented teardown makes a throwaway app genuinely throwaway — run it when you're done to avoid surprise charges.

## Troubleshooting

### Function App cannot read database
- Verify the managed identity user was created in Step 3
- Check that the identity has `db_datareader`, `db_datawriter`, and `db_ddladmin` roles
- Verify `database__useManagedIdentity` is set to `true` in Function App settings

### Authentication fails at 401 instead of 403
- Verify the App Role was created in Entra
- Check that your user/service principal is assigned the role
- Verify `authentication__requiredRole` in config matches the role name

### Easy Auth intercepts API calls
- Verify the bicep deployed `authsettingsV2` with `enabled: false`
- Manual fix: Azure Portal > Function App > Authentication > turn off

### /configuration.json returns HTML instead of JSON
- This indicates Easy Auth is intercepting (see above)
- The platform serves `/configuration.json` unauthenticated; if it redirects, Easy Auth is enabled

## See also

- [Background task lease renewal](background-tasks.md)
- [Authentication setup](auth-setup.md)
- [Consuming NuGet packages](consuming-packages.md)
