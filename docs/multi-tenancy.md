# Multi-tenancy

## Tenant resolution

In `Single` and `Multi` mode, every protected HTTP call goes through
authentication → **tenant resolution** → authorization, inside
`FunctionAuthorizationMiddleware`. `[AllowAnonymous]` functions skip all three. In mode `None`
nothing changes.

The resolver looks the user up by the token's `oid` + `tid`, picks the tenant and fills the
scoped `ITenantContext`:

- The SPA sends the chosen tenant in `X-Tenant-Id` (configurable: `tenancy:tenantHeader`).
  The server never trusts it; it must be one of the user's tenants.
- With no header, a user with exactly one tenant gets that tenant. This covers every user of a
  `Single`-mode app.

Errors are JSON bodies, `{"error":"<code>"}`:

| Code | Status | When |
|---|---|---|
| `not_registered` | 403 | The user is not in the registry, or the token has no `oid`/`tid` |
| `tenant_forbidden` | 403 | The header names a tenant the user isn't a member of, or isn't a GUID |
| `tenant_required` | 409 | The user has more than one tenant and sent no header |
| `unauthorized` | 401 | No valid token (unchanged) |
| `forbidden` | 403 | Authorization failed (unchanged) |

### Roles come from the registry only

After resolution the principal's `roles` claims are **replaced** by the user's roles in the
selected tenant, the union across their teams. Entra App Role claims in the token are dropped.
`authentication:requiredRole` and `[Authorize(Roles = "editor")]` therefore check registry roles:

```csharp
[Authorize(Roles = "editor")]            // registry role in the current tenant, AND requiredRole
public Task<IActionResult> Publish(...)

[Authorize(Policy = PlatformPolicies.AuthenticatedOnly)]  // any signed-in caller, ignores requiredRole
public Task<IActionResult> Register(...)
```

`[Authorize(Roles = ...)]` is added on top of the default policy, so it never bypasses
`requiredRole`. A named `Policy` replaces the default policy, as in ASP.NET Core.

### Endpoints that need a user but no tenant

`[TenantOptional]` (method or class) lets the call through without a selected tenant, and for
unregistered users too. `ITenantContext` then has `UserId` set and `TenantId` null, or is
unresolved for an unregistered user. A header that names a non-member tenant is still rejected.
Such a caller has no roles, so in an app with `requiredRole` a `[TenantOptional]` endpoint also
needs `[Authorize(Policy = PlatformPolicies.AuthenticatedOnly)]`.

## Tenant-aware data

### Entity base class

Tables that belong to a tenant derive from `TenantEntity` instead of `Entity`. The platform automatically filters queries to the current tenant and stamps inserts with the tenant ID:

```csharp
public class Note : TenantEntity
{
    public string Content { get; set; } = "";
}

public class AppSetting : Entity  // Application-wide, not per-tenant
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
```

### SQL schema

A tenant-aware table MUST have:
- A `TenantId` column of type `UNIQUEIDENTIFIER NOT NULL`
- An index that leads with `TenantId`

Example migration script:

```sql
CREATE TABLE [dbo].[Notes] (
    [Id]       UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Content]  NVARCHAR(MAX)    NOT NULL,
    [CreatedUtc] DATETIME2      NOT NULL DEFAULT GETUTCDATE()
);

CREATE INDEX [IX_Notes_TenantId] ON [dbo].[Notes] ([TenantId]);
```

The `--migrate` command validates these conventions and fails if they are violated.

### Which factory to use when

Choose your context factory based on whether code runs in an HTTP request context with a resolved tenant:

| Scenario | Factory | Notes |
|----------|---------|-------|
| **HTTP request handler** (controller, endpoint) | `IDbContextFactory<TContext>` or `IScopedDbContextFactory<TContext>` | Default, automatic tenant filtering. Use this 99% of the time. |
| **Anonymous endpoint** that resolves tenant from slug/header | `IScopedDbContextFactory<TContext>.CreateForTenant(tenantId)` | Explicitly pass the tenant. Endpoint validates access. |
| **Background task** (timer, queue trigger) | `IUnscopedDbContextFactory<TContext>` or `IScopedDbContextFactory<TContext>.CreateForTenant(tenantId)` | No implicit tenant. Set it explicitly or query across all tenants. |
| **Reporting / cross-tenant query** | `IUnscopedDbContextFactory<TContext>` | No filtering; you see all tenants. Guard this code in review. |
| **Platform internals** (seeding, admin operations) | `IUnscopedDbContextFactory<TContext>` | Unfiltered. Explicitly set `TenantId` on inserts. |

### Navigation property caveat

An application-wide entity must **not** have a *required* navigation to a `TenantEntity`. The query filter would hide the principal (the tenant entity), causing EF warnings (`10622`) and silent query failures.

**Bad:**
```csharp
public class AppRole : Entity
{
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }  // ❌ Required nav to TenantEntity
}
```

**Good:**
```csharp
public class AppRole : Entity
{
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }  // ✅ Optional nav to TenantEntity
}

// Or model it the other way:
public class Tenant : TenantEntity
{
    public ICollection<AppRole> Roles { get; set; } = [];  // ✅ TenantEntity owns the nav
}
```

### Fail closed

A context with tenant filtering enabled will throw `TenantContextMissingException` if you query a `TenantEntity` without a tenant resolved. This is intentional — queries never silently return empty or leak across tenants.

```csharp
using var context = CreateContextWithoutTenant();
var notes = context.Set<Note>().ToList();  // ❌ Throws TenantContextMissingException
```

Use `IUnscopedDbContextFactory` or `CreateForTenant()` when you need to query across tenants or without a request tenant.

### Background tasks

`BackgroundTask` itself carries a nullable `TenantId` and `CreatedByUserId`, stamped from the
creating request's `ITenantContext` when a task is created via `IBackgroundTaskService`. A task
created outside a request (a timer, an anonymous flow) gets `TenantId = null` — a system task.

The platform endpoints (`GET /api/tasks`, `GET /api/tasks/{id}`, `GET /api/tasks/notifications`)
and `ResumeTaskAsync` are filtered to the caller's tenant automatically; a user never sees or
resumes another tenant's task, and a system task with no tenant is invisible to every user.

`TaskExecutionManager` runs each claimed task in its own DI scope and resolves that task's
tenant into the scope's `ITenantContext` before the handler is created. A handler therefore
needs no tenancy code of its own — `IDbContextFactory<TContext>` injected into the handler is
already filtered to the task's tenant:

```csharp
public class WordCountTaskHandler(IDbContextFactory<AppDbContext> factory) : ITaskHandler<WordCountTaskData>
{
    public async Task HandleAsync(BackgroundTask task, WordCountTaskData data, TaskHandlerContext context, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);   // scoped to task.TenantId
        ...
    }
}
```

A task with no tenant (`TenantId == null`) runs with **no** tenant resolved: a filtered query in
such a handler fails closed with `TenantContextMissingException`, the same as any other
unresolved-tenant query. A handler that intentionally runs across tenants must ask for
`IUnscopedDbContextFactory<TContext>` explicitly. `TaskHandlerContext.TenantId` exposes the
task's tenant without a DI lookup, for handlers that only need to read it.

### How the filter is applied

The filter is added to every `TenantEntity` in the finished EF model, however it got there
(assembly discovery, a `DbSet`, or `modelBuilder.Entity<T>()` in your `OnModelCreating`). A
query filter you add yourself is combined with it, never replaced. If your context overrides
`ConfigureConventions`, it must call `base.ConfigureConventions(...)`.
