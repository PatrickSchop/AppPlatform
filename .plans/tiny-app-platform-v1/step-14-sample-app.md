# Step 14 — Sample app (the standing regression gate)

**Phase:** 2 — Functions surface
**Depends on:** Step 13
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Build `samples/SampleApp` — a minimal but genuinely real consumer of the platform. It is the
first thing that compiles the Step 13 shims, and from here on it is the regression gate:
**every later step must leave it building, migrating and serving.**

It is also the dry run for the template in Step 18. Whatever `Program.cs` ends up looking
like here is what the template generates.

## What it contains

One entity (`Note`), one task handler (`WordCountTaskHandler`), one endpoint
(`NotesEndpoints`). Nothing more — the point is to exercise every platform seam with the
least possible app code.

## Tasks

### 1. `samples/SampleApp/SampleApp.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <AzureFunctionsVersion>v4</AzureFunctionsVersion>
    <OutputType>Exe</OutputType>
    <RootNamespace>SampleApp</RootNamespace>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Timer" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Wisdi.AppPlatform\Wisdi.AppPlatform.csproj" />
  </ItemGroup>

  <!--
    ProjectReference does not flow build/*.targets the way PackageReference does, so the
    shims are included explicitly here. A real app consuming the NuGet package gets these
    automatically. See src/Wisdi.AppPlatform.Functions/README.md.
  -->
  <ItemGroup>
    <Compile Include="..\..\src\Wisdi.AppPlatform.Functions\endpoints\*.cs" Visible="false" />
  </ItemGroup>

  <ItemGroup>
    <None Update="host.json" CopyToOutputDirectory="PreserveNewest" />
    <None Update="local.settings.json" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="Never" />
    <Content Include="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="appsettings.development.json" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="Database\Scripts\**\*.sql" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

Unlike the engine, this project **does** reference `Microsoft.Azure.Functions.Worker.Sdk` —
it is the Functions app, and it needs the metadata source generator.

### 2. `samples/SampleApp/Program.cs`

This is the file the template will generate. Keep it short; that is the deliverable.

```csharp
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SampleApp.Data;
using Wisdi.AppPlatform.Data;
using Wisdi.AppPlatform.Hosting;

namespace SampleApp;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (MigrationEntryPoint.IsMigrationRun(args))
            return await MigrationEntryPoint.RunAsync<AppDbContext>(args);

        var builder = FunctionsApplication.CreateBuilder(args);

        builder.Configuration.AddPlatformConfiguration(typeof(Program).Assembly);

        var assemblies = new PlatformAssemblies().AddContaining<Program>();
        builder.Services.AddPlatform(builder.Configuration, assemblies);
        builder.Services.AddPlatformData<AppDbContext>(builder.Configuration);

        builder.ConfigureFunctionsWebApplication().UsePlatform();

        await builder.Build().RunAsync();
        return 0;
    }
}
```

If any of those calls do not line up with what Steps 03-10 actually produced, **change the
platform, not this file**. This shape — nine lines, one generic argument — is the product
requirement. If it cannot be met, that is a design problem worth fixing now rather than
baking into the template.

### 3. `samples/SampleApp/Data/AppDbContext.cs`

```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options, PlatformAssemblies assemblies)
    : PlatformDbContext(options, assemblies)
{
    public DbSet<Note> Notes { get; set; } = null!;
}
```

### 4. `samples/SampleApp/Data/Note.cs`

```csharp
public class Note : Entity
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int? WordCount { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
```

### 5. `samples/SampleApp/Database/Scripts/100_CreateNotes.sql`

Numbered `100` — the app range. Anything under `100` must be rejected by Step 05's validation,
and Step 15 tests that.

```sql
IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[Notes]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Notes] (
        [Id]         UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [Title]      NVARCHAR(200)    NOT NULL,
        [Body]       NVARCHAR(MAX)    NOT NULL DEFAULT '',
        [WordCount]  INT              NULL,
        [CreatedUtc] DATETIME2        NOT NULL DEFAULT GETUTCDATE()
    );
END
```

### 6. `samples/SampleApp/Tasks/WordCountTaskHandler.cs`

Deliberately slow, so progress reporting and the task lifecycle are observable in the UI at
Step 15 rather than completing instantly.

```csharp
public class WordCountTaskHandler(IDbContextFactory<AppDbContext> factory, ILogger<WordCountTaskHandler> logger)
    : ITaskHandler<WordCountTaskData>
{
    public async Task HandleAsync(BackgroundTask task, WordCountTaskData data,
                                  TaskHandlerContext context, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var notes = await db.Notes.ToListAsync(ct);

        for (var i = 0; i < notes.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            notes[i].WordCount = notes[i].Body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            await Task.Delay(500, ct);
            await context.UpdateProgressAsync((i + 1) * 100 / Math.Max(notes.Count, 1));
        }

        await db.SaveChangesAsync(ct);
        await context.CompleteAsync();
    }
}

public sealed record WordCountTaskData(bool Recount = true);
```

Note it calls `UpdateProgressAsync` in the loop — which also renews the lease (Step 06). That
is the convention handlers should follow, and the sample should model it.

### 7. `samples/SampleApp/Api/NotesEndpoints.cs` and `NotesFunctions.cs`

Two files, deliberately, to show apps the same split the platform uses:

- `NotesEndpoints` — a plain service with `GetAllAsync`, `CreateAsync`, `StartWordCountAsync`
  (which calls `IBackgroundTaskService.CreateTaskAsync("wordcount", new WordCountTaskData(), "Counting words", requiresNotification: true)`)
- `NotesFunctions` — `[Function]` shims in the **app's own** compilation, all `[Authorize]`,
  routes `api/notes` (get, post) and `api/notes/wordcount` (post)

### 8. `samples/SampleApp/SampleServiceBuilder.cs`

```csharp
public sealed class SampleServiceBuilder : ServiceBuilder
{
    public override void BuildServices(IServiceCollection services, IConfiguration configuration)
        => services.AddScoped<NotesEndpoints>();

    public override void RegisterBackgroundTasks(IBackgroundTaskCollection tasks)
        => tasks.AddBackgroundTask<WordCountTaskHandler>("wordcount");
}
```

### 9. Configuration files

**`appsettings.json`** — no secrets, ever:

```json
{
  "webApp": { "api": { "root": "/api" }, "title": "Sample App" },
  "database": { "connectionString": "", "useManagedIdentity": false, "apiMigration": { "enable": false } },
  "backgroundTasks": { "maxConcurrentTasks": 4, "leaseSeconds": 300 }
}
```

**`appsettings.development.json`**:

```json
{
  "staticContent": { "files": { "rootPath": "../../../../wwwroot" } },
  "database": {
    "connectionString": "Server=(localdb)\\.;Database=SampleApp;Trusted_Connection=true;TrustServerCertificate=True",
    "useManagedIdentity": false,
    "apiMigration": { "enable": true }
  },
  "azureIdentity": {},
  "httpAccessControl": { "allowOrigin": "http://localhost:4200,http://localhost:5173" },
  "backgroundTasks": { "apiBaseUrl": "http://localhost:7071" }
}
```

**No `authentication` section** — so Step 10 resolves `Enabled` to false, the app runs
locally without Entra, and the startup warning fires. Step 15 verifies both that it runs and
that the warning appears.

**`local.settings.json`** (gitignored, but create it):

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "DEV_ENVIRONMENT": "development",
    "backgroundTasks:checkSchedule": "0 */5 * * * *"
  }
}
```

`backgroundTasks:checkSchedule` must be here, not only in `appsettings.json` — the
`%…%` syntax in a `TimerTrigger` reads **app settings** (Step 13).

**`host.json`** — port from `C:\Dev\StockAnalysis\App\host.json`, keeping
`"extensions": { "http": { "routePrefix": "" } }`. The empty route prefix is required: every
platform route already carries its own `api/` prefix, and the catch-all static route must sit
at the site root.

### 10. `samples/SampleApp/wwwroot/`

A single hand-written `index.html` — no framework. It should:
- fetch `/configuration.json` and show the title
- list notes from `/api/notes`, with a form to add one
- a button that POSTs `/api/notes/wordcount` and then polls
  `/api/tasks/notifications` every second, showing the progress bar

~100 lines of plain HTML and JavaScript. It exists so Step 15 can exercise the whole loop
without any front-end toolchain, and so Step 28 has a reference behaviour to compare the
Angular and React starters against.

Also add `wwwroot/dashboard` as **nothing at all** — the deep-link test at Step 15 navigates
to `/dashboard`, and the point is that no such file exists and `index.html` is served anyway.

### 11. `docs/background-tasks.md`

Short. Cover: defining an `ITaskHandler<T>`, registering it in a `ServiceBuilder`, creating a
task, and — importantly — **the lease obligation**: a handler running longer than
`backgroundTasks:leaseSeconds` (default 300) must call `context.UpdateProgressAsync`
periodically or its task will be reclaimed and re-run.

### 12. Add to the solution

```powershell
dotnet sln add samples\SampleApp\SampleApp.csproj
```

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

**Expected:** builds clean. This is the first compilation of the Step 13 shims, so expect to
fix typos in them here — that is the point of this step existing before Step 15.

Full runtime verification is Step 15. Do not attempt `func start` yet.

## Done when

- [ ] `dotnet build --configuration Release` is clean with zero warnings
- [ ] The Step 13 shims compile
- [ ] `Program.cs` is under ~20 lines of body
- [ ] `appsettings.json` contains no secret and no `authentication` section
- [ ] `wwwroot/index.html` exercises config, notes and task polling
- [ ] There is no `wwwroot/dashboard` file
- [ ] `docs/background-tasks.md` states the lease obligation

## Commit

```powershell
git add -A
git commit -m "Step 14: SampleApp consumer - one entity, one task handler, one endpoint"
```
