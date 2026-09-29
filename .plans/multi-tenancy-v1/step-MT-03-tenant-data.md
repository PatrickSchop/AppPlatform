# Step MT-03 — `TenantEntity`, query filters and the three factories

**Phase:** 1 — Core tenancy
**Depends on:** MT-02
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Make "this table is per tenant" a one-word decision, `: TenantEntity` instead of `: Entity`.
Every query on such an entity is filtered to the current tenant, every insert is stamped, and
cross-tenant writes throw. An unfiltered context must be asked for by name.

## Design constraints (read before coding)

- **Do not change the `PlatformDbContext` constructor.** Every existing `AppDbContext` passes
  `(options, assemblies)`; a new parameter breaks every consumer. The tenant scope is applied
  **after** construction by the factories below.
- **EF Core 9 caches one model per context type.** The filter must reference **members of the
  context instance**, so EF re-reads them for every context. A captured local or a static
  would freeze the first tenant into the cached model. That is the classic multi-tenant
  leak, and test 8 below exists to catch it.
- **Fail closed.** A filtered context with no tenant must throw, never return all rows.

## Tasks

### 1. `Data/TenantEntity.cs`

```csharp
/// <summary>
/// Base class for per-tenant data. Rows are filtered to the current tenant on every query
/// and stamped on insert. The table MUST have [TenantId] UNIQUEIDENTIFIER NOT NULL.
/// </summary>
public abstract class TenantEntity : Entity
{
    public Guid TenantId { get; set; }
}
```

### 2. `Data/TenantScope.cs` and the exceptions

```csharp
public readonly record struct TenantScope(bool Enabled, Guid? TenantId)
{
    public static TenantScope Disabled => new(false, null);
    public static TenantScope For(Guid? tenantId) => new(true, tenantId);
}

public sealed class TenantContextMissingException : InvalidOperationException { ... }
public sealed class TenantMismatchException : InvalidOperationException { ... }
```

Messages must say what to do. For example: "No tenant is resolved for this context. Use
IScopedDbContextFactory.CreateForTenant(...) or IUnscopedDbContextFactory for code that runs
without a request tenant (timers, anonymous endpoints, reporting)."

### 3. `PlatformDbContext` changes

```csharp
private TenantScope _tenantScope = TenantScope.Disabled;
private bool _scopeApplied;

/// <summary>Called once by the platform factories, before first use.</summary>
public void ApplyTenantScope(TenantScope scope); // throws if called twice

public bool TenantFilterEnabled => _tenantScope.Enabled;

/// <summary>Guid.Empty when filtering is disabled; throws when enabled without a tenant.</summary>
public Guid CurrentTenantId => !_tenantScope.Enabled
    ? Guid.Empty
    : _tenantScope.TenantId ?? throw new TenantContextMissingException();
```

In `OnModelCreating`, after the existing `Id` convention loop, for every discovered type
assignable to `TenantEntity`, build and apply the filter with the expression API so it
references **this instance**:

```csharp
// e => !this.TenantFilterEnabled || e.TenantId == this.CurrentTenantId
var e = Expression.Parameter(entityType, "e");
var ctx = Expression.Constant(this);
var body = Expression.OrElse(
    Expression.Not(Expression.Property(ctx, nameof(TenantFilterEnabled))),
    Expression.Equal(
        Expression.Property(e, nameof(TenantEntity.TenantId)),
        Expression.Property(ctx, nameof(CurrentTenantId))));
modelBuilder.Entity(entityType).HasQueryFilter(Expression.Lambda(body, e));
modelBuilder.Entity(entityType).HasIndex(nameof(TenantEntity.TenantId));
```

Only apply to the **root** of an inheritance hierarchy. EF rejects filters on derived types.

### 4. `Data/TenantSaveChangesInterceptor.cs`

A singleton `SaveChangesInterceptor`. Override both `SavingChanges` and `SavingChangesAsync`.
For each tracked `TenantEntity` entry on a `PlatformDbContext`:

| Context | State | Rule |
|---|---|---|
| filtered | `Added`, `TenantId == Guid.Empty` | set to `CurrentTenantId` (throws if no tenant) |
| filtered | `Added`, other tenant | throw `TenantMismatchException` |
| filtered | `Modified` / `Deleted`, original `TenantId` ≠ current | throw `TenantMismatchException` |
| any | `Modified`, `TenantId` property modified | throw: rows never move between tenants |
| unfiltered | `Added`, `TenantId == Guid.Empty` | throw: an unscoped insert must set `TenantId` explicitly |

Add the interceptor in `AddPlatformData`'s `configureDbContext` alongside the SQL token
interceptor. It must be registered **unconditionally**; it is a no-op for models without
`TenantEntity`.

### 5. The factories: `Data/TenantDbContextFactories.cs`

```csharp
public interface IScopedDbContextFactory<TContext> : IDbContextFactory<TContext>
    where TContext : PlatformDbContext
{
    /// <summary>Filtered to an explicit tenant, e.g. one resolved by an anonymous endpoint.</summary>
    TContext CreateForTenant(Guid tenantId);
}

public interface IUnscopedDbContextFactory<TContext> where TContext : PlatformDbContext
{
    /// <summary>NO tenant filter. Cross-tenant reporting, platform internals, anonymous flows.</summary>
    TContext CreateDbContext();
    Task<TContext> CreateDbContextAsync(CancellationToken ct = default);
}
```

Implementation (all **scoped**):

- Contexts are created with `ActivatorUtilities.CreateInstance<TContext>(serviceProvider)`,
  which resolves `DbContextOptions<TContext>` and `PlatformAssemblies` the same way EF's own
  factory does. Register the options with
  `services.AddDbContext<TContext>(configureDbContext, ServiceLifetime.Scoped, ServiceLifetime.Singleton)`
  and **remove** the existing `AddDbContextFactory<TContext>` call.
- `ScopedDbContextFactory<TContext>` gets `TenancyMode` and `ITenantContext?` (resolve with
  `GetService`; it is absent in `None` mode):
  - `CreateDbContext()` → mode `None`: `TenantScope.Disabled`; otherwise
    `TenantScope.For(tenantContext?.TenantId)` (null tenant → fails closed on first query).
  - `CreateForTenant(id)` → mode `None`: throw `InvalidOperationException`; otherwise `For(id)`.
  - **Guard for `None` mode:** on first creation per `TContext`, if the model contains any
    `TenantEntity`, throw ("TenantEntity requires AddPlatformTenancy"). Cache the check result
    in a static per closed generic type.
- `UnscopedDbContextFactory<TContext>` always applies `TenantScope.Disabled`.
- Registrations in `AddPlatformData<TContext>`:

  | Service | Resolves to |
  |---|---|
  | `IScopedDbContextFactory<TContext>` | `ScopedDbContextFactory<TContext>` |
  | `IDbContextFactory<TContext>` | the same scoped factory (**secure default**) |
  | `IUnscopedDbContextFactory<TContext>` | `UnscopedDbContextFactory<TContext>` |
  | `TContext` (scoped) | `IScopedDbContextFactory<TContext>.CreateDbContext()` |
  | `IDbContextFactory<PlatformDbContext>` | existing adapter over the scoped factory |
  | `IUnscopedDbContextFactory<PlatformDbContext>` | new adapter over the unscoped one |

- `services.TryAddSingleton(TenancyMode.None)` in `AddPlatformData`, so the command graph
  (MT-01), which never calls `AddPlatform`, still resolves.
- `DatabaseMigrator<TContext>` switches to `IUnscopedDbContextFactory<TContext>`: migrations
  are raw SQL and run without a tenant.

### 6. `Data/TenancyConventions.cs`

```csharp
public static class TenancyConventions
{
    /// <summary>
    /// Returns one message per mapped TenantEntity table that lacks a TenantId
    /// UNIQUEIDENTIFIER NOT NULL column, by querying INFORMATION_SCHEMA.COLUMNS.
    /// </summary>
    public static Task<IReadOnlyList<string>> FindTenantColumnViolationsAsync(DbContext context, CancellationToken ct = default);
}
```

`MigrateCommand` (MT-01) calls it after a successful migration **when the model has any
`TenantEntity`**, prints each violation, and returns exit code 1 if there are any. A
convention that is only documented gets forgotten; this makes `--migrate` fail on it.

### 7. Document the convention

Add a "Tenant-aware data" section to `docs/multi-tenancy.md` (create the file; MT-15
completes it):
- `: TenantEntity` vs `: Entity`, and the SQL column and index template:

  ```sql
  [TenantId] UNIQUEIDENTIFIER NOT NULL,
  ...
  CREATE INDEX [IX_Notes_TenantId] ON [dbo].[Notes] ([TenantId], ...);
  ```

- which factory to use when (a table of request, anonymous, timer and reporting cases);
- **navigation caveat:** an application-wide entity must not have a *required* navigation to
  a `TenantEntity`. The filter would hide the principal and EF warns (`10622`); make it
  optional or model it the other way round.

## Tests to add

`tests/PS.AppPlatform.Tests/TenantDataTests.cs`, using the InMemory provider (it evaluates
query filters). Test context: one `TenantNote : TenantEntity`, one `Setting : Entity`.

1. Rows for tenants A and B; a scoped context for A returns only A's notes.
2. `CreateForTenant(B)` returns only B's notes.
3. The unscoped factory returns both.
4. Scoped with no tenant: querying `TenantNote` throws `TenantContextMissingException`
   (assert on the exception or its `InnerException` if EF wraps it, and record which);
   querying `Setting` succeeds.
5. Scoped insert with `TenantId` unset is stamped with A.
6. Scoped insert with `TenantId = B` throws `TenantMismatchException`.
7. A row loaded unscoped for B, attached to a scoped-A context and modified → throws.
8. **Two contexts of the same type, tenants A then B, created in the same test, each see only
   their own rows.** This guards against the cached-model leak.
9. `TenancyMode.None` with `TenantNote` in the model → factory throws on creation; without it,
   `BackgroundTasks` is queryable as before.
10. Unscoped insert with `TenantId` unset throws.
11. `ApplyTenantScope` twice throws.

Existing `DataTests` and `MigrationTests` must stay green.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
cd samples\SampleApp; dotnet run -- --migrate
```

**Expected:** all tests pass. `SampleApp` (mode `None`, no `TenantEntity`) migrates and serves
exactly as before; `GET /api/notes` with a token → 200.

## Done when

- [ ] Deriving from `TenantEntity` is the only thing an entity needs to be filtered and stamped
- [ ] Filtered contexts fail closed with no tenant
- [ ] The unfiltered factory exists only as `IUnscopedDbContextFactory<T>`
- [ ] `--migrate` fails when a tenant table lacks `TenantId`
- [ ] Test 8 (two tenants, one model) passes
- [ ] No derived context's constructor changed

## Commit

```powershell
git add -A
git commit -m "MT-03: TenantEntity with per-instance query filter, stamping interceptor, scoped and unscoped factories"
```
