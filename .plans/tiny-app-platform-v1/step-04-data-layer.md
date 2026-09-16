# Step 04 — Data layer

**Phase:** 1 — Core engine
**Depends on:** Step 03
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Build the generic data layer that fixes analysis §2.2: a `PlatformDbContext` base class
apps derive from, generic registration `AddPlatformData<TContext>`, and managed-identity
SQL token acquisition.

## The problem being solved

`C:\Dev\StockAnalysis\App\Database\AppDbContext.cs` hard-codes eleven `DbSet`s and scans
`Assembly.GetExecutingAssembly()` for `Entity` subclasses. Both make it impossible to ship
in a package. The base class keeps only what is domain-free and takes the assembly list
injected in Step 03.

## Reference material (read-only)

| Source | Use |
|---|---|
| `App\Database\AppDbContext.cs` | `DiscoverEntityTypes` + the `NEWID()` Id convention only — **not** the eleven DbSets, **not** the Stock/Investment relationship config |
| `App\Database\DatabaseConfiguration.cs` | Port verbatim |
| `App\Database\AzureSqlTokenInterceptor.cs` | Port verbatim, namespace change only |
| `App\Database\ServiceBuilder.cs` | The registration shape, made generic |

## Tasks

### 1. `Data/DatabaseConfiguration.cs`

Port verbatim from the source (both `DatabaseConfiguration` and `ApiMigrationConfiguration`),
into namespace `Wisdi.AppPlatform.Data`.

```csharp
public class DatabaseConfiguration
{
    public string? ConnectionString { get; set; }
    public bool UseManagedIdentity { get; set; }
    public ApiMigrationConfiguration? ApiMigration { get; set; }
}

public class ApiMigrationConfiguration
{
    public bool Enable { get; set; } = false;
}
```

### 2. `Data/AzureSqlTokenInterceptor.cs`

Port verbatim. Only the namespace and the `IAzureIdentityProvider` using change.

It acquires a `https://database.windows.net/.default` token from the credential and sets
`SqlConnection.AccessToken`. Keep **both** the sync and async overrides — EF calls the sync
one in some paths and a missing token there is a confusing runtime failure.

### 3. `Data/PlatformDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Wisdi.AppPlatform.Hosting;
using Wisdi.AppPlatform.Tasks;

namespace Wisdi.AppPlatform.Data;

/// <summary>
/// Base DbContext for platform apps. Holds only the platform's own entities; apps derive
/// from this and add their own DbSets.
/// </summary>
public abstract class PlatformDbContext : DbContext
{
    private readonly PlatformAssemblies _assemblies;

    protected PlatformDbContext(DbContextOptions options, PlatformAssemblies assemblies)
        : base(options)
    {
        _assemblies = assemblies;
    }

    public DbSet<BackgroundTask> BackgroundTasks { get; set; } = null!;

    public DbSet<T> GetEntitySet<T>() where T : Entity => Set<T>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in DiscoverEntityTypes())
        {
            modelBuilder.Entity(entityType)
                .Property(nameof(Entity.Id))
                .HasDefaultValueSql("NEWID()")
                .ValueGeneratedOnAdd();
        }

        ConfigurePlatformModel(modelBuilder);
    }

    private static void ConfigurePlatformModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BackgroundTask>(e =>
        {
            e.ToTable("BackgroundTasks");
            e.HasIndex(t => t.Status);
            e.HasIndex(t => t.TaskType);
            e.HasIndex(t => t.CreatedDate);
            e.HasIndex(t => new { t.ExecutionManagerId, t.Status });
        });
    }

    private IEnumerable<Type> DiscoverEntityTypes() =>
        _assemblies.All
            .SelectMany(SafeGetTypes)
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(Entity)));

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
```

`BackgroundTask` does not exist until Step 06. Either stub it now as an empty
`public class BackgroundTask : Entity { }` in `Tasks/BackgroundTask.cs` (Step 06 fills in
the properties), or comment out the `DbSet` and `ConfigurePlatformModel` body. **Prefer the
stub.**

The derived context must pass `PlatformAssemblies` through its own constructor. Document
this in the class XML remarks with the exact expected shape:

```csharp
public class AppDbContext : PlatformDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options, PlatformAssemblies assemblies)
        : base(options, assemblies) { }

    public DbSet<Note> Notes { get; set; } = null!;
}
```

### 4. `Data/PlatformDataExtensions.cs`

The generic replacement for `App\Database\ServiceBuilder.cs`.

```csharp
public static class PlatformDataExtensions
{
    /// <summary>
    /// Registers TContext as scoped, a scoped IDbContextFactory&lt;TContext&gt;, the
    /// managed-identity interceptor when configured, and the DatabaseMigrator.
    /// </summary>
    public static IServiceCollection AddPlatformData<TContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TContext : PlatformDbContext;
}
```

Port the body from the source `ServiceBuilder.BuildServices`, with these changes:
- `AppDbContext` becomes `TContext` everywhere.
- Keep `CommandTimeout(30)` and `EnableRetryOnFailure(3, 30s, null)` exactly as-is.
- Keep `AddDbContext<TContext>` **and** `AddDbContextFactory<TContext>(..., ServiceLifetime.Scoped)`.
  Background task code uses the factory because it outlives the request scope.
- **Drop** the source's redundant third registration (`services.AddScoped<AppDbContext>()`)
  — `AddDbContext` already does this.
- Throw `InvalidOperationException` with a clear message if the `database` section or
  `connectionString` is missing, as the source does.
- `DatabaseMigrator` registration moves to Step 05; leave it out for now.

### 5. `Data/DataServiceBuilder.cs`

Do **not** create one. The app calls `AddPlatformData<TContext>` explicitly from its
`Program.cs`, because the platform cannot know the app's context type. This is the single
place where the platform requires one line of per-app wiring, and it is the direct
consequence of analysis §2.2.

Record that in a comment at the top of `PlatformDataExtensions.cs`.

## Tests to add

`tests/Wisdi.AppPlatform.Tests/DataTests.cs`:

1. A test-local `TestDbContext : PlatformDbContext` with one extra entity builds a model
   via the in-memory provider, and `Model.FindEntityType(typeof(TestEntity))` is not null.
   This proves `DiscoverEntityTypes` picks up entities from the **test** assembly, not the
   platform assembly — i.e. the §2.2 fix works.
2. `Model.FindEntityType(typeof(BackgroundTask))` is not null on that same derived context
   — the platform entity comes along for free.
3. `AddPlatformData<TestDbContext>` throws `InvalidOperationException` when the `database`
   section is absent, and the message mentions `database`.

The in-memory provider ignores `HasDefaultValueSql`, so do not assert on the `NEWID()`
default here — it is covered by the real migration run in Step 15.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

**Expected:** zero warnings, zero errors, all tests pass.

## Done when

- [ ] Build clean, all tests pass
- [ ] `PlatformDbContext` contains **no** domain DbSets and no `GetExecutingAssembly()`
- [ ] `AddPlatformData<TContext>` is generic over `TContext : PlatformDbContext`
- [ ] Entity discovery from a *different* assembly is proven by a test
- [ ] `AzureSqlTokenInterceptor` keeps both sync and async overloads

## Commit

```powershell
git add -A
git commit -m "Step 04: generic PlatformDbContext, AddPlatformData<TContext>, SQL token interceptor"
```
