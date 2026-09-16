# Step 05 — Migrations with version tracking

**Phase:** 1 — Core engine
**Depends on:** Step 04
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Rebuild `DatabaseMigrator` so it runs each script **once**, merges core scripts shipped as
embedded resources in the package with app scripts on disk, and has a well-defined order
across the package boundary.

## The problems being solved (analysis §3)

The source migrator (`App\Database\DatabaseMigrator.cs`) reads `*.sql` from a folder and
runs **every script on every call**, relying on hand-written `IF NOT EXISTS` guards, with
no tracking table. That has a latent drift bug already: `004` creates `BackgroundTasks`
without the `StatusMessage`/`ExecutionManagerId` columns that `005`/`006` add, so a fresh
database is only correct because all three always run together. Split them across a package
boundary and that breaks.

Two fixes:
1. A `__SchemaVersions` tracking table, so scripts run once.
2. Core scripts become embedded resources, numbered `000-099`; app scripts stay on disk,
   numbered `100+`. Core always runs first.

## Tasks

### 1. Create the core scripts

Under `src/Wisdi.AppPlatform/Data/Scripts/`:

**`000_CreateSchemaVersions.sql`** — the bootstrap. Runs before tracking exists, so it must
be idempotent on its own and is never recorded in the table:

```sql
IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[__SchemaVersions]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[__SchemaVersions] (
        [ScriptName]  NVARCHAR(255) NOT NULL PRIMARY KEY,
        [AppliedUtc]  DATETIME2     NOT NULL CONSTRAINT [DF___SchemaVersions_AppliedUtc] DEFAULT GETUTCDATE(),
        [Checksum]    NVARCHAR(64)  NULL
    );
END
```

**`010_CreateBackgroundTasks.sql`** — the consolidation of source `004`+`005`+`006`, plus
the new lease column from Step 08. One `CREATE TABLE` with every column present:

```sql
IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[BackgroundTasks]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[BackgroundTasks] (
        [Id]                    UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [TaskType]              NVARCHAR(100)    NOT NULL,
        [Status]                INT              NOT NULL DEFAULT 0,
        [StatusMessage]         NVARCHAR(MAX)    NOT NULL DEFAULT '',
        [CompletionPercentage]  INT              NOT NULL DEFAULT 0,
        [Description]           NVARCHAR(MAX)    NOT NULL DEFAULT '',
        [RequiresNotification]  BIT              NOT NULL DEFAULT 0,
        [TaskData]              NVARCHAR(MAX)    NOT NULL DEFAULT '',
        [CreatedDate]           DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedDate]           DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        [StartedDate]           DATETIME2        NULL,
        [CompletedDate]         DATETIME2        NULL,
        [ExecutionManagerId]    UNIQUEIDENTIFIER NULL,
        [LeaseExpiresUtc]       DATETIME2        NULL
    );

    CREATE INDEX [IX_BackgroundTasks_Status] ON [dbo].[BackgroundTasks] ([Status]);
    CREATE INDEX [IX_BackgroundTasks_TaskType] ON [dbo].[BackgroundTasks] ([TaskType]);
    CREATE INDEX [IX_BackgroundTasks_CreatedDate] ON [dbo].[BackgroundTasks] ([CreatedDate]);
    CREATE INDEX [IX_BackgroundTasks_ExecutionManagerId_Status]
        ON [dbo].[BackgroundTasks] ([ExecutionManagerId], [Status]);
    CREATE INDEX [IX_BackgroundTasks_Lease]
        ON [dbo].[BackgroundTasks] ([Status], [LeaseExpiresUtc]);
END
```

`TaskType` is widened from the source's `NVARCHAR(50)` to `NVARCHAR(100)`; 50 is tight for
a namespaced task name and widening later is a migration nobody wants to write.

Keep the `IF NOT EXISTS` guards even though tracking now prevents re-runs — they make the
scripts safe to apply to a database that predates tracking.

### 2. Embed the scripts

In `Wisdi.AppPlatform.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="Data\Scripts\*.sql" />
</ItemGroup>
```

Resource names become `Wisdi.AppPlatform.Data.Scripts.000_CreateSchemaVersions.sql` etc.

### 3. `Data/MigrationScript.cs`

```csharp
public sealed record MigrationScript(string Name, string Source, Func<CancellationToken, Task<string>> ReadAsync);
```

`Source` is `"core"` or `"app"` — used only for logging.

### 4. `Data/IMigrationScriptProvider.cs` and the two implementations

```csharp
public interface IMigrationScriptProvider
{
    IEnumerable<MigrationScript> GetScripts();
}
```

**`EmbeddedMigrationScriptProvider`** — scans the platform assembly for resources matching
`Wisdi.AppPlatform.Data.Scripts.*.sql` and yields them with the resource-name prefix
stripped, so `Name` is just `000_CreateSchemaVersions.sql`.

**`DirectoryMigrationScriptProvider`** — reads `*.sql` from a directory, top level only.
Resolution order for the directory, first hit wins:
1. `{assemblyDirectory}\Database\Scripts`
2. `{currentDirectory}\Database\Scripts`

Drop the source's third fallback (`..\..\..\..\App\Database\Scripts`) — it hard-codes the
StockAnalysis layout. Returning zero scripts when the directory is absent is **not** an
error; an app with no schema of its own is legitimate.

### 5. `Data/DatabaseMigrator.cs`

```csharp
public class DatabaseMigrator
{
    public DatabaseMigrator(
        IDbContextFactory<PlatformDbContext> dbContextFactory,  // see note below
        IEnumerable<IMigrationScriptProvider> providers,
        ILogger<DatabaseMigrator> logger);

    public Task<MigrationResult> InitializeDatabaseAsync(CancellationToken ct = default);
    public Task<IReadOnlyList<string>> GetAvailableScriptsAsync(CancellationToken ct = default);
    public Task<IReadOnlyList<string>> GetAppliedScriptsAsync(CancellationToken ct = default);
}
```

**Constructor note:** `IDbContextFactory<PlatformDbContext>` will not resolve, because the
factory is registered for the concrete `TContext`. Make the migrator generic —
`DatabaseMigrator<TContext> where TContext : PlatformDbContext` — and have
`AddPlatformData<TContext>` register both `DatabaseMigrator<TContext>` and a non-generic
`DatabaseMigrator` base or interface `IDatabaseMigrator` that endpoints depend on. Endpoints
must not be generic.

Algorithm for `InitializeDatabaseAsync`:

1. Create a context from the factory. `if (!await Database.CanConnectAsync(ct))` return
   `Success = false, CanConnect = false` with the source's message — the database must
   already exist; the migrator never creates it.
2. Execute `000_CreateSchemaVersions.sql` unconditionally. It is idempotent and is never
   recorded.
3. Read applied names: `SELECT ScriptName FROM [dbo].[__SchemaVersions]` into a
   `HashSet<string>` (ordinal-ignore-case).
4. Build the ordered script list: **all core scripts first**, then all app scripts, each
   group sorted by `Name` with `StringComparer.OrdinalIgnoreCase`.
5. Validate numbering and **fail the whole run** on violation:
   - a core script whose leading number is not in `000`-`099`
   - an app script whose leading number is below `100`
   - a duplicate `Name` across the two providers

   Failing loudly here is the point — silent misordering across the package boundary is
   exactly the bug this step exists to prevent.
6. For each script not already applied:
   - split the text into batches on lines that are exactly `GO` (trimmed,
     case-insensitive); skip empty batches
   - open a transaction
   - `ExecuteSqlRawAsync` each batch
   - `INSERT INTO [dbo].[__SchemaVersions] (ScriptName, Checksum) VALUES (@name, @checksum)`
     where checksum is the lowercase hex SHA-256 of the script text
   - commit; on exception roll back, log, and return `Success = false` with the scripts
     applied so far
7. Return `Success = true` with `AppliedMigrations` = the names applied **in this run**.
   An unchanged database must therefore report an **empty** list — this is what Step 15
   asserts.

Keep `MigrationResult` as the source defines it (`Success`, `Message`, `AppliedMigrations`,
`CanConnect`, `Error`), with `AppliedMigrations` as `List<string>`.

### 6. Wire into `AddPlatformData<TContext>`

Add to the Step 04 extension method:

```csharp
services.AddSingleton<IMigrationScriptProvider, EmbeddedMigrationScriptProvider>();
services.AddSingleton<IMigrationScriptProvider, DirectoryMigrationScriptProvider>();
services.AddScoped<DatabaseMigrator<TContext>>();
services.AddScoped<IDatabaseMigrator>(sp => sp.GetRequiredService<DatabaseMigrator<TContext>>());
```

### 7. `Hosting/MigrationEntryPoint.cs`

The `--migrate` CLI path from `Program.RunMigration`, generalised so apps do not reimplement it.

```csharp
public static class MigrationEntryPoint
{
    /// <summary>
    /// Returns true when args contain "--migrate".
    /// </summary>
    public static bool IsMigrationRun(string[] args);

    /// <summary>
    /// Builds a minimal service provider, runs migrations, writes results to the console
    /// and returns a process exit code (0 success, 1 failure).
    /// </summary>
    public static Task<int> RunAsync<TContext>(string[] args, Action<PlatformAssemblies>? configureAssemblies = null)
        where TContext : PlatformDbContext;
}
```

Port from `Program.RunMigration`: parse `--settingsFile` / `-settingsFile`, layer config via
`AddPlatformConfiguration`, add console logging, call `AddPlatform` + `AddPlatformData<TContext>`,
resolve `IDatabaseMigrator`, print `Applied migrations: ...` or the failure, return the code.

**Change from the source:** return an exit code rather than calling `Environment.Exit(1)`,
so it is testable. The app's `Main` does `return await MigrationEntryPoint.RunAsync<AppDbContext>(args);`.

## Tests to add

`tests/Wisdi.AppPlatform.Tests/MigrationTests.cs` — these need no database:

1. `EmbeddedMigrationScriptProvider` returns exactly the two core scripts, named
   `000_CreateSchemaVersions.sql` and `010_CreateBackgroundTasks.sql`.
2. The batch splitter turns `A\nGO\nB` into two batches, leaves `A\nGONZO\nB` as one, and
   handles trailing `GO` and `go` in any case.
3. Ordering: given fake core `[050_x]` and app `[100_a, 110_b]` providers, the merged order
   is `050_x, 100_a, 110_b`.
4. Validation: an app script named `090_bad.sql` is rejected with a message naming the file
   and the `100+` rule.
5. Validation: the same `Name` from both providers is rejected as a duplicate.
6. Checksum is stable and 64 lowercase hex characters.

The real "run twice, second is a no-op" test needs SQL and happens at Step 15.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

**Expected:** zero warnings, zero errors, all tests pass.

## Done when

- [ ] Build clean, all tests pass
- [ ] Core scripts are embedded resources, not files copied to output
- [ ] `010_CreateBackgroundTasks.sql` creates the table with **all** columns including
      `StatusMessage`, `ExecutionManagerId` and `LeaseExpiresUtc` — the §3 drift bug is gone
- [ ] Out-of-range script numbers fail the run rather than being silently reordered
- [ ] A second `InitializeDatabaseAsync` on an unchanged database returns an empty
      `AppliedMigrations`

## Commit

```powershell
git add -A
git commit -m "Step 05: script-tracked migrations with embedded core scripts and 000-099/100+ ordering"
```
