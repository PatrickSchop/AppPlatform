# Step 21 — Reusable GitHub Actions workflows

**Phase:** 4 — Template and infrastructure
**Depends on:** Step 20
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Move the build and deploy pipelines into the platform repo as `workflow_call` workflows
parameterised by app name, so each app keeps a ~15-line `deploy.yaml` — and fix the two bugs
in the originals while porting.

## The two bugs to fix (analysis §5)

Both are in `C:\Dev\StockAnalysis\.github\workflows\build.yaml`:

1. **The inputs are declared `dotnetversion` and `node_version` but read as
   `inputs.dotnet_version`.** An undeclared input evaluates to empty, so `setup-dotnet`
   silently gets nothing and falls back to whatever the runner has. It works by accident.
2. **The default is `9.0.x` against a `net10.0` project.** Stale, and only harmless because
   of bug 1.

Fix both: consistent naming (`dotnet_version`, `node_version`) and a `10.0.x` default.

## Tasks

### 1. `.github/workflows/app-build.yaml`

```yaml
name: Build a platform app

on:
  workflow_call:
    inputs:
      dotnet_version:
        required: false
        type: string
        default: '10.0.x'
      node_version:
        required: false
        type: string
        default: '24'
      app_project_path:
        required: false
        type: string
        default: './App'
      frontend_path:
        required: false
        type: string
        default: ''
        description: 'Front-end folder. Empty means backend only.'
      frontend_dist_path:
        required: false
        type: string
        default: ''
        description: 'Build output relative to frontend_path, e.g. dist/WebApp/browser'
```

Two jobs:

**`dotnet-build`** — checkout, `setup-dotnet` with `inputs.dotnet_version`, `dotnet publish`
into `output/App`, upload the `App` artifact with `include-hidden-files: true`.

Use `dotnet publish`, not the source's `dotnet build --output`. `build --output` copies the
whole build tree; `publish` produces what actually deploys and is what the Functions action
expects.

**`frontend-build`** — `if: inputs.frontend_path != ''`, `setup-node` with `node_version`,
npm cache keyed on `${{ inputs.frontend_path }}/package-lock.json`, `npm ci` (not
`npm install` — the source uses `install`, which ignores the lockfile in CI), `npm run build`,
upload the `frontend` artifact from `frontend_dist_path`.

Add a NuGet auth step so apps can restore the platform packages:

```yaml
- name: Authenticate to GitHub Packages
  run: |
    dotnet nuget add source https://nuget.pkg.github.com/PatrickSchop/index.json \
      --name wisdi --username ${{ github.actor }} \
      --password ${{ secrets.GITHUB_TOKEN }} --store-password-in-clear-text
```

This needs `secrets: inherit` from the caller, or an explicit `secrets:` block. Document which.

### 2. `.github/workflows/app-deploy.yaml`

```yaml
on:
  workflow_call:
    inputs:
      app_name:          { required: true,  type: string }   # e.g. recipes
      function_app_name: { required: false, type: string, default: '' }  # defaults to <app_name>-api
      storage_account:   { required: false, type: string, default: 'stockinfostorage' }
      blob_container:    { required: false, type: string, default: '' }  # defaults to web-<app_name>
      sql_server_name:   { required: false, type: string, default: 'pschop-db' }
      resource_group:    { required: false, type: string, default: 'Applications' }
      environment:       { required: false, type: string, default: 'prod' }
      deploy_frontend:   { required: false, type: boolean, default: false }
      run_migrations:    { required: false, type: boolean, default: true }
    secrets:
      AZURE_RBAC_CREDENTIALS: { required: true }
```

Port from `C:\Dev\StockAnalysis\.github\workflows\deploy.yaml`, parameterised. Keep its good
parts:

- the SQL firewall dance: get the runner IP from `api.ipify.org`, add a rule, migrate, and
  **remove it in an `if: always()` step**. That `always()` is important — without it a failed
  migration leaves the firewall open.
- `rm ./App/appsettings.development.json` before deploying, so dev settings never ship.

Fix these while porting:

- **`continue-on-error: true` on the migration step must go.** In the source a failed
  migration is reported as a successful deploy, leaving code running against an old schema —
  the worst possible outcome. Make it fail the workflow.
- **Use a unique firewall rule name per run**: `gh-<run_id>` instead of the fixed
  `GitHubActionsRunnerIP`. Concurrent deploys of two apps currently fight over one rule and
  can delete each other's access.
- **Deploy the front-end before the backend.** The source's blob delete-batch plus
  upload-batch leaves a window where the SPA is partially uploaded; doing it first means the
  backend cut-over is the last visible change. Better still, upload first and delete stale
  blobs afterwards — note that as a future improvement rather than doing a full atomic swap now.
- **Use OIDC federated credentials** if available (`azure/login` with `client-id`,
  `tenant-id`, `subscription-id` and `permissions: id-token: write`) instead of the source's
  long-lived `AZURE_RBAC_CREDENTIALS` secret. Support both; prefer OIDC in the docs.

### 3. The caller's `deploy.yaml` in the template

This is the deliverable — what a new app actually contains:

```yaml
name: Deploy

on:
  push:
    branches: [main]

jobs:
  build:
    uses: PatrickSchop/AppPlatform/.github/workflows/app-build.yaml@v0.1.0
    secrets: inherit
    with:
      frontend_path: './WebApp'
      frontend_dist_path: 'dist/WebApp/browser'

  deploy:
    needs: build
    uses: PatrickSchop/AppPlatform/.github/workflows/app-deploy.yaml@v0.1.0
    secrets: inherit
    with:
      app_name: TINYAPP-NAME
      deploy_frontend: true
```

Pin to a **tag**, not `@main`. A reusable workflow referenced at `@main` means a platform
change can break every app's deploy without any app changing. Note that explicitly in
`docs/deployment.md` — it is exactly the kind of coupling that makes shared CI unpleasant.

### 4. `docs/deployment.md`

Cover: what each input does, the two auth options (OIDC preferred, RBAC secret as fallback),
the required repository secrets, why the workflow is pinned to a tag, and how to upgrade the
pin.

Include a troubleshooting section for the three failures that will actually happen:
- `401` restoring platform packages → `secrets: inherit` is missing
- migration fails with a login error → `infra/sql-user.sql` was never run (Step 20)
- the deployed app returns 500 on every request → `database__connectionString` app setting
  uses `:` instead of `__`

### 5. Update the platform's own CI

Add a `workflow_call` lint job that runs `actionlint` over `.github/workflows/`, so a syntax
error in a reusable workflow is caught here rather than in a consuming app.

## Verification

```powershell
cd C:\Dev\AppPlatform
Get-ChildItem .github\workflows\*.yaml | ForEach-Object {
    python -c "import sys,yaml; yaml.safe_load(open(sys.argv[1], encoding='utf-8'))" $_.FullName
}
```

Then the real check — grep for the bug that was fixed:

```powershell
Select-String -Path .github\workflows\*.yaml -Pattern 'dotnetversion|9\.0\.x'
```

**Expected:** no matches. Both originals are gone.

Then confirm consistency between declared and referenced inputs:

```powershell
Select-String -Path .github\workflows\app-build.yaml -Pattern 'inputs\.\w+' -AllMatches |
  ForEach-Object { $_.Matches.Value } | Sort-Object -Unique
```

**Expected:** every name that appears also appears in the `on.workflow_call.inputs` block.
This is the mechanical version of the bug-1 check.

End-to-end verification is Step 19 Check 7.

## Done when

- [ ] Input names are consistent between declaration and use — bug 1 fixed
- [ ] `10.0.x` default — bug 2 fixed
- [ ] `dotnet publish` replaces `dotnet build --output`
- [ ] `npm ci` replaces `npm install`
- [ ] The migration step fails the workflow rather than continuing on error
- [ ] The firewall rule name is unique per run and removed in an `always()` step
- [ ] The template's `deploy.yaml` pins a tag, and the docs explain why
- [ ] `docs/deployment.md` covers the three likely failures

## Commit

```powershell
git add -A
git commit -m "Step 21: reusable build and deploy workflows; fixed input naming and stale SDK version"
```
