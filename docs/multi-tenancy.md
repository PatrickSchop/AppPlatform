# Multi-tenancy

An app can know who its users are, which **tenant** each belongs to and what each may do there,
instead of letting every authenticated person in. This page is the one reference for app authors
and for the operator.

1. [Concepts](#concepts)
2. [Quick start](#quick-start)
3. [Tenant resolution](#tenant-resolution) and [roles](#roles-come-from-the-registry-only)
4. [Opting in](#opting-in-addplatformtenancy-and-platformtenancy)
5. [Tenant-aware data](#tenant-aware-data), the [factory table](#which-factory-to-use-when) and
   [anonymous endpoints](#anonymous-endpoints)
6. [The login flow](#the-login-flow)
7. [Registration](#registration)
8. [Operator runbook](#operator-runbook)
9. [Caching and consistency](#caching-and-consistency)

## Concepts

The **management app** (`apps/Management`) holds the registry: **Application → Tenant → Team →
User → Role**.

- An **application** is one deployed app, identified by its `Key`. It declares its roles in code
  (`Manifest`) and registers them on every deploy.
- A **tenant** is a customer or organisation inside an application. Data belongs to a tenant.
- A **team** groups users inside a tenant. Roles are assigned to a user's membership of a team;
  a user's roles in a tenant are the union across their teams.
- A **user** is a person, created by an administrator and **bound** to a sign-in identity the first
  time they accept an invitation. Email addresses are never matched to identities.

An app runs in one of three modes, set by `tenancy:mode` and declared by its manifest:

| Mode | Registry | Tenants | Use |
|---|---|---|---|
| `None` | no | – | authentication only (the original behaviour) |
| `Single` | yes | exactly one, always selected | registered users and roles, no tenant choice |
| `Multi` | yes | several; the user chooses | a SaaS-style app |

## Quick start

```powershell
dotnet new install PS.AppPlatform.Templates
dotnet new tinyapp -n Recipes --tenancy multi --azure-app-name recipes `
    --management-url https://<management app>.azurewebsites.net `
    --management-audience api://<client id of PS Apps API>
```

(`--tenancy` is `none`, `single` or `multi`; `none` is the default and generates the same app as
before.) The generated app has a `Manifest`, a tenant-owned `Item` entity with its migration
script, `GET`/`POST /api/items`, the tenancy wiring in `Program.cs` and `.csproj`, and a deploy
workflow that registers it.

Local development needs no management app. `appsettings.development.json` selects the **config
directory** (`tenancy:directory = config`) and lists one tenant and one user. Replace the zero
`oid` and `tid` with yours, then `dotnet build`, `dotnet exec bin/Debug/net10.0/Recipes.dll --migrate`
and `func start`. In production the directory is the management app (`tenancy:directory =
management`).

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
| `registry_unavailable` | 503 | The management app could not be reached and nothing usable is cached; `Retry-After: 5` |
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

## Opting in: `AddPlatformTenancy` and `<PlatformTenancy>`

Both of these go together — one without the other gives either a missing endpoint or one that
fails DI resolution on its first call:

```xml
<!-- app.csproj -->
<PlatformTenancy>true</PlatformTenancy>
```

```csharp
// Program.cs
builder.Services.AddPlatformTenancy(builder.Configuration);
```

`<PlatformTenancy>true</PlatformTenancy>` makes `PS.AppPlatform.Functions.targets` inject the
tenancy shims (`endpoints/tenancy/*.cs`, currently `GET /api/me/tenants`) alongside the always-on
platform shims. Nothing can read the MSBuild property at runtime, so `AddPlatformTenancy` cannot
detect a missing `<PlatformTenancy>` (the shim just isn't compiled in) and the property cannot
detect a missing `AddPlatformTenancy` call (the shim compiles but fails DI resolution the first
time it runs). The template sets both from the same `--tenancy` option so generated apps cannot
drift. The order of `AddPlatformTenancy` and `AddPlatformData` does not matter: the tenancy call
replaces the default mode that the data call registers.

### `GET /api/me/tenants`

The endpoint the login flow is built on. `[TenantOptional]` with
`[Authorize(Policy = PlatformPolicies.AuthenticatedOnly)]`, so it answers any authenticated caller,
registered or not, with no tenant selected, and ignores `requiredRole`. A user who holds no role
must still be told that they are not registered, instead of getting a 403.

```json
{
  "registered": true,
  "displayName": "Patrick",
  "tenants": [
    { "tenantId": "…", "name": "Contoso",  "roles": [ "editor" ] },
    { "tenantId": "…", "name": "Fabrikam", "roles": [ "viewer" ] }
  ]
}
```

An unregistered user gets `200 { "registered": false, "displayName": null, "tenants": [] }` —
a clean state for the SPA to render, not an error to decode. Tenants are ordered by name
(ordinal, case-insensitive).

### `/configuration.json`

In `Single`/`Multi` mode the platform adds a `tenancy` key so the SPA knows whether to run the
tenant flow before it has a token:

```json
"tenancy": { "mode": "Multi", "header": "X-Tenant-Id" }
```

An app-supplied `webApp:tenancy` key is overwritten (and logged as a warning) — that key is
reserved by the platform. In mode `None` the key is omitted entirely.

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

### Anonymous endpoints

A function marked `[AllowAnonymous]` skips tenant resolution, so it has no tenant of its own. It
resolves the tenant itself from something it can trust (a slug in the URL, a public alias) and
then writes into that tenant explicitly. It never trusts a tenant the caller claims.
`MultiTenantSample`'s feedback endpoint is the pattern:

```csharp
// 1. look the tenant up without a tenant filter
await using var unscoped = await unscopedFactory.CreateDbContextAsync(ct);
var alias = await unscoped.TenantAliases.FirstOrDefaultAsync(a => a.Slug == slug, ct);
if (alias is null) return new NotFoundResult();

// 2. write into exactly that tenant
await using var db = scopedFactory.CreateForTenant(alias.TenantId);
db.Feedback.Add(new Feedback { Text = request.Text });
await db.SaveChangesAsync(ct);
```

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


## The login flow

After sign-in an app has to find the user's tenants, select one (automatically, or by asking),
remember the choice, send it on every request and recover when the server rejects it. The
framework-free `TenantSession` in `@patrickschop/app-client` does all of that; the Angular and React
packages only render its state.

| State | Meaning | What the UI shows |
|---|---|---|
| `idle` / `loading` | not loaded yet | nothing |
| `not-registered` | the user is not in the registry, or has no tenants | "ask an administrator" page |
| `selecting` | several tenants, none chosen | the tenant picker |
| `selected` | a tenant is chosen | the app |
| `error` | the lookup failed | an error with a retry |

- `load()` calls `GET me/tenants`. One tenant is selected automatically. A remembered choice is
  reused if the user still belongs to that tenant; a stale one is forgotten.
- The choice is remembered per app **and per account** in `localStorage`, so switching account never
  reuses another account's tenant.
- Every request carries the tenant in `X-Tenant-Id` (`tenancy.header` in `/configuration.json`).
- The server's answer is the truth: `tenant_required` and `tenant_forbidden` send the user back to
  the picker, `not_registered` reloads the session. The request that got the error still fails.
- Background task polling starts only once a tenant is selected, and follows tenant changes.

Angular (`@patrickschop/app-client-angular`):

```ts
providers: [provideAppPlatform(), provideTenancy()]        // provideTenancy waits for the lookup before the first render

{ path: '', canActivate: [authGuard, tenantGuard], children: [...] }   // your auth guard first
{ path: 'select-tenant', component: TenantPickerComponent }
{ path: 'not-registered', component: NotRegisteredComponent }
```
```html
<ps-tenant-switcher />   <!-- header; renders nothing unless the user has more than one tenant -->
```

`TenantService` exposes `state`, `current`, `tenants` as signals and `select`, `reset`, `hasRole`.
After a switch the page reloads by default, so nothing from the previous tenant stays on screen;
provide `TENANT_SWITCHED` to reload only your data.

React (`@patrickschop/app-client-react`): `PlatformProvider` supplies the session; wrap the authenticated tree
in `<TenantGate>` (picker, not-registered and loading content are replaceable) and put
`<TenantSwitcher />` in the header. `useTenant()` gives the same state and actions.

The components carry `ps-*` class names and no styling of their own, so they take on the app's
design system.

## Registration

An app tells the management app who it is on **every deploy**, before the new code goes live
(`--register`, run by `app-deploy.yaml` when `register: true`):

1. the runner is already signed in as the **deploy principal**;
2. `dotnet <App>.dll --register --principal-id <the app's managed identity>` reads the `Manifest`
   and `PUT`s it to `<management url>/api/registry/v1/applications/<key>`;
3. the management app accepts the call only from a principal in `registry:trustedDeployers`, in
   the tenant `registry:trustedTenantId`.

The upsert rules:

- A new key creates the application with a `Default` tenant and a `Default` team.
- Display name and tenancy mode are updated. **Multi → Single is refused while more than one
  tenant is enabled.**
- Roles are matched by name. A new role is added. A role missing from the manifest is
  **deprecated**, not deleted: existing assignments stay, but it cannot be assigned any more. A
  deprecated role that comes back is reactivated.
- Registration is idempotent: deploying the same manifest twice changes nothing.

If registration fails (the deployer is not trusted, the management app is down) the deploy stops
before the new code is published. The deploy also stops up front when `register` is true and
`management_url` or `management_audience` is empty.

## Operator runbook

Once, for the management app (`apps/Management`, deployed by `management-deploy.yaml`): the
bootstrap administrator is created during deployment from the repository variables
`BOOTSTRAP_ADMIN_OID`, `BOOTSTRAP_ADMIN_TID` and `BOOTSTRAP_ADMIN_NAME`. For a personal Microsoft
account use the `oid` and `tid` the app shows on its "No access" page, not the ones `az` reports.

For each app that uses the registry:

1. **Provision** it with `infra/app.bicep`, passing `managementUrl` and `managementAudience`
   (`api://<client id of PS Apps API>`); they become the `tenancy__management__url` and
   `tenancy__management__audience` app settings.
2. **Trust its deployer.** Add the object id of the principal that deploys the app to
   `registry__trustedDeployers__<n>` on the management Function App. The same principal can deploy
   several apps, so this is usually done once.
3. **Allow its origin.** Add the app's origin to the SPA registration's redirect URIs
   (`docs/auth-setup.md`, Part 2.3).
4. **Deploy.** The deploy registers the app. It then appears in the management app under
   Applications, with its roles.
5. **Invite users.** In the management app, Users → Create user: pick the tenant and team, tick the
   roles, and copy the invitation link (it is shown once). The user opens it, signs in and accepts.
   If the link is lost or has expired, create a new one from the user's page; the old one stops
   working.

A user from a different Entra tenant can only sign in once an administrator of *that* tenant has
consented to the app (the error is `AADSTS500011`). Personal Microsoft accounts and accounts of the
tenant that owns the app registrations need no consent.

## Caching and consistency

An app asks the management app for a user's memberships and keeps the answer in memory, **per
running instance**:

| Answer | Kept for |
|---|---|
| user found | 5 minutes |
| user not registered | 1 minute |
| management app unreachable | the last answer is reused for up to 1 hour (stale-if-error); with none, the request fails with `registry_unavailable` |

So a change in the management app reaches a running app within about five minutes: a newly invited
user may see "not registered" for up to a minute after accepting, and **revoking a role or a user
takes up to five minutes to take effect**, longer if the management app is down. The management
UI says so after every change. Settings: `tenancy:cacheDuration` (the 5 minutes; the "not
registered" time is the smaller of that and one minute, and is not configurable on its own) and
`tenancy:staleIfError`.
