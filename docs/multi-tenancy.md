# Multi-tenancy

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
