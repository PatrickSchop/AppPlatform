# Step 20 â€” Azure infrastructure as bicep

**Phase:** 4 â€” Template and infrastructure
**Depends on:** Step 18
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Put the per-app Azure footprint in code. Today **none** of it is (analysis Â§5) â€” there is no
bicep, ARM or terraform anywhere in StockAnalysis, and every resource was created by hand.
That is precisely the "shortcuts get taken" risk this whole exercise exists to remove.

## The topology (analysis Â§5)

| Shared â€” created once, referenced by id | Per app â€” created by `app.bicep` |
|---|---|
| Resource group `Applications` | Azure SQL **database** on the shared server |
| SQL server `pschop-db` | Function App (consumption, Flex Consumption if available) |
| Storage account `stockinfostorage` | Blob container `web-<app>` |
| Entra tenant | User-assigned managed identity |
| Azure OpenAI account | Custom domain `<app>.PS.nl` |

A new app is then `dotnet new tinyapp` plus one bicep deployment.

## Tasks

### 1. `infra/app.bicep`

```bicep
targetScope = 'resourceGroup'

@description('Short app name, lowercase, used to derive every resource name.')
@minLength(3)
@maxLength(20)
param appName string

@description('Existing shared SQL server name.')
param sqlServerName string = 'pschop-db'

@description('Existing shared storage account name.')
param storageAccountName string = 'stockinfostorage'

param location string = resourceGroup().location

@description('SQL database SKU. Basic is the cheap default; GP_S_Gen5_1 auto-pauses.')
param sqlSkuName string = 'Basic'

@description('Custom domain, e.g. recipes.PS.nl. Empty skips domain binding.')
param customDomain string = ''

@description('Azure OpenAI account resource id. Empty skips the role assignment.')
param openAiAccountId string = ''
```

Resources to create:

1. **User-assigned managed identity** `id-<appName>`. Everything else authenticates as this;
   creating it first is what makes the rest key-free.
2. **SQL database** `<appName>` on the existing server, via an `existing` reference to
   `sqlServerName`. Set `zoneRedundant: false` and the chosen SKU.
3. **Blob container** `web-<appName>` on the existing storage account.
4. **Function App** `<appName>-api`, consumption plan, `dotnet-isolated`, `net10.0`,
   `FUNCTIONS_EXTENSION_VERSION ~4`, with the user-assigned identity attached and
   `httpsOnly: true`, `minTlsVersion: '1.2'`.
5. **App settings** on the function app:
   - `AzureWebJobsStorage__accountName` (identity-based, not a connection string)
   - `DEV_ENVIRONMENT = production`
   - `azureIdentity__type = userAssigned`
   - `azureIdentity__clientId = <identity client id>`
   - `database__connectionString` â€” `Server=tcp:<server>.database.windows.net,1433;Database=<appName>;Encrypt=True;`
     with **no credentials**; the Step 04 interceptor supplies the token
   - `database__useManagedIdentity = true`
   - `staticContent__blob__uri = https://<storage>.blob.core.windows.net/web-<appName>`
   - `backgroundTasks__apiBaseUrl = https://<appName>-api.azurewebsites.net`
   - `backgroundTasks__checkSchedule = 0 */5 * * * *`

   Use `__` (double underscore) as the section separator â€” it is what maps to `:` in
   `IConfiguration` on Linux. Getting this wrong is a silent misconfiguration, so put a
   comment in the bicep saying so.
6. **Easy Auth explicitly disabled.** The platform authenticates in-process (Step 10); App
   Service Authentication would intercept requests before any platform code runs and break
   the security model â€” see `docs/auth-setup.md` Part 4a for the three specific failures.

   Declare it rather than relying on the default, so that a redeploy also *reverts* it if
   someone enabled it by hand in the portal:

   ```bicep
   resource authSettings 'Microsoft.Web/sites/config@2023-12-01' = {
     parent: functionApp
     name: 'authsettingsV2'
     properties: {
       // The platform authenticates in-process. Easy Auth must stay off:
       // it would turn /api/* 401s into 302 redirects and stop /configuration.json
       // and /api/health being anonymous. See docs/auth-setup.md Part 4a.
       globalValidation: {
         requireAuthentication: false
         unauthenticatedClientAction: 'AllowAnonymous'
       }
       platform: {
         enabled: false
       }
     }
   }
   ```

7. **Role assignments** for the managed identity:
   - `Storage Blob Data Reader` on the storage account (serving the SPA)
   - `Storage Blob Data Owner` scoped to the app's own container (the `AzureWebJobsStorage`
     identity-based connection needs write)
   - `Cognitive Services OpenAI User` on `openAiAccountId`, conditional on it being non-empty
8. **Custom domain binding** plus a managed certificate, conditional on `customDomain`.
   Note in a comment that the DNS `CNAME` and the `asuid` TXT record must exist **before**
   this deploys, or it fails â€” that ordering constraint is not obvious.

Outputs: `functionAppName`, `functionAppHostName`, `identityClientId`, `identityPrincipalId`,
`databaseName`, `blobContainerUri`.

### 2. `infra/sql-user.sql`

The one thing bicep **cannot** do: the managed identity needs a database user, and that is a
T-SQL operation against the database, executed by an Entra admin.

```sql
-- Run against the app's database as an Entra admin on the SQL server.
-- Replace <identity-name> with the managed identity name, e.g. id-recipes.
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'<identity-name>')
BEGIN
    CREATE USER [<identity-name>] FROM EXTERNAL PROVIDER;
END
ALTER ROLE db_datareader ADD MEMBER [<identity-name>];
ALTER ROLE db_datawriter ADD MEMBER [<identity-name>];
ALTER ROLE db_ddladmin  ADD MEMBER [<identity-name>];
```

`db_ddladmin` is needed because the app runs its own migrations. Say so in a comment, and say
that dropping it means moving migrations out of the app â€” a real trade-off worth stating
rather than silently granting.

### 3. `infra/shared.bicep`

The shared resources, for documentation and disaster recovery. It is **not** run routinely â€”
these already exist and running it could disrupt StockAnalysis.

Put a prominent header comment:

```bicep
// DO NOT RUN against the live subscription without review.
// The shared resources already exist and are used by StockAnalysis.
// This file documents them and allows recreation in a new subscription.
```

Cover the resource group, SQL server with Entra admin, storage account with static website
enabled, and the Azure OpenAI account.

### 4. `infra/main.bicepparam` template

For the `dotnet new` template (Step 18), with placeholder tokens:

```bicep
using '../infra/app.bicep'

param appName = 'TINYAPP-NAME'
param sqlServerName = 'SQL-SERVER-PLACEHOLDER'
param storageAccountName = 'STORAGE-ACCOUNT-PLACEHOLDER'
param customDomain = ''
```

### 5. `docs/provisioning.md`

The runbook, in order, because the ordering has real dependencies:

1. `az deployment group create --resource-group Applications --template-file infra/app.bicep --parameters appName=<app>`
2. Run `infra/sql-user.sql` against the new database as the Entra SQL admin
3. Add the App Role (Step 22)
4. Push to GitHub; the deploy workflow does the rest
5. Optional: DNS records, then re-deploy with `customDomain` set

Include the teardown too:

```powershell
az sql db delete --resource-group Applications --server pschop-db --name <app> --yes
az functionapp delete --resource-group Applications --name <app>-api
az storage container delete --account-name stockinfostorage --name web-<app>
az identity delete --resource-group Applications --name id-<app>
```

A documented teardown is what makes a throwaway app genuinely throwaway.

### 6. Add bicep linting to CI

In `.github/workflows/ci.yaml`:

```yaml
- name: Lint bicep
  run: |
    az bicep install
    az bicep build --file infra/app.bicep --stdout > /dev/null
    az bicep build --file infra/shared.bicep --stdout > /dev/null
```

Compilation is not validation, but it catches typos, and an infra file that does not compile
is worse than none.

## Verification

```powershell
cd C:\Dev\AppPlatform
az bicep build --file infra\app.bicep --stdout > $null
az deployment group what-if `
  --resource-group Applications `
  --template-file infra\app.bicep `
  --parameters appName=biceptest
```

**Expected:** compiles clean; the what-if lists **only** creates â€” the identity, database,
container, function app, auth settings and role assignments. **If it shows a modify or delete
on any shared resource, stop and fix `app.bicep`.** It must never touch the shared server,
storage account or anything belonging to StockAnalysis.

Confirm the Easy Auth assertion is actually in the compiled template:

```powershell
az bicep build --file infra\app.bicep --stdout |
    ConvertFrom-Json |
    Select-Object -ExpandProperty resources |
    Where-Object { $_.type -like '*sites/config*' -and $_.name -like '*authsettingsV2*' }
```

**Expected:** one resource, with `platform.enabled` false and
`globalValidation.unauthenticatedClientAction` of `AllowAnonymous`. If it is absent, the
template will silently leave Easy Auth at whatever a portal user last set it to.

A real deployment happens as part of Step 19 Check 7.

## Done when

- [ ] `infra/app.bicep` compiles and `what-if` shows creates only
- [ ] It touches no shared resource and no StockAnalysis resource
- [ ] The template declares `authsettingsV2` with Easy Auth **disabled**, so a redeploy
      reverts a hand-enabled setting
- [ ] Every credential path is managed identity; no connection string carries a password
- [ ] App settings use `__` separators
- [ ] `infra/sql-user.sql` exists with the `db_ddladmin` rationale
- [ ] `infra/shared.bicep` carries the DO-NOT-RUN header
- [ ] `docs/provisioning.md` covers provision **and** teardown
- [ ] CI lints the bicep

## Commit

```powershell
git add -A
git commit -m "Step 20: bicep for the per-app Azure footprint; shared resources documented"
```

