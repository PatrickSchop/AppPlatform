# Deployment Guide

This app uses reusable GitHub Actions workflows from the AppPlatform repository to build and deploy. The workflow is automatically triggered on push to `main`.

## Setup

### 1. Configure Placeholders

Update `.github/workflows/deploy.yaml`:
- `app_name`, `resource_group` and `assembly_name` are filled in by `dotnet new`. Check them
  against what you actually provisioned: `app_name` must match the `appName` parameter you
  passed to `infra/app.bicep` (lowercase), `resource_group` the group you created, and
  `assembly_name` your project name without `.dll`.

### 2. Create Azure Credentials

Choose one of these auth methods:

#### Option A: OIDC (Recommended)

Set up GitHub → Azure OIDC trust and add these repository secrets:
- `AZURE_CLIENT_ID` — Azure app registration client ID
- `AZURE_TENANT_ID` — Azure tenant ID
- `AZURE_SUBSCRIPTION_ID` — Azure subscription ID

The workflow will automatically detect and use these if available.

#### Option B: RBAC (Legacy)

Add this repository secret:
- `AZURE_RBAC_CREDENTIALS` — Full Azure credentials JSON (from `az account show`)

### 3. Database User

Before the first deployment, run `infra/sql-user.sql` against the app database as an Entra SQL admin:

```bash
sqlcmd -S yourserver.database.windows.net -d yourdb -i infra/sql-user.sql
```

This creates the managed identity database user that the app needs.

### 4. GitHub Token

The workflow uses `${{ secrets.GITHUB_TOKEN }}` to restore platform NuGet packages from GitHub Packages. This is automatically available — no setup needed.

## Workflow Inputs

The reusable workflows accept these parameters (see `.github/workflows/deploy.yaml`):

| Input | Default | Purpose |
|-------|---------|---------|
| `app_name` | — | Short app name (e.g., `recipes`) — **required** |
| `resource_group` | — | Per-app Azure resource group — **required** |
| `function_app_name` | `{app_name}-api` | Function App name if different |
| `storage_account` | `{app_name}storage` | Storage account if different |
| `blob_container` | `web-{app_name}` | Blob container for SPA if different |
| `sql_server_name` | `pschop-db` | Shared SQL server name |
| `deploy_frontend` | `false` | Also deploy SPA front-end |
| `run_migrations` | `true` | Run `dotnet migrate` on deploy |

## Workflow Pinning

The `.github/workflows/deploy.yaml` references the platform workflows via `@main`, which means workflow changes in the AppPlatform repository immediately affect all consuming apps.

**To pin to a stable version**, change the references to a specific tag:

```yaml
uses: PatrickSchop/AppPlatform/.github/workflows/app-build.yaml@v0.1.0
uses: PatrickSchop/AppPlatform/.github/workflows/app-deploy.yaml@v0.1.0
```

This isolates you from breaking changes but requires manual updates to get improvements and bug fixes.

## Common Failures

### 401: Unauthorized restoring platform packages

**Cause:** Workflow missing `secrets: inherit`

**Fix:** Ensure `.github/workflows/deploy.yaml` has `secrets: inherit` on both workflow calls:

```yaml
jobs:
  build:
    uses: PatrickSchop/AppPlatform/.github/workflows/app-build.yaml@main
    secrets: inherit  # ← Must be present
```

### Migration fails: Login failure

**Cause:** `infra/sql-user.sql` was never run against the database

**Fix:** Run it now as an Entra SQL admin:

```bash
sqlcmd -S yourserver.database.windows.net -d yourdb -i infra/sql-user.sql
```

### Deployed app returns 500 on every request

**Cause:** App setting key uses `:` instead of `__` (double underscore)

**Example of wrong:** `database__connectionString` setting with key `database:connectionString`

**Fix:** The app setting key must use `__` as the section separator (this is what maps to `:` in `IConfiguration` on Linux). Verify in the Azure portal under Function App → Configuration.

## Logs

To view workflow logs:
1. Go to your repository's **Actions** tab
2. Click the workflow run you want to debug
3. Click the job to expand steps and view logs

For Azure deployment errors:
```bash
az functionapp log tail --resource-group <group> --name <app-name>-api
```

## Troubleshooting

If the workflow fails:
1. Check the **Actions** tab for the exact error
2. Verify all secrets are set correctly
3. Ensure the SQL database user exists (step 3 above)
4. Confirm the resource group and app name in `deploy.yaml` match your Azure setup
5. Run `az bicep build` locally to verify your infrastructure code compiles
