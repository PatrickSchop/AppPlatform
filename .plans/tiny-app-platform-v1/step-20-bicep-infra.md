# Step 20 â€” Azure infrastructure as bicep

**Phase:** 4 â€” Template and infrastructure
**Depends on:** Step 18
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Put the per-app Azure footprint in code. Today **none** of it is (analysis Â§5) â€” there is no
bicep, ARM or terraform anywhere in StockAnalysis, and every resource was created by hand.
That is precisely the "shortcuts get taken" risk this whole exercise exists to remove.

## The topology (analysis Â§5)

**Post-Step-20 revision:** the original plan below put every app plus the shared SQL server
and a shared storage account into one `Applications` resource group. That was reorganized once
this ran for real: a shared `AzureWebJobsStorage` account across apps is a known anti-pattern
(host lease/trigger state collides), and one flat resource group made per-app teardown and
access scoping harder than it needed to be. The as-built topology:

| Shared â€” resource group `ApplicationsShared` | Per app â€” own resource group, created by `app.bicep` |
|---|---|
| SQL server `pschop-db` + its Entra admin | Azure SQL **database** on the shared server (deployed as a module scoped into `ApplicationsShared`, since a database must live in its server's resource group) |
| Entra tenant | Storage account `<app>storage` (own account, not shared) with blob container `web-<app>` |
| | Function App (consumption plan) |
| | User-assigned managed identity |
| | Custom domain `<app>.PS.nl` |

Azure OpenAI is no longer treated as a shared platform resource â€” it's provisioned ad hoc per
app when needed, via the existing `openAiAccountId` param.

A new app is `az group create --name <AppName>` plus one bicep deployment into it (see
`docs/provisioning.md`).

## Tasks

### 1. `infra/app.bicep`

**As-built** (see `infra/app.bicep`) â€” the storage account is no longer an existing shared
resource passed in by name; the template provisions its own per-app account, and the SQL
server reference now also carries which resource group it lives in:

```bicep
targetScope = 'resourceGroup'

@description('Short app name, lowercase, used to derive every resource name. Capped at 17
chars because it also names a storage account (appName + "storage"), which must stay under
Azure\'s 24-char storage account name limit.')
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
```

Resources to create:

1. **User-assigned managed identity** `id-<appName>`. Everything else authenticates as this;
   creating it first is what makes the rest key-free.
2. **SQL database** `<appName>` on the existing server, deployed as a module
   (`modules/database.bicep`) scoped into `sharedResourceGroupName` â€” the database is a child
   resource of the server and must live in the server's resource group, which is no longer this
   deployment's own resource group. Set `zoneRedundant: false` and the chosen SKU.
3. **Storage account** `<appName>storage`, created fresh by this deployment (not shared with
   other apps â€” a shared `AzureWebJobsStorage` account across apps is a known anti-pattern), plus
   **blob container** `web-<appName>` on it.
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

7. **Role assignments** for the managed identity, all account-wide on the app's own storage
   account (revised after a real deploy: account-scoped Reader plus container-scoped Owner
   looked sufficient but a **fresh** storage account has none of the Functions host's
   bookkeeping containers yet â€” `azure-webjobs-hosts`, `azure-webjobs-secrets` â€” so the host's
   first-run write to create them failed with an opaque `InternalServerError`):
   - `Storage Blob Data Owner` account-wide (covers the SPA container plus the host's own
     bookkeeping containers)
   - `Storage Queue Data Contributor` and `Storage Table Data Contributor` account-wide
     (identity-based `AzureWebJobsStorage` needs queue/table access for internal bookkeeping,
     e.g. timer trigger locking, even for apps with no queue/table bindings of their own)
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

**As-built:** this now covers only the SQL server and its Entra admin, deployed into
`ApplicationsShared`. Storage and Azure OpenAI turned out not to be genuinely shared in
practice (only one app used them) and are provisioned per-app instead â€” storage by `app.bicep`
itself, OpenAI ad hoc per app when needed. Secrets (`sqlAdminLoginPassword`) are `@secure()`
params, not hardcoded placeholders, since this is deployed for real, not documentation-only.

Put a prominent header comment:

```bicep
// DO NOT RUN against the live subscription without review.
// Run `what-if` before any redeploy to confirm it matches reality with zero unexpected changes.
```

### 4. `infra/main.bicepparam` template

For the `dotnet new` template (Step 18), with placeholder tokens:

```bicep
using '../infra/app.bicep'

param appName = 'TINYAPP-NAME'
param sqlServerName = 'SQL-SERVER-PLACEHOLDER'
param customDomain = ''
```

There is no `storageAccountName` param â€” `app.bicep` provisions its own storage account, it no
longer references an existing shared one.

### 5. `docs/provisioning.md`

The runbook, in order, because the ordering has real dependencies:

1. `az group create --name <AppName> --location westeurope`, then
   `az deployment group create --resource-group <AppName> --template-file infra/app.bicep --parameters appName=<app>`
   â€” each app gets its own resource group; only the SQL server lives in `ApplicationsShared`
2. Run `infra/sql-user.sql` against the new database as the Entra SQL admin
3. Add the App Role (Step 22)
4. Push to GitHub; the deploy workflow does the rest
5. Optional: DNS records, then re-deploy with `customDomain` set

Include the teardown too:

```powershell
# The database is the one resource outside the app's own resource group.
az sql db delete --resource-group ApplicationsShared --server pschop-db --name <app> --yes
az group delete --name <AppName> --yes
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
az group create --name biceptest --location westeurope
az deployment group what-if `
  --resource-group biceptest `
  --template-file infra\app.bicep `
  --parameters appName=biceptest
```

**Expected:** compiles clean; the what-if lists **only** creates in `biceptest` â€” the identity,
storage account, container, function app, auth settings and role assignments â€” plus a database
create inside the `ApplicationsShared` scope from the nested module. **If it shows a modify or
delete on any resource in `ApplicationsShared` other than the new database, stop and fix
`app.bicep`.** It must never touch the shared SQL server itself or anything belonging to
another app.

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

