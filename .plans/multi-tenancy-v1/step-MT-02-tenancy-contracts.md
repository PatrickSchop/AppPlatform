# Step MT-02 — Tenancy contracts and `AddPlatformTenancy`

**Phase:** 1 — Core tenancy
**Depends on:** MT-01
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Create the `PS.AppPlatform.Tenancy` namespace: the types every later step depends on, plus
the local-dev directory that lets a multi-tenant app run with **no management app at all**.
Nothing is wired into requests yet (MT-04) and nothing touches EF yet (MT-03).

## Tasks

### 1. `Tenancy/TenancyMode.cs`

```csharp
public enum TenancyMode
{
    None,   // v1 behaviour: authentication only, no registry
    Single, // registry-backed users and roles; exactly one tenant, always resolved
    Multi   // registry-backed; users may belong to several tenants
}
```

### 2. `Tenancy/AppManifest.cs`

```csharp
public sealed record AppRoleDefinition(string Name, string DisplayName, string? Description = null);

/// <summary>
/// Declares the application to the registry. Exactly one non-abstract subclass per app,
/// discovered across PlatformAssemblies like ServiceBuilder (public parameterless ctor).
/// </summary>
public abstract class AppManifest
{
    /// <summary>Stable registry key: lowercase letters, digits, '-'; 3-40 chars.</summary>
    public abstract string Key { get; }
    public virtual string DisplayName => Key;
    public abstract TenancyMode Tenancy { get; }
    public virtual IReadOnlyList<AppRoleDefinition> Roles => [];
}
```

Validation (throw `InvalidOperationException` at registration time, message names the class):
key format; role names unique case-insensitively and matching `^[a-z][a-z0-9.\-]{0,63}$`;
more than one manifest found; `Tenancy != None` with no manifest.

### 3. `Tenancy/ITenantContext.cs`

```csharp
public interface ITenantContext
{
    bool IsResolved { get; }
    Guid? UserId { get; }                 // registry user id, not the Entra oid
    Guid? TenantId { get; }
    IReadOnlyList<Guid> TeamIds { get; }
    IReadOnlySet<string> Roles { get; }   // effective roles in TenantId
}

/// <summary>Scoped. Written once per invocation by the middleware or the task runner.</summary>
public sealed class TenantContext : ITenantContext
{
    public void Set(Guid userId, Guid tenantId, IReadOnlyList<Guid> teamIds, IEnumerable<string> roles);
    public void SetSystem(Guid tenantId);  // background task: tenant but no user
    // Set/SetSystem throw if called twice — a scope never changes tenant.
}
```

Register `TenantContext` scoped, and `ITenantContext` forwarding to it.

### 4. `Tenancy/ITenantDirectory.cs`

```csharp
public sealed record IdentityKey(string ObjectId, string IssuerTenantId); // oid, tid claims

public sealed record TenantMembership(
    Guid TenantId, string TenantName, IReadOnlyList<Guid> TeamIds, IReadOnlyList<string> Roles);

public sealed record UserMemberships(Guid UserId, string DisplayName, IReadOnlyList<TenantMembership> Tenants);

public interface ITenantDirectory
{
    /// <summary>Null when the identity is not a registered, enabled user of this application.</summary>
    Task<UserMemberships?> GetMembershipsAsync(IdentityKey identity, CancellationToken ct);
}
```

`Roles` in `TenantMembership` is already the **union across the user's teams in that tenant**.
Implementations compute it; the middleware does not.

### 5. `Tenancy/ConfigTenantDirectory.cs`

For local development and tests. Reads `tenancy:devDirectory`:

```json
"tenancy": {
  "mode": "Multi",
  "directory": "config",
  "devDirectory": {
    "tenants": [
      { "id": "11111111-0000-0000-0000-000000000001", "name": "Contoso" },
      { "id": "11111111-0000-0000-0000-000000000002", "name": "Fabrikam" }
    ],
    "users": [
      {
        "id": "22222222-0000-0000-0000-000000000001",
        "oid": "<your oid>", "tid": "<your tid>", "displayName": "Dev user",
        "memberships": [
          { "tenantId": "11111111-0000-0000-0000-000000000001", "roles": [ "editor" ] },
          { "tenantId": "11111111-0000-0000-0000-000000000002", "roles": [ "viewer" ] }
        ]
      }
    ]
  }
}
```

Each membership gets a synthetic `Default` team id derived deterministically from the tenant
id. **Refuse to construct** when `IHostingEnvironment` reports production — a config directory
in production would let an app setting grant access. Throw with a message pointing at
`tenancy:directory`.

### 6. `Tenancy/TenancyOptions.cs` and `TenancyServiceBuilder.cs`

```csharp
public sealed class TenancyOptions
{
    public const string SectionName = "tenancy";
    public TenancyMode Mode { get; set; }              // must equal the manifest's Tenancy
    public string Directory { get; set; } = "management"; // "management" | "config" | "local"
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);
    public string TenantHeader { get; set; } = "X-Tenant-Id";
}

public static class TenancyServiceBuilder
{
    public static IServiceCollection AddPlatformTenancy(this IServiceCollection services, IConfiguration configuration);
}
```

`AddPlatformTenancy`:
- binds `TenancyOptions`, finds the manifest (via the registered `PlatformAssemblies`), and
  throws if `tenancy:mode` disagrees with `manifest.Tenancy` — two sources of truth that
  disagree is a deployment bug, not a preference;
- registers `AppManifest` (singleton instance), `TenantContext` / `ITenantContext` (scoped),
  `IMemoryCache`;
- registers `ITenantDirectory` by `Directory`: `config` → `ConfigTenantDirectory`;
  `management` and `local` → throw `NotSupportedException("... arrives in MT-08/MT-09")` for now;
- is idempotent (a second call is a no-op).

Apps that never call it remain `TenancyMode.None`. Register a `TenancyMode` singleton of
`None` in `AddPlatform` via `TryAddSingleton`, so later steps can always ask which mode is
active.

## Tests to add

`tests/PS.AppPlatform.Tests/TenancyContractTests.cs`:

1. Manifest discovery finds a test manifest; two manifests → throws naming both.
2. Invalid key (`"Has Caps"`) and duplicate role (`editor`/`Editor`) are rejected.
3. `tenancy:mode=Single` with a `Multi` manifest → `AddPlatformTenancy` throws.
4. `ConfigTenantDirectory` returns memberships for a configured oid/tid, `null` for an
   unknown oid, and `null` for a known oid with the **wrong tid** (the pair is the key).
5. `ConfigTenantDirectory` refuses to construct in production.
6. `TenantContext.Set` twice throws.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

**Expected:** zero warnings, all tests pass. `SampleApp` still builds and runs unchanged
(it does not call `AddPlatformTenancy`).

## Done when

- [ ] All public contracts from this document exist under `PS.AppPlatform.Tenancy`
- [ ] A missing, duplicate or disagreeing manifest fails at startup with a clear message
- [ ] The config directory keys users by `(oid, tid)` and cannot run in production
- [ ] `SampleApp` unaffected

## Commit

```powershell
git add -A
git commit -m "MT-02: tenancy contracts, AppManifest, config tenant directory, AddPlatformTenancy"
```
