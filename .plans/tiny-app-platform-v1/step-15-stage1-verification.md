# Step 15 â€” Gate A: the seam holds

**Phase:** 2 â€” Functions surface
**Depends on:** Step 14
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Prove the extraction actually works at runtime. This is the first gate; **do not start
Phase 3 until every check below passes.**

This step writes almost no code â€” it runs things and checks results. Where a check fails,
fix the relevant earlier step and re-run the whole gate.

## Prerequisites

- `func --version` reports 4.x (Step 01)
- SQL Server LocalDB available: `sqllocaldb info` lists an instance
- Azurite or the storage emulator running, for `AzureWebJobsStorage` (the timer trigger needs
  it). `npx azurite --silent` in a separate shell is enough.

## Check 1 â€” Function metadata (analysis Â§2.1)

This is the direct test that source-injected shims are visible to worker indexing.

```powershell
cd C:\Dev\AppPlatform
dotnet build samples\SampleApp\SampleApp.csproj -c Debug
Get-Content samples\SampleApp\obj\Debug\net10.0\functions.metadata | ConvertFrom-Json |
    Select-Object name, scriptFile, entryPoint | Format-Table -AutoSize
```

**Expected â€” all of these present:**

| name | scriptFile |
|---|---|
| `GetNotificationTasks` | `SampleApp.dll` |
| `GetTasks` | `SampleApp.dll` |
| `CreateTask` | `SampleApp.dll` |
| `GetTaskStatus` | `SampleApp.dll` |
| `CheckTasks` | `SampleApp.dll` |
| `StaticContent` | `SampleApp.dll` |
| `GetWebAppConfiguration` | `SampleApp.dll` |
| `GetHealth` | `SampleApp.dll` |
| `InitializeDatabase` | `SampleApp.dll` |
| `ScheduledTaskCheck` | `SampleApp.dll` |
| `GetNotes`, `CreateNote`, `StartWordCount` | `SampleApp.dll` |

**Fails if:** any platform function is missing (the targets/`Compile Include` did not take),
or any `scriptFile` is `PS.AppPlatform.dll` (a `[Function]` leaked into the engine â€” the
Step 02 architecture test should have caught that).

Also confirm worker indexing is still on, since that is what the whole design protects:

```powershell
Get-Content samples\SampleApp\bin\Debug\net10.0\worker.config.json
```

**Expected:** `"workerIndexing": "true"`.

## Check 2 â€” Migration runs once (analysis Â§3)

```powershell
cd C:\Dev\AppPlatform\samples\SampleApp
sqllocaldb start
sqlcmd -S "(localdb)\." -Q "IF DB_ID('SampleApp') IS NULL CREATE DATABASE [SampleApp];"

$env:DEV_ENVIRONMENT = "development"
cd bin\Debug\net10.0
dotnet exec SampleApp.dll --migrate
dotnet exec SampleApp.dll --migrate
```

**Expected:**
- First run: `Applied migrations: 010_CreateBackgroundTasks.sql, 100_CreateNotes.sql`
  (`000_CreateSchemaVersions.sql` is the untracked bootstrap and is not listed)
- **Second run: an empty applied list** and exit code 0. This is the `__SchemaVersions` proof.

Then confirm the Â§3 drift bug is gone â€” the table must have every column from one script:

```powershell
sqlcmd -S "(localdb)\." -d SampleApp -Q "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.BackgroundTasks') ORDER BY name;"
sqlcmd -S "(localdb)\." -d SampleApp -Q "SELECT ScriptName FROM dbo.__SchemaVersions ORDER BY ScriptName;"
```

**Expected columns include:** `StatusMessage`, `ExecutionManagerId`, `LeaseExpiresUtc`.

## Check 3 â€” Script numbering is enforced

```powershell
Copy-Item samples\SampleApp\Database\Scripts\100_CreateNotes.sql `
          samples\SampleApp\bin\Debug\net10.0\Database\Scripts\090_Bad.sql
cd samples\SampleApp\bin\Debug\net10.0
dotnet exec SampleApp.dll --migrate
```

**Expected:** non-zero exit, and a message naming `090_Bad.sql` and the `100+` rule.
**Then delete `090_Bad.sql`** and re-run to confirm it is clean again.

## Check 4 â€” The app runs and serves the SPA

```powershell
cd C:\Dev\AppPlatform\samples\SampleApp
func start
```

**Expected in the startup log:**
- every function from Check 1 listed with its route
- the Step 10 warning: `Platform authentication is DISABLED...`
- exactly **one** `TaskExecutionManager initialized with ID ...` line

That last one is the Â§7.1 fix visible at runtime. If it appears once per request, the
`ExecutionManagerIdentity` singleton is not wired correctly.

Then, in another shell:

```powershell
curl.exe -i http://localhost:7071/api/health
curl.exe -i http://localhost:7071/configuration.json
curl.exe -i http://localhost:7071/
curl.exe -i http://localhost:7071/dashboard
curl.exe -i http://localhost:7071/assets/definitely-missing.png
```

**Expected:**
| Request | Result |
|---|---|
| `/api/health` | 200, `{"status":"ok"...}` |
| `/configuration.json` | 200, the `webApp` section including `"title":"Sample App"` |
| `/` | 200, `index.html`, `Cache-Control: no-cache` |
| **`/dashboard`** | **200 and `index.html`** â€” the Â§4 deep-link fix |
| `/assets/definitely-missing.png` | 404 â€” extensions do not fall back |

Also confirm routing precedence: `/api/health` returned JSON, not `index.html`. If the
catch-all swallowed it, the `routePrefix: ""` setting or route ordering is wrong.

Check caching and revalidation on a real asset:

```powershell
curl.exe -i http://localhost:7071/index.html
# take the ETag value from the response, then:
curl.exe -i -H 'If-None-Match: "<etag>"' http://localhost:7071/index.html
```

**Expected:** 200 then **304**.

## Check 5 â€” The background task lifecycle

Open `http://localhost:7071/` in a browser.

1. Add two or three notes through the form.
2. Click the word-count button.
3. **Expected:** the progress indicator appears and advances, and the task reaches
   `Completed`. The notes now show word counts.

Confirm the lease was actually being renewed while it ran â€” during execution:

```powershell
sqlcmd -S "(localdb)\." -d SampleApp -Q "SELECT Status, CompletionPercentage, ExecutionManagerId, LeaseExpiresUtc FROM dbo.BackgroundTasks;"
```

**Expected while running:** `Status = 9`, a non-null `ExecutionManagerId`, and a
`LeaseExpiresUtc` that **moves forward** between two queries.
**Expected after completion:** `Status = 32`, `ExecutionManagerId` and `LeaseExpiresUtc`
both null.

## Check 6 â€” Orphan recovery (analysis Â§7.2)

```powershell
# With the app stopped, fake a task abandoned by a dead worker:
sqlcmd -S "(localdb)\." -d SampleApp -Q @"
INSERT INTO dbo.BackgroundTasks (Id, TaskType, Status, TaskData, Description, RequiresNotification, LeaseExpiresUtc, ExecutionManagerId)
VALUES (NEWID(), 'wordcount', 9, '{}', 'orphan', 1, DATEADD(minute, -10, GETUTCDATE()), NEWID());
"@
```

Start the app and either wait for the timer or `curl.exe -X POST http://localhost:7071/api/tasks/check`.

**Expected:** the orphan is reclaimed â€” `StatusMessage` mentions the reclaim, and it then
runs to `Completed`. Without the Â§7.2 fix it would sit at `Status = 9` forever.

## Check 7 â€” The concurrency cap (analysis Â§7.1)

Set `backgroundTasks:maxConcurrentTasks` to `1` in `appsettings.development.json`, restart,
and create three word-count tasks quickly.

**Expected:** exactly one is `Running` (`Status = 9`) at a time; the others wait at
`Status = 1` and run in sequence. Before the Â§7.1 fix all three would start at once.

Restore the value to `4` afterwards.

## Check 8 â€” Default-deny authorization (analysis Â§6)

Auth is disabled in the sample's config, so enable it temporarily. Add to
`appsettings.development.json`:

```json
"authentication": {
  "enabled": true,
  "azureEntraId": { "tenantId": "<a real tenant guid>", "clientId": "<a different guid>" }
}
```

Restart and:

```powershell
curl.exe -i http://localhost:7071/api/tasks/notifications
curl.exe -i http://localhost:7071/api/health
curl.exe -i http://localhost:7071/configuration.json
curl.exe -i -X OPTIONS -H "Origin: http://localhost:4200" http://localhost:7071/api/tasks/notifications
```

**Expected:**
| Request | Result |
|---|---|
| `/api/tasks/notifications` with no token | **401** with `WWW-Authenticate: Bearer` |
| `/api/health` | 200 â€” `[AllowAnonymous]` |
| `/configuration.json` | 200 â€” `[AllowAnonymous]` |
| `OPTIONS` preflight | 204 with `Access-Control-Allow-Origin`, **without** reaching auth |

And the CORS-ordering fix:

```powershell
curl.exe -i -H "Origin: http://localhost:4200" http://localhost:7071/api/tasks/notifications
```

**Expected:** 401 **carrying `Access-Control-Allow-Origin: http://localhost:4200`**. A 401
with no CORS header means the ordering regressed to the source's arrangement.

Then set `tenantId` equal to `clientId` and restart.
**Expected:** startup fails with a message naming both keys â€” the Â§6 copy-paste guard.

**Confirm the security model** â€” the SPA is public, the API is not. With auth still enabled,
browse to `http://localhost:7071/` in a normal browser tab with no token anywhere:

```powershell
curl.exe -i http://localhost:7071/
curl.exe -i http://localhost:7071/dashboard
curl.exe -i http://localhost:7071/api/notes
```

**Expected:**
| Request | Result |
|---|---|
| `/` | **200, `index.html`** â€” the shell is `[AllowAnonymous]` |
| `/dashboard` | **200, `index.html`** â€” deep links are public too |
| `/api/notes` | **401** â€” the data is not |

The shell loading while every data endpoint refuses is exactly the intended arrangement, not
a gap. If `/` returns 401, `StaticContentFunctions` still carries `[Authorize]` and Step 13
was not applied.

Then prove the boundary actually holds â€” walk the whole route table with no token and confirm
**nothing returns data**:

```powershell
@('/api/notes','/api/tasks','/api/tasks/notifications','/api/tasks/check','/api/initializeDatabase') |
  ForEach-Object {
    $r = curl.exe -s -o NUL -w "%{http_code}" "http://localhost:7071$_"
    "{0,-32} {1}" -f $_, $r
  }
```

**Expected:** every line is `401`. Any `200` here is a real vulnerability, not a cosmetic
issue â€” that is the one check in this gate where a failure blocks everything downstream.

Remove the temporary `authentication` block when done.

## Check 9 â€” Full suite still green

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

## Gate A checklist

- [ ] All platform functions in `functions.metadata` with `"scriptFile": "SampleApp.dll"`
- [ ] `worker.config.json` still has `"workerIndexing": "true"`
- [ ] Second `--migrate` applies nothing
- [ ] `BackgroundTasks` has `StatusMessage`, `ExecutionManagerId`, `LeaseExpiresUtc`
- [ ] An out-of-range script number fails the migration run
- [ ] `/dashboard` serves `index.html`; `/assets/missing.png` is 404
- [ ] `If-None-Match` yields 304
- [ ] `TaskExecutionManager initialized` logs once per process
- [ ] A task runs to completion with visible progress; the lease moves forward
- [ ] An orphaned task is reclaimed and completes
- [ ] `maxConcurrentTasks = 1` actually serialises three tasks
- [ ] Unauthenticated `/api/*` is 401; `/api/health` and `/configuration.json` are 200
- [ ] A 401 carries `Access-Control-Allow-Origin`
- [ ] `tenantId == clientId` fails at startup
- [ ] `dotnet test` green

## Record the result

Write `docs/gate-a-results.md` with the date, the outcome of each check, and anything that
had to be fixed to get here. Steps 19 and 28 do the same; together they are the evidence that
the platform works, which matters more than any of the prose documentation.

## Commit

```powershell
git add -A
git commit -m "Step 15: Gate A passed - shims visible, migrations tracked, deep links, tasks, auth"
```

