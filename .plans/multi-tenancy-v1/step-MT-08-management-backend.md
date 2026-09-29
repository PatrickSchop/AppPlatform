# Step MT-08 — Management app backend: registry schema, self-registration, bootstrap

**Phase:** 2 — Management app
**Depends on:** MT-07 (Gate D passed)
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Create `apps/Management`, a platform app in `TenancyMode.Single` that owns the registry. After
this step it can migrate its database, register **itself** as application `management` with
role `admin`, make a given identity its first admin, and authorize that admin through the
ordinary platform pipeline (MT-04) using its own database as the directory.

No HTTP API for other apps yet (MT-09), and no admin CRUD yet (MT-10).

## Tasks

### 1. Project

`apps/Management/Management.csproj`, shaped like `samples/MultiTenantSample` (`ProjectReference`
to `PS.AppPlatform`, both shim folders, `<PlatformTenancy>true</PlatformTenancy>`). Root
namespace `PS.Management`. Add to `AppPlatform.slnx` under a new `/apps/` folder. Add the test
project `tests/Management.Tests` (xUnit, NSubstitute and EF InMemory, the same packages as
`PS.AppPlatform.Tests`).

```csharp
public sealed class Manifest : AppManifest
{
    public override string Key => "management";
    public override string DisplayName => "Application management";
    public override TenancyMode Tenancy => TenancyMode.Single;
    public override IReadOnlyList<AppRoleDefinition> Roles => [new("admin", "Administrator")];
}
```

### 2. Registry entities (`Registry/`), all **application-wide** `Entity`

| Entity | Columns | Constraints |
|---|---|---|
| `Application` | `Key`, `DisplayName`, `Tenancy` (int), `ServicePrincipalId` (Guid?), `IsDisabled`, `RegisteredUtc`, `UpdatedUtc` | unique `Key` |
| `Role` | `ApplicationId`, `Name`, `DisplayName`, `Description`, `IsDeprecated` | unique (`ApplicationId`, `Name`) |
| `Tenant` | `ApplicationId`, `Name`, `IsDisabled`, `CreatedUtc` | unique (`ApplicationId`, `Name`) |
| `Team` | `TenantId`, `Name`, `IsDefault` | unique (`TenantId`, `Name`); one `IsDefault` per tenant (filtered unique index) |
| `User` | `ObjectId` (nvarchar(64) null), `IssuerTenantId` (nvarchar(64) null), `DisplayName`, `Email` (label only), `IsDisabled`, `CreatedUtc`, `BoundUtc` | unique (`ObjectId`, `IssuerTenantId`) `WHERE ObjectId IS NOT NULL` |
| `TeamMember` | `TeamId`, `UserId` | unique (`TeamId`, `UserId`) |
| `RoleAssignment` | `TeamMemberId`, `RoleId` | unique (`TeamMemberId`, `RoleId`) |
| `Invitation` | `UserId`, `TokenHash` (char(64)), `ExpiresUtc`, `AcceptedUtc`, `RevokedUtc`, `CreatedByUserId` | unique `TokenHash` |

`Tenant` here is a registry **row**, not a `TenantEntity`: the management app manages every
application's tenants and must never filter them. Say so in an XML comment on the class.

Foreign keys with `ON DELETE CASCADE` down the tree
(Application → Role/Tenant → Team → TeamMember → RoleAssignment), and `NO ACTION` from
`RoleAssignment.RoleId`. That second rule makes deleting an assigned role fail at the
database as well as in the service.

`Database/Scripts/100_CreateRegistry.sql` creates all eight tables with `IF NOT EXISTS`
guards, following the v1 Step 05 script style.

### 3. `Registry/RegistryService.cs`, the one place that mutates the tree

MT-09 and MT-10 call into it, so write it with that reuse in mind.

```csharp
public sealed record ApplicationRegistration(
    string Key, string DisplayName, TenancyMode Tenancy,
    IReadOnlyList<AppRoleDefinition> Roles, Guid? ServicePrincipalId);

public sealed record RegistrationResult(
    Guid ApplicationId, bool Created,
    IReadOnlyList<string> RolesAdded, IReadOnlyList<string> RolesDeprecated, IReadOnlyList<string> RolesReactivated);

public Task<RegistrationResult> UpsertApplicationAsync(ApplicationRegistration registration, CancellationToken ct);
public Task<Guid> EnsureAdminAsync(IdentityKey identity, string displayName, CancellationToken ct);
```

Upsert rules (D2):
- new application → create it + tenant `Default` + team `Default` (`IsDefault = true`);
- role in request, missing in DB → add; present but deprecated → reactivate;
- role in DB, missing from request → `IsDeprecated = true`. **Never delete** (assignments survive);
- `Tenancy` changed from `Multi` to `Single` while more than one enabled tenant exists → reject
  with a message. The reverse is allowed;
- `ServicePrincipalId` is overwritten when supplied (redeploys can recreate the identity);
- one transaction; idempotent (a second identical call reports no changes).

### 4. `Registry/MembershipQuery.cs`

`Task<UserMemberships?> GetAsync(string appKey, IdentityKey identity, CancellationToken ct)`,
shared by the local directory (below) and MT-09's API. Rules:
- user must be bound (`ObjectId`/`IssuerTenantId` match) and not disabled; application and
  tenant not disabled;
- per tenant: `TeamIds` = the user's teams in it; `Roles` = distinct non-deprecated role
  names across those teams' assignments;
- a tenant where the user is in a team but has **no** roles is still returned (roles empty).
  Membership grants access; roles grant permissions;
- null when the user is unknown or has no membership in the application.

### 5. `Registry/LocalRegistryTenantDirectory.cs`

`ITenantDirectory` for the management app: `MembershipQuery.GetAsync(manifest.Key, identity)`
wrapped in the same `IMemoryCache` duration as other apps (`tenancy:cacheDuration`).

Platform change in `AddPlatformTenancy` (MT-02 threw for `local`): `tenancy:directory = local`
now registers **nothing** and adds a startup validation that some `ITenantDirectory` is
registered, with a message saying the app must register its own.

### 6. Post-migration steps (platform change)

Add to `PS.AppPlatform.Hosting`:

```csharp
/// <summary>Runs after a successful --migrate, in registration order. Non-zero aborts.</summary>
public interface IPostMigrationStep
{
    Task<int> RunAsync(IServiceProvider services, CancellationToken ct);
}
```

`MigrateCommand` (MT-01) runs registered steps after the migration and the tenancy column
check (MT-03). The management app registers `SelfRegistrationStep`, which calls
`UpsertApplicationAsync` with its own manifest (`ServicePrincipalId = null`). The management
app therefore exists in its own registry after every `--migrate`, which is what D5 requires.

### 7. `--bootstrap-admin`

`BootstrapAdminCommand : IPlatformCommand` (`Name = "bootstrap-admin"`), registered through
`PlatformCommandLine.RunAsync`'s `configureServices` hook in `Program.cs`:

```powershell
dotnet Management.dll --bootstrap-admin --oid <guid> --tid <guid> --name "Patrick Schop"
```

- validates both ids as GUIDs; requires the `management` application to exist (so run
  `--migrate` first) and says so if not;
- `EnsureAdminAsync`: find the user by (`oid`, `tid`) or create it **already bound**
  (`BoundUtc = now`); ensure membership of `management`/`Default`/`Default`; ensure the `admin`
  assignment; re-enable the user if disabled;
- idempotent, prints what it changed, exit 0.

### 8. `Program.cs` and settings

```csharp
if (PlatformCommandLine.IsCommandRun(args))
    return await PlatformCommandLine.RunAsync<RegistryDbContext>(args, configureServices: ManagementCommands.Register);
...
builder.Services.AddPlatform(builder.Configuration, assemblies);
builder.Services.AddPlatformData<RegistryDbContext>(builder.Configuration);
builder.Services.AddPlatformTenancy(builder.Configuration);
builder.Services.AddScoped<ITenantDirectory, LocalRegistryTenantDirectory>();
```

`appsettings.json`: `tenancy: { mode: "Single", directory: "local" }`, `requiredRole: "admin"`
under `authentication`. **Every** management endpoint is admin-only by default. The exceptions
in MT-09/MT-10 opt out explicitly with `[Authorize(Policy = PlatformPolicies.AuthenticatedOnly)]`
(MT-04) and then do their own caller check.

## Tests to add (`tests/Management.Tests`)

1. First upsert creates application + `Default` tenant + `Default` team; second identical upsert
   reports no changes.
2. Removing a role deprecates it and keeps its assignments; re-adding reactivates it.
3. `Multi` → `Single` with two tenants is rejected.
4. `MembershipQuery`: roles are the union across two teams in one tenant; a deprecated role is
   excluded; a disabled user, disabled tenant or disabled application returns null; an unbound
   user returns null.
5. `EnsureAdminAsync` twice → one user, one membership, one assignment.
6. `BootstrapAdminCommand` **invoked** through `PlatformCommandLine` with a test service graph:
   exit 0, admin exists; bad GUID → non-zero with a message naming `--oid`.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
cd apps\Management
dotnet run -- --migrate
dotnet run -- --bootstrap-admin --oid <your oid> --tid <your tid> --name "Operator"
dotnet run -- --bootstrap-admin --oid <your oid> --tid <your tid> --name "Operator"   # no changes
func start
```

Then, with a token as in Gate D:

| Request | Expected |
|---|---|
| `GET /api/me/tenants` | 200, one tenant `Default`, roles `[admin]` |
| `GET /api/health` | 200 |
| Re-run bootstrap with a different oid, then call `/api/me/tenants` as yourself | still 200 (both are admins) |
| `UPDATE Users SET IsDisabled = 1` for your row, restart the host (clears the cache) | `GET /api/me/tenants` → `registered: false` |
| Re-run `--bootstrap-admin` for yourself, restart | `roles: [admin]` again (bootstrap re-enables) |

## Done when

- [ ] `--migrate` leaves the management app registered in its own registry
- [ ] `--bootstrap-admin` is idempotent and yields a working admin sign-in
- [ ] Membership rules above are tested, including the union of roles and the disabled cases

## Commit

```powershell
git add -A
git commit -m "MT-08: management app backend with registry schema, self-registration and bootstrap admin"
```
