# Step MT-01 — `PlatformCommandLine`

**Phase:** 0 — Prerequisites
**Depends on:** v1 Step 22 (the platform as published at `0.1.2`)
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Turn the single-purpose `--migrate` entry point into a small command dispatcher, so later
steps can add `--register` (MT-09) and `--bootstrap-admin` (MT-08) without each one
reinventing configuration loading, settings-file parsing and exit codes.

No new commands ship in this step. `--migrate` must behave exactly as it does today.

## Why

`MigrationEntryPoint` (`src/PS.AppPlatform/Hosting/MigrationEntryPoint.cs`) already does the
hard parts correctly, after two fixed defects (v1 §5): it anchors configuration on the
**entry** assembly, and it registers `IConfiguration` so `AzureIdentityProvider` resolves
under a managed identity. Copying that per command would re-open both defects.

## Tasks

### 1. `Hosting/IPlatformCommand.cs`

```csharp
namespace PS.AppPlatform.Hosting;

/// <summary>A one-shot CLI command run instead of the Functions host, e.g. --migrate.</summary>
public interface IPlatformCommand
{
    /// <summary>The flag that selects this command, without dashes, e.g. "migrate".</summary>
    string Name { get; }

    /// <summary>Returns a process exit code: 0 success, non-zero failure.</summary>
    Task<int> RunAsync(PlatformCommandArgs args, IServiceProvider services, CancellationToken ct);
}

public sealed record PlatformCommandArgs(string[] Raw)
{
    /// <summary>Value after --name or -name, or null.</summary>
    public string? Get(string name);
    public string Require(string name); // throws ArgumentException naming the flag
}
```

### 2. `Hosting/PlatformCommandLine.cs`

```csharp
public static class PlatformCommandLine
{
    /// <summary>True when args select any registered command (--migrate, --register, ...).</summary>
    public static bool IsCommandRun(string[] args);

    /// <summary>
    /// Builds the command service graph (config layering + BuildMigrationServices), finds the
    /// command whose Name matches, runs it, returns its exit code. Unknown command → exit 2.
    /// </summary>
    public static Task<int> RunAsync<TContext>(
        string[] args,
        Action<PlatformAssemblies>? configureAssemblies = null,
        Action<IServiceCollection, IConfiguration>? configureServices = null)
        where TContext : PlatformDbContext;
}
```

- Built-in commands are registered as `IPlatformCommand` in the command service graph. Later
  steps add theirs with `services.AddSingleton<IPlatformCommand, ...>()` inside the
  `configureServices` hook, or from the library itself when tenancy is enabled.
- Move the settings-file parsing and configuration layering out of `MigrationEntryPoint`
  unchanged. Reuse `BuildMigrationServices<TContext>` as the base graph (rename it
  `BuildCommandServices<TContext>`, keeping it `internal`).
- `MigrateCommand` (`Name = "migrate"`) holds the body of today's `RunAsync`: resolve
  `IDatabaseMigrator`, print results in the same colours and wording, return 0 or 1.
- The recognised flag spellings stay `--migrate` and `-migrate`.

### 3. Keep `MigrationEntryPoint` as a thin shim

Existing apps (ScratchApp, template output at `0.1.2`) call
`MigrationEntryPoint.IsMigrationRun` / `RunAsync<T>`. Keep both methods and delegate:

```csharp
public static bool IsMigrationRun(string[] args) => PlatformCommandLine.IsCommandRun(args);
public static Task<int> RunAsync<TContext>(string[] args, Action<PlatformAssemblies>? configureAssemblies = null)
    where TContext : PlatformDbContext
    => PlatformCommandLine.RunAsync<TContext>(args, configureAssemblies);
```

Mark both `[Obsolete("Use PlatformCommandLine")]` **only if** that does not break the build
under `TreatWarningsAsErrors` for `SampleApp` and the template — otherwise leave them
unmarked and note it in the progress document.

### 4. Switch `SampleApp` and the template to the new API

`samples/SampleApp/Program.cs` and `templates/content/tinyapp/Program.cs`:

```csharp
if (PlatformCommandLine.IsCommandRun(args))
    return await PlatformCommandLine.RunAsync<AppDbContext>(args);
```

## Tests to add

`tests/PS.AppPlatform.Tests/CommandLineTests.cs`:

1. `IsCommandRun` is true for `--migrate` and `-migrate`, false for `--other` and for no args.
2. `PlatformCommandArgs.Get("oid")` reads `--oid X` and `-oid X`; `Require` throws naming the
   flag when missing.
3. An unknown command selected through a test-registered command set returns exit code 2.
4. A fake `IPlatformCommand` registered through `configureServices` is invoked, receives the
   args, and its exit code is returned — **invoke it, do not just resolve it** (v1 §5 lesson).
5. `BuildCommandServices` still resolves `IDatabaseMigrator` and `IAzureIdentityProvider`
   with `database:useManagedIdentity=true` (keeps the 2026-09-29 regression test green).

Existing `MigrationTests` must pass unchanged.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
cd samples\SampleApp
dotnet run -- --migrate   # against LocalDB, as in Gate A
dotnet run -- --migrate   # second run
```

**Expected:** build clean, all tests pass; the first run applies nothing new or the usual
scripts, the second reports `Database is up to date`, exit code 0 both times.

## Done when

- [ ] `--migrate` output and exit codes are unchanged
- [ ] `MigrationEntryPoint` still compiles for existing consumers
- [ ] A new command can be added with one `IPlatformCommand` class and one registration
- [ ] Tests invoke a command end to end, not just resolve it

## Commit

```powershell
git add -A
git commit -m "MT-01: PlatformCommandLine dispatcher, --migrate moved onto it"
```
