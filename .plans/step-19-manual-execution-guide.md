# Step 19 Manual Execution Guide — Check 7 & 8

**Precondition:** Steps 1-18 complete (platform, template, bicep infrastructure)

This guide covers the manual steps required to complete Step 19 Checks 7-8 when automated execution is not feasible.

---

## Prerequisites

### 1. GitHub PAT for NuGet Authentication

The template consumes `PS.AppPlatform` packages from GitHub Packages, which requires authentication.

**Get a classic Personal Access Token:**

1. Go to https://github.com/settings/tokens
2. Click "Generate new token (classic)"
3. Name it: `NuGet-PS-AppPlatform`
4. Grant scope: `read:packages` (that's all that's needed)
5. Click "Generate" and copy the token immediately
6. **Store it securely** — you'll paste it in the next step

**Configure NuGet credentials (one-time on this machine):**

```powershell
$token = "ghp_XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX"  # Your classic PAT from above

dotnet nuget update source PS `
  --username PatrickSchop `
  --password $token `
  --store-password-in-clear-text `
  --configfile $env:APPDATA\NuGet\NuGet.Config
```

This stores the token in `%APPDATA%\NuGet\NuGet.Config` (required on Windows; GitHub Packages limitation).

**Verify the configuration:**

```powershell
Get-Content $env:APPDATA\NuGet\NuGet.Config | Select-String -Pattern "PatrickSchop|PS"
```

Expected: entries for username `PatrickSchop` and source `PS`.

### 2. GitHub Repo for ScratchApp

You need a GitHub repository to push ScratchApp for the deploy workflow to test.

**Create a public GitHub repo:**

1. Go to https://github.com/new
2. Name: `ScratchApp`
3. Description: "Tiny app platform test — generated from `dotnet new tinyapp`"
4. **Public** (the workflow will read this, so it can't be private without additional setup)
5. Initialize with no README (you'll add the scaffolded content)
6. Create the repo

**Record your repo URL:**

```
https://github.com/YOUR_USERNAME/ScratchApp
```

### 3. Local Prerequisites

Verify these are installed and working:

```powershell
# Azure CLI
az --version

# Azure Functions Core Tools
func --version

# .NET SDK
dotnet --version

# Git
git --version
```

---

## Check 7 — Deploy ScratchApp to Azure

### Step 1: Scaffold the app

```powershell
$scratch = "C:\Dev\ScratchApp"
Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $scratch | Out-Null
cd $scratch

$start = Get-Date
Write-Host "Timer started: $(Get-Date -Format 'HH:mm:ss')"

dotnet new tinyapp -n ScratchApp -pv 0.1.0 -ar scratch.user -aa scratchapp
cd ScratchApp
dotnet build

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed"
    exit 1
}
Write-Host "✅ Scaffold successful"
```

**Expected:** Restores from GitHub Packages (using your new PAT), builds clean.

**If restore fails:**
- Check `dotnet nuget source list` — PS source should be present
- Verify the PAT has `read:packages` scope
- Try: `dotnet restore --verbosity diagnostic` for details

### Step 2: Verify functions are indexed

```powershell
Get-Content obj\Debug\net10.0\functions.metadata | ConvertFrom-Json |
    Select-Object name, scriptFile | Format-Table -AutoSize
```

**Expected output:**

```
name                      scriptFile
----                      ----------
GetNotificationTasks      ScratchApp.dll
GetTasks                  ScratchApp.dll
CheckTasks                ScratchApp.dll
StaticContent             ScratchApp.dll
GetWebAppConfiguration    ScratchApp.dll
GetRecipes                ScratchApp.dll
CreateRecipe              ScratchApp.dll
```

All platform functions plus the recipe endpoint shims.

### Step 3: Test locally

```powershell
# Terminal 1: Start the functions host
func start
```

**In another terminal, test the endpoints:**

```powershell
# These should work (anonymous):
curl.exe -i http://localhost:7071/api/health
curl.exe -i http://localhost:7071/configuration.json

# These should return 401 (auth required, not yet set up):
curl.exe -i http://localhost:7071/api/recipes
curl.exe -i -X POST -H "Content-Type: application/json" `
  -d '{"name":"Soup","servings":2}' http://localhost:7071/api/recipes
```

**Expected:**
- `/api/health` → **200 OK**
- `/configuration.json` → **200 OK** with JSON
- `/api/recipes` GET/POST → **401 Unauthorized** (no bearer token)

When satisfied, stop `func start` (Ctrl+C).

### Step 4: Initialize git repo and push to GitHub

```powershell
cd C:\Dev\ScratchApp\ScratchApp

# Initialize repo
git init
git config user.email "you@example.com"
git config user.name "Your Name"
git add -A
git commit -m "Initial commit: scaffolded from dotnet new tinyapp"

# Add remote
git remote add origin https://github.com/YOUR_USERNAME/ScratchApp.git
git branch -M main
git push -u origin main
```

**Expected:** All files push successfully. Repo on GitHub now has the scaffolded app.

### Step 5: Create Azure resource group

```powershell
az group create --name ScratchApp --location westeurope
```

**Expected:** Resource group created. Record the location if you want to use a different Azure region.

### Step 6: Deploy infrastructure with bicep

```powershell
az deployment group create `
  --resource-group ScratchApp `
  --template-file C:\Dev\AppPlatform\infra\app.bicep `
  --parameters appName=scratchapp
```

**Expected:** Deployment completes. Creates:
- Azure SQL database (in shared `ApplicationsShared` resource group)
- Function App `scratchapp-api`
- Storage account
- Managed identity
- App Service plan

**If it fails:**
- Check `az group show --name ScratchApp`
- Check `az deployment group show --name app --resource-group ScratchApp` for error details

### Step 7: Verify Easy Auth is disabled

```powershell
az webapp auth show --resource-group ScratchApp --name scratchapp-api `
  --query "{enabled:enabled, action:unauthenticatedClientAction}" -o json
```

**Expected:** `"enabled": false` (or `null` if auth settings don't exist)

**If enabled:**

```powershell
az webapp auth update --resource-group ScratchApp --name scratchapp-api --enabled false
```

### Step 8: Deploy the app via GitHub Actions

**Set up the deploy secret in GitHub (one-time):**

1. Go to https://github.com/YOUR_USERNAME/ScratchApp/settings/secrets/actions
2. Click "New repository secret"
3. Name: `AZURE_PUBLISH_PROFILE`
4. Get the value:

```powershell
az webapp deployment list-publishing-credentials `
  --resource-group ScratchApp `
  --name scratchapp-api `
  --query "{publishProfile: publishingProfile}" -o tsv | Set-Clipboard
```

(The publish profile is now in your clipboard; paste it into the GitHub secret)

5. Click "Add secret"

**Trigger the deploy workflow:**

1. Go to https://github.com/YOUR_USERNAME/ScratchApp/actions
2. Click the "deploy" workflow
3. Click "Run workflow" → "Run workflow"
4. Wait for it to complete (usually ~2-3 minutes)

**If the workflow fails:**
- Click the failed run
- Check the logs under "deploy" step
- Common issues:
  - Publish profile not set or malformed
  - Resource group name mismatch
  - App name doesn't match bicep parameter

### Step 9: Verify the deployed app

```powershell
# Check Easy Auth again (should still be off)
az webapp auth show --resource-group ScratchApp --name scratchapp-api `
  --query "{enabled:enabled, action:unauthenticatedClientAction}" -o json

# Test the endpoints
$api = "https://scratchapp-api.azurewebsites.net"

# Should return 200 (anonymous)
curl.exe -i "$api/api/health"
curl.exe -i "$api/configuration.json"

# Should return 401 (not yet authenticated)
curl.exe -i "$api/api/recipes"
```

**Expected:**
- Easy Auth: `"enabled": false`
- `/api/health` → **200 OK**
- `/configuration.json` → **200 OK** with JSON body
- `/api/recipes` → **401 Unauthorized** (correct; auth required but not set up yet)

**If `/configuration.json` returns HTML instead of JSON or status 302, Easy Auth is enabled** — go back to Step 7.

### Step 10: Stop the clock

```powershell
$elapsed = (Get-Date) - $start
"Elapsed time: {0:hh\:mm\:ss}" -f $elapsed
```

Record this time.

---

## Check 8 — Auth End-to-End

This check verifies that Entra role-based access control (RBAC) works correctly.

### Prerequisites for Check 8

You need:
1. An Azure tenant with admin access (to create app registrations)
2. Your own user account in that tenant
3. Step 22 docs (`docs/auth-setup.md` in the platform repo)

### Step 1: Create Entra app registrations (one-time)

Follow [Step 22: Part 1](../tiny-app-platform-v1/step-22-entra-auth-runbook.md) in the plan to:

1. **Create the API app registration:**
   - Name: `PS Apps API`
   - Set Application ID URI to `api://<api-client-id>`
   - Add scope `access_as_user`

2. **Create the SPA app registration:**
   - Name: `PS Apps SPA`
   - Redirect URIs: `http://localhost:4200`, `http://localhost:5173`, `https://scratchapp-api.azurewebsites.net`
   - Grant API permissions to `PS Apps API`

**Record:**
- Tenant ID (Azure portal → Azure Active Directory → Properties → Tenant ID)
- API app client ID
- SPA app client ID

### Step 2: Create the `scratch.user` App Role

Follow [Step 22: Part 2](../tiny-app-platform-v1/step-22-entra-auth-runbook.md):

1. Go to Azure portal → Azure Active Directory → App registrations → `PS Apps API`
2. Click "App roles" → "Create app role"
   - Display name: `Scratch user`
   - Value: `scratch.user` (this must match what's in `appsettings.json`)
   - Allowed member types: Users, Groups
   - Click "Apply"

3. Assign the role to yourself:
   - Go to "Enterprise applications" → `PS Apps API`
   - Click "Users and groups" → "Add user/group"
   - Select your user account
   - Select the `Scratch user` role
   - Click "Assign"

### Step 3: Configure ScratchApp with Entra credentials

```powershell
# Edit appsettings.json locally
cd C:\Dev\ScratchApp\ScratchApp

# Replace the placeholder values with your real credentials
# Open appsettings.json and update:
# - tenantId: <your tenant ID>
# - clientId: <API app client ID>
# - requiredRole: scratch.user (already set by template)

# Example:
# "authentication": {
#   "azureEntraId": {
#     "tenantId": "12345678-1234-1234-1234-123456789012",
#     "clientId": "abcdefgh-abcd-abcd-abcd-abcdefghijkl",
#     "additionalAudiences": [ "api://abcdefgh-abcd-abcd-abcd-abcdefghijkl" ]
#   },
#   "requiredRole": "scratch.user"
# }
```

### Step 4: Get an Entra token

Use the Azure CLI to get a token for testing:

```powershell
$token = az account get-access-token `
  --resource "api://abcdefgh-abcd-abcd-abcd-abcdefghijkl" `
  --query "accessToken" -o tsv

Write-Host "Token (first 50 chars): $($token.Substring(0,50))..."
```

**Note:** This gets a token with your user's roles attached.

### Step 5: Test the 401 / 403 / 200 flow

```powershell
$api = "https://scratchapp-api.azurewebsites.net"

# Test 1: No token → 401
Write-Host "Test 1: No token"
curl.exe -i "$api/api/recipes"
# Expected: 401 Unauthorized

# Test 2: Invalid token → 401 or 403 (depending on token validity)
Write-Host "`nTest 2: Random invalid token"
curl.exe -i -H "Authorization: Bearer invalid.token.here" "$api/api/recipes"
# Expected: 401 Unauthorized

# Test 3: Valid token WITH scratch.user role → 200
Write-Host "`nTest 3: Valid token with scratch.user role"
curl.exe -i -H "Authorization: Bearer $token" "$api/api/recipes"
# Expected: 200 OK (and returns empty recipe list)
```

**Expected results:**
- No token → **401 Unauthorized**
- Invalid token → **401 Unauthorized**
- Valid token with `scratch.user` → **200 OK** (returns `[]` for empty recipe list)

The 401 vs 200 distinction proves that role-based access control is working.

### Step 6: Create a recipe via authenticated API

```powershell
$api = "https://scratchapp-api.azurewebsites.net"
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

$body = @{
    name = "Test Recipe"
    servings = 4
} | ConvertTo-Json

curl.exe -i -X POST `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  -d $body `
  "$api/api/recipes"

# Verify it was created
curl.exe -i -H "Authorization: Bearer $token" "$api/api/recipes"
```

**Expected:** Recipe is created and returned in the list.

---

## Cleanup

When finished testing, delete Azure resources to avoid ongoing costs:

```powershell
# Delete the app's resource group
az group delete --name ScratchApp --yes

# Delete the database (it's in the shared group)
az sql db delete `
  --resource-group ApplicationsShared `
  --server pschop-db `
  --name scratchapp `
  --yes
```

---

## Gate B Checklist

- [ ] NuGet authentication configured (`%APPDATA%\NuGet\NuGet.Config` has credentials)
- [ ] ScratchApp scaffolds and builds against published packages
- [ ] Platform functions indexed with `"scriptFile": "ScratchApp.dll"` via targets injection
- [ ] Local testing passes (health 200, config JSON, auth 401)
- [ ] GitHub repo created and pushed
- [ ] Azure bicep deployment succeeded
- [ ] Easy Auth verified disabled on deployed app
- [ ] Deployed health endpoint returns 200
- [ ] `/configuration.json` returns JSON (not HTML or 302)
- [ ] Entra app registrations created (one-time)
- [ ] `scratch.user` App Role created and assigned to your user
- [ ] Auth 401 / 200 flow verified with real token

---

## Common Issues

| Symptom | Cause | Fix |
|---------|-------|-----|
| `Package not found` on restore | GitHub PAT not configured or lacks `read:packages` scope | Check `%APPDATA%\NuGet\NuGet.Config`; regenerate PAT with correct scope |
| `/api/recipes` returns 302 instead of 401/200 | Easy Auth is enabled on the deployed app | Run `az webapp auth update --resource-group ScratchApp --name scratchapp-api --enabled false` |
| `/configuration.json` returns HTML | Easy Auth is enabled | Same fix as above |
| Deploy workflow fails | Publish profile not set or malformed | Get fresh publish profile with `az webapp deployment list-publishing-credentials` |
| Token validation fails (all 401s even with valid token) | `clientId` and `tenantId` are swapped or missing from config | Check `appsettings.json`; verify tenant/client IDs match your Entra app registrations |
| Role not in token | App Role created on wrong app registration (should be on **API** not SPA) | Delete role from SPA, create it on API registration instead |

---

## Next Steps

Once Check 8 passes, proceed to Phase 5 (Steps 23-28) — TypeScript clients and front-end starters.

Record results in `docs/gate-b-results.md`:
- Elapsed time
- Any friction points encountered
- Any template or doc defects found (and fixes applied)

Example:

```markdown
# Gate B Results

**Elapsed time:** 1 hour 15 minutes

**Friction points:**
- NuGet authentication required GitHub PAT (not documented in initial scaffold message)
- Azure Functions deployment took longer than expected (~3 minutes)

**Template defects found and fixed:**
- None

**Docs improvements:**
- Add NuGet PAT setup to consuming-packages.md template instructions
```

Then commit and push:

```powershell
cd C:\Dev\AppPlatform
git add docs/gate-b-results.md
git commit -m "Step 19: Gate B complete - manual deployment verification"
git push
```
