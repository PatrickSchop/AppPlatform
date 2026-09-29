# Step 19 — Gate B: the exercise pays off

**Phase:** 4 — Template and infrastructure
**Depends on:** Step 18, Step 20 (bicep is needed for the deploy half)
**Working directory:** a scratch folder, **not** the platform repo

## Goal

The real test of the whole exercise (analysis §8): build a working app from
`dotnet new tinyapp` with **zero files copied** from StockAnalysis or SampleApp, and deploy it.

**Target: under an hour from `dotnet new` to a deployed app.** Time it, honestly, and record
the result. If it takes three hours, the template is not finished — that is useful
information, not a failure to hide.

## Ordering note

This step needs Step 20's bicep for the deploy half. Either run Step 20 first, or run Checks
1-6 here now and Check 7 after Step 20. Do not skip Check 7 — "it builds locally" is a much
weaker claim than "it deployed".

## Rules

- **Nothing may be copied.** No file from `C:\Dev\StockAnalysis` or
  `C:\Dev\AppPlatform\samples`. If you find yourself reaching for one, the template is
  missing something — fix the template, note it, and restart the timer.
- Work entirely from the generated `README.md` and `docs/`. If the docs are wrong, that is a
  finding.

## Check 1 — Scaffold

```powershell
$scratch = "C:\Dev\ScratchApp"
Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $scratch | Out-Null
cd $scratch

# Start the clock.
$start = Get-Date

dotnet new tinyapp -n ScratchApp --PlatformVersion 0.1.0 --AppRole scratch.user
cd ScratchApp
dotnet build
```

**Expected:** restores from GitHub Packages and builds clean.

If restore fails on authentication, follow `docs/consuming-packages.md` exactly as written.
Any correction needed there is a Step 17 defect — fix that doc.

## Check 2 — Functions are indexed

```powershell
Get-Content obj\Debug\net10.0\functions.metadata | ConvertFrom-Json |
    Select-Object name, scriptFile | Format-Table -AutoSize
```

**Expected:** the platform functions, all with `"scriptFile": "ScratchApp.dll"`, injected via
the package targets with no `<Compile Include>` in the csproj.

This is Gate A's Check 1 reproduced through the fully packaged path. If it passes here, §2.1
is solved for real.

## Check 3 — Add one entity

Following only the generated README:

`Data/Recipe.cs`:
```csharp
public class Recipe : Entity
{
    public string Name { get; set; } = string.Empty;
    public int Servings { get; set; }
}
```

Add `public DbSet<Recipe> Recipes { get; set; } = null!;` to `AppDbContext`, and a
`CREATE TABLE` to `Database/Scripts/100_InitialSchema.sql`.

```powershell
sqlcmd -S "(localdb)\." -Q "IF DB_ID('ScratchApp') IS NULL CREATE DATABASE [ScratchApp];"
$env:DEV_ENVIRONMENT = "development"
dotnet build
cd bin\Debug\net10.0
dotnet exec ScratchApp.dll --migrate
dotnet exec ScratchApp.dll --migrate
```

**Expected:** first run applies the core script plus `100_InitialSchema.sql`; second run
applies nothing.

## Check 4 — Add one task handler

`Tasks/RescaleTaskHandler.cs` — an `ITaskHandler<RescaleTaskData>` that doubles every
recipe's `Servings`, reporting progress. Register it in `AppServiceBuilder.RegisterBackgroundTasks`.

**Expected:** builds with no additional wiring beyond the one registration line.

## Check 5 — Add one endpoint

`Api/RecipeEndpoints.cs` (plain service) plus `Api/RecipeFunctions.cs` (`[Function]` shims,
`[Authorize]`), routes `api/recipes` get/post. Register the service in
`AppServiceBuilder.BuildServices`.

## Check 6 — Run it

```powershell
cd $scratch\ScratchApp
func start
```

```powershell
curl.exe -i http://localhost:7071/api/health
curl.exe -i http://localhost:7071/configuration.json
curl.exe -i -X POST -H "Content-Type: application/json" -d '{\"name\":\"Soup\",\"servings\":2}' http://localhost:7071/api/recipes
curl.exe -i http://localhost:7071/api/recipes
curl.exe -i -X POST -H "Content-Type: application/json" -d '{\"taskType\":\"rescale\",\"taskData\":{},\"description\":\"Rescale\",\"requiresNotification\":true}' http://localhost:7071/api/tasks
curl.exe -i http://localhost:7071/api/tasks/notifications
```

**Expected:** health 200; config returns the `webApp` section; the recipe round-trips; the
task is created and reaches `Completed`; `Servings` is now 4.

Note the app has no `wwwroot` yet, so static routes 404 — correct for a backend-only scaffold.

## Check 7 — Deploy (needs Step 20)

**Precondition added 2026-09-29:** the platform packages must be at a version that contains
the authorization fixes (see container plan §5). A build from `0.1.0` enforces nothing.
Publish `0.1.1`, point the app at it, and deploy that — otherwise this check passes against
a build whose `/api/*` routes are wide open.

Each app gets its own resource group (Azure was reorganized post-Step-20: apps no longer share
`Applications` — the SQL server alone lives in `ApplicationsShared`, and every app, including
this one, gets its own per-app resource group). Create it first:

```powershell
az group create --name ScratchApp --location westeurope

az deployment group create `
  --resource-group ScratchApp `
  --template-file C:\Dev\AppPlatform\infra\app.bicep `
  --parameters appName=scratchapp
```

Then follow the generated `deploy.yaml`: push to a GitHub repo, let the workflow run.

**Expected:**
- the bicep creates the database, function app, blob container and managed identity
- the workflow builds, deploys, and runs `--migrate` against the scratch database
- `https://scratchapp-api.azurewebsites.net/api/health` returns 200
- creating a task through the deployed API runs it to completion

**Confirm Easy Auth is off on the deployed app.** The platform authenticates in-process and
App Service Authentication would intercept requests before any platform code runs
(`docs/auth-setup.md` Part 4a). Step 20's bicep asserts this, so this check proves the
assertion took effect:

```powershell
az webapp auth show --resource-group ScratchApp --name scratchapp-api `
  --query "{enabled:enabled, action:unauthenticatedClientAction}" -o json

# And the behavioural check, which is the one that actually matters:
curl.exe -s -o NUL -w "%{http_code}`n" https://scratchapp-api.azurewebsites.net/api/health
curl.exe -s -o NUL -w "%{http_code}`n" https://scratchapp-api.azurewebsites.net/api/recipes
curl.exe -s https://scratchapp-api.azurewebsites.net/configuration.json | Select-Object -First 1
```

**Expected:** `enabled: false`; health **200**; recipes **401**; `configuration.json` returns
**JSON**.

**If health or configuration.json returns 302**, Easy Auth is on — the bicep assertion did not
apply. Fix it before continuing; every later check would be testing the wrong thing.
`configuration.json` returning HTML rather than JSON is the same failure wearing a disguise,
and it is what the SPA would hit first.

**Stop the clock.**

```powershell
"Elapsed: {0:hh\:mm\:ss}" -f ((Get-Date) - $start)
```

## Check 8 — Auth end to end

**Amended by the container plan §2(4): this app uses no App Role.** The authorization
model is authentication-only so that any Microsoft account can sign in, so there is no
`scratch.user` role to create and no 403 to observe.

Following `docs/auth-setup.md` (Step 22, Part 2 steps 3-4 only), set the Function App
settings to the **API** registration's client id with `tenantId` = `common`:

```powershell
az functionapp config appsettings set `
  --resource-group ScratchApp --name scratchapp-api --settings `
    "authentication__azureEntraId__tenantId=common" `
    "authentication__azureEntraId__clientId=<API app client id>" `
    "authentication__azureEntraId__additionalAudiences__0=api://<API app client id>" `
    "authentication__requiredRole="
```

Acquire a token for the API and call a protected route:

```powershell
$token = az account get-access-token --resource "api://<API app client id>" --query accessToken -o tsv
curl.exe -s -o NUL -w "%{http_code}`n" https://scratchapp-api.azurewebsites.net/api/recipes
curl.exe -s -o NUL -w "%{http_code}`n" -H "Authorization: Bearer $token" https://scratchapp-api.azurewebsites.net/api/recipes
```

**Expected:**
- no token → **401**
- malformed token → **401** (not 500)
- valid token → **200**

The 401-vs-200 distinction is the proof that authentication is enforced. If you also want to
prove the role check works, set `requiredRole` to a role you have *not* been assigned and
confirm the same valid token then yields **403**; this is optional for this app but it is the
only way to exercise that branch.

## Gate B checklist

- [ ] `dotnet new tinyapp` scaffolds and builds against the **published** packages
- [ ] Platform functions indexed with `"scriptFile": "ScratchApp.dll"` via targets injection
- [ ] One entity, one task handler, one endpoint added following only the generated README
- [ ] Migrations apply once and are idempotent
- [ ] The task completes and mutates data
- [ ] `az deployment group create` produces the full per-app footprint
- [ ] The deploy workflow succeeds and the deployed health endpoint is 200
- [ ] Easy Auth is off on the deployed app: no 302s, and `/configuration.json` returns JSON
- [ ] Auth yields 401 without a token and 200 with one (403 only if `requiredRole` is set)
- [ ] **Zero files copied from StockAnalysis or SampleApp**
- [ ] Elapsed time recorded

## Record the result

Write `docs/gate-b-results.md`: the elapsed time, every friction point, and every template or
doc defect found. Then **fix the defects** and note the fixes. A list of problems that were
not acted on is worth very little.

If the elapsed time is well over an hour, say so plainly and list what consumed it. That list
is the roadmap for v1.1.

## Clean up

Delete the scratch Azure resources so they do not accrue cost:

```powershell
# The database is the one resource outside the app's own resource group.
az sql db delete --resource-group ApplicationsShared --server pschop-db --name scratchapp --yes
az group delete --name ScratchApp --yes
```

## Commit

```powershell
cd C:\Dev\AppPlatform
git add -A
git commit -m "Step 19: Gate B passed - app built from template with nothing copied"
```
