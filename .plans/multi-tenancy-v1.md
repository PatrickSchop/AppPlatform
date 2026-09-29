# Multi-tenancy for PS.AppPlatform: plan

## Context

`tiny-app-platform-v1.md` §9 explicitly left multi-tenancy out of scope: one app is one database, auth is authentication-only (v1 §2(4), `tenantId: common`, any Microsoft account), and there are no users, roles or query filters. The operator now wants:

- **Single-tenant and multi-tenant apps** built from the same platform, where a multi-tenant app takes only a few components.
- **A central registry** of applications, tenants, teams, users and role assignments, owned by a **management app** (a platform app with an Angular UI).
- **A standard login flow with tenant selection** for identities that belong to more than one tenant.
- **Tenant-aware EF Core**: a base-class convention, automatic filtering, and an explicit way to bypass the filter.

This plan builds on the **end state** of v1, so it assumes Phase 5 is done: `@PS/app-client` with its Angular/React adapters and both starters. Phases 1 and 2 touch only the backend and can start before v1 Phase 5 closes. Phase 3 cannot: it modifies the Phase 5 packages.

Step documents: [multi-tenancy-v1/](multi-tenancy-v1/). Progress: [multi-tenancy-v1-progress.md](multi-tenancy-v1-progress.md). The v1 execution rules (§7: strictly in order, green build and tests per step, commit and push, update progress) apply unchanged.

## Locked design decisions

### D1. The registry lives in the management app, and apps query it through its API

- The management app (`apps/Management`) owns the registry database: `Applications`, `Roles`, `Tenants`, `Teams`, `Users`, `TeamMembers`, `RoleAssignments`, `Invitations`.
- Apps call `GET /api/registry/v1/applications/{appKey}/users/{oid}/memberships` with their **managed identity** (token audience: the shared `PS Apps API`). The management API authorizes the call by matching the caller's `oid` to the `ServicePrincipalId` that was recorded when the application registered.
- Apps cache the result per `(oid, tid)` in `IMemoryCache` for 5 minutes (configurable). Management changes therefore take up to 5 minutes to reach an app. Document this.
- The lookup sits behind `ITenantDirectory`, with three implementations:
  - `ManagementApiTenantDirectory`: the default for apps.
  - `LocalRegistryTenantDirectory`: the management app queries its own database.
  - `ConfigTenantDirectory`: local dev and tests. Memberships come from `appsettings.development.json`, so no management app needs to run.

### D2. Apps register during deployment, not at first startup

The app declares its roles in code:

```csharp
public sealed class Manifest : AppManifest
{
    public override string Key => "recipes";
    public override TenancyMode Tenancy => TenancyMode.Multi;
    public override IReadOnlyList<AppRoleDefinition> Roles =>
        [new("editor", "Editor"), new("viewer", "Viewer")];
}
```

The deploy workflow runs `dotnet <App>.dll --register` right after `--migrate` (this reuses `MigrationEntryPoint` from `src/PS.AppPlatform/Hosting/MigrationEntryPoint.cs`, extended into `PlatformCommandLine`). The command is an idempotent `PUT /api/registry/v1/applications/{key}` that sends the roles and the managed identity principal id taken from the bicep output `identityPrincipalId`. It authenticates as the GitHub OIDC deploy principal, which must be listed in the management app's `registry:trustedDeployers` (by object id). The workflow looks up the principal id itself (`az identity show -n id-<app>`), so callers pass no new input.

**Why deployment is better than startup:**
- A consumption plan cold-starts constantly, so startup registration would add latency and a runtime dependency to every cold start.
- It would also mean every app's managed identity needs write rights on the registry.
- Deploy-time registration runs once and fails the deploy loudly.

**Upsert rules:**
- The first registration creates the application, a tenant `Default` and a team `Default`.
- New roles are added.
- A removed role is marked `Deprecated` rather than deleted, so its assignments survive a bad deploy.

The step reuses the deploy workflow's existing `assembly_name` input, the same one `--migrate` uses.

### D3. The tenancy model and how the platform resolves a tenant

- **Modes:** `TenancyMode.None` is v1 behaviour, unchanged. `Single` and `Multi` both use the registry.
  - **Single:** the registry keeps exactly one tenant, the server always resolves it, and the UI never shows a picker.
  - Tenant-aware entities still work in Single mode, so moving an app from Single to Multi later is a config change.
- **Opting in:** `builder.Services.AddPlatformTenancy(config)`, plus `<PlatformTenancy>true</PlatformTenancy>` in the csproj. The property makes `PS.AppPlatform.Functions.targets` inject the tenancy shims (`endpoints/tenancy/*.cs`), so apps that don't use tenancy don't get them.
- **Transport:** the SPA sends the `X-Tenant-Id` header. The server **never trusts it**: it validates the header against the user's memberships on every request (served from the cache).
- **Order:** CORS → authentication → **tenant resolution** (new) → authorization. Authentication and authorization already live in one middleware, `FunctionAuthorizationMiddleware`, so tenant resolution is a `TenantResolver` service it calls between the two, not a separate middleware; a separate one could not sit between them. The resolver:
  - resolves the user through `ITenantDirectory`;
  - picks the tenant: the header if present, otherwise the only tenant if the user has exactly one;
  - fills the scoped `ITenantContext` (`UserId`, `TenantId`, `TeamIds`, `Roles`);
  - **replaces the principal's `roles` claims with the effective roles in that tenant**, which are the union across the user's teams in the tenant. Any Entra App Role claims in the token are dropped, so the registry is the only source of roles. The existing `requiredRole` check and `[Authorize(Roles=…)]` then work unchanged. `FunctionAuthorizationMiddleware` has to learn to read `AuthorizeAttribute.Roles`; today it only reads `Policy`.
- **Error codes** (all JSON bodies, so the SPA can branch on them):

  | Code | Status | When |
  |---|---|---|
  | `not_registered` | 403 | The user is not in the registry |
  | `tenant_forbidden` | 403 | The header names a tenant the user isn't a member of |
  | `tenant_required` | 409 | The user has more than one tenant and sent no header |

- **Anonymous functions** (`[AllowAnonymous]`) skip tenant resolution entirely. Endpoints that need a user but no tenant, such as `/api/me/tenants`, carry the new `[TenantOptional]` attribute.

### D4. Data convention and filtering

- **Tenant-aware entities** derive from `TenantEntity : Entity` (`Guid TenantId`). Application-wide entities keep deriving from `Entity`. One base class, no attribute, discovered the same way `PlatformDbContext.DiscoverEntityTypes` already finds entities.
- **The filter:** `PlatformDbContext.OnModelCreating` adds `HasQueryFilter(e => !TenantFilterEnabled || e.TenantId == CurrentTenantId)` to every `TenantEntity`. Both are context members, so EF (9.x, one cached model per context type) re-parameterizes them for each context instance.
- **No constructor change:** the tenant scope is applied to a context **after** creation, by a factory wrapper (`context.ApplyTenantScope(...)`). Every existing derived `AppDbContext` keeps its `(options, assemblies)` constructor.
- **Writes:** a `SaveChanges` interceptor stamps `TenantId` on `Added` entities and throws if a `Modified` or `Deleted` entity belongs to another tenant.
- **Fail closed:** a filtered context with no tenant throws `TenantContextMissingException` on any query of a `TenantEntity` (the `CurrentTenantId` getter throws when parameters are extracted). It never returns everything.
- **Tenancy `None`:** contexts are created with the filter disabled, and startup fails if a `TenantEntity` exists in the model. A v1 app upgrades with no behaviour change.
- **Three ways to get a context:**
  - `IDbContextFactory<TContext>` / `IScopedDbContextFactory<TContext>`: filtered by the current request tenant. This is the default and keeps existing code secure.
  - `IScopedDbContextFactory<TContext>.CreateForTenant(Guid tenantId)`: filtered to an explicit tenant. For anonymous flows where the app resolved the tenant itself, for example from a slug.
  - `IUnscopedDbContextFactory<TContext>`: no filter. For cross-tenant reporting and anonymous use. It has to be asked for by name, which makes it easy to find in review.
- **Wiring:** `AddPlatformData<TContext>` in `src/PS.AppPlatform/Data/PlatformDataExtensions.cs` registers all three. The existing adapter pattern (`PlatformDbContextFactoryAdapter`) is reused.
- **SQL convention**, since migrations are script-based: a tenant table has `TenantId UNIQUEIDENTIFIER NOT NULL` plus an index that leads with `TenantId`. A platform test helper, `TenancyConventions.AssertTenantColumns<TContext>()`, checks that every mapped `TenantEntity` table really has the column.
- **Background tasks become tenant-aware:**
  - `BackgroundTask` gets nullable `TenantId` and `CreatedByUserId`, via the new core script `020_AddBackgroundTaskTenancy.sql`.
  - `BackgroundTask` gets the same query filter, so `GetTasks`, `GetTaskById` and `GetNotificationTasks` are filtered with no endpoint changes. Without this, a multi-tenant app leaks every tenant's tasks.
  - `BackgroundTaskService` sets `TenantId`/`CreatedByUserId` on create. `TaskExecutionManager` and the status and lease updates use the unscoped factory; the execution manager then runs each handler in a DI scope whose `ITenantContext` is set to that task's tenant.
  - Timer and other non-HTTP triggers have no tenant, so they must use `CreateForTenant` or the unscoped factory.

### D5. Users, invitations and bootstrap

- **Users** are keyed by `(EntraObjectId, IssuerTenantId)`, the `oid` + `tid` claims. The pair is needed because the `common` audience admits identities from any directory.
- **Invitations:**
  1. The admin registers a user (display name, email as a label only) and places them in a team with role assignments.
  2. The system creates a single-use `Invitation` (a hashed token that expires after 7 days) and shows a link: `https://<management>/invite/<token>`.
  3. The recipient signs in there. `POST /api/invitations/{token}/accept` binds their `oid`/`tid` and consumes the token.
  4. Email is never used for matching.
- **Management app bootstrap:**
  - The management app is registered **in its own registry** (key `management`, one role, `admin`, `TenancyMode.Single`) by its `--migrate` run, a local database write.
  - `--bootstrap-admin --oid <oid> --tid <tid> --name <name>` ensures that identity is an `admin` in the `Default` team. It is idempotent.
  - The deploy workflow passes these values from **repository variables**. They are identifiers, not credentials, and a reusable workflow's `with:` cannot read `secrets`. The operator gets them from `az ad signed-in-user show` or a decoded token.

### D6. Front-end login flow (`@PS/app-client` + adapters)

- **Core:** `TenantSession` in `@PS/app-client`.
  - After sign-in it calls `GET /api/me/tenants`, which returns `[{ tenantId, name, roles }]`.
  - Zero tenants shows a "not registered" state. One tenant is selected automatically. Several tenants trigger the picker.
  - The choice is persisted in `localStorage`, per app and per account.
  - `ApiClient` adds `X-Tenant-Id`.
  - A `tenant_required` or `tenant_forbidden` response sends the user back to the picker.
  - `createPlatformClient()` returns `tenant` next to `auth`, `api` and `tasks`.
- **Angular:** `provideTenancy()`, `tenantGuard`, `<ps-tenant-picker>`, `<ps-tenant-switcher>`.
- **React:** `<TenantProvider>`, `useTenant()`, `<TenantPicker>`, `<TenantSwitcher>`.
- **Starters and template:** `dotnet new tinyapp --Tenancy none|single|multi`. `multi` adds a sample `TenantEntity`, the `Manifest`, `<PlatformTenancy>`, the `--register` deploy step and the picker route.

### D7. The management app (`apps/Management`)

- **Backend:** a tinyapp in `TenancyMode.Single`. The registry tables are **application-wide** `Entity` types, not `TenantEntity`, because the admin manages every application's data.
- **Two API surfaces:**
  - `/api/admin/*` requires `[Authorize(Roles="admin")]`. It provides CRUD for applications (read and deprecate only, since roles come from code), tenants, teams, users, memberships, role assignments and invitations.
  - `/api/registry/v1/*` is for apps (memberships lookup, authorized by application principal) and deployers (register, authorized by `trustedDeployers`).
- **Front-end:** Angular, based on the Angular starter and `@PS/app-client-angular`. Pages:
  - Applications list and detail (roles and tenants)
  - Tenant detail (teams)
  - Team detail (members and role assignments)
  - Users (register, invite link, disable, memberships across apps)
  - Invitation accept page
- **Deployed** through `app.bicep` like any other app, with its own database.

## Phases and steps

Each step links to its document in [multi-tenancy-v1/](multi-tenancy-v1/).

| Phase | Step | Outcome |
|---|---|---|
| **0 Prereqs** | [MT-01](multi-tenancy-v1/step-MT-01-command-line.md) | Generalize `MigrationEntryPoint` into `PlatformCommandLine` with a command registry (`--migrate` now; `--register` and `--bootstrap-admin` plug in later) |
| **1 Core tenancy** | [MT-02](multi-tenancy-v1/step-MT-02-tenancy-contracts.md) | `Tenancy/` namespace: `TenancyMode`, `AppManifest`, `ITenantContext`, `ITenantDirectory` + `ConfigTenantDirectory`, `AddPlatformTenancy` |
| | [MT-03](multi-tenancy-v1/step-MT-03-tenant-data.md) | `TenantEntity`, query filter, save interceptor, scoped/unscoped/`CreateForTenant` factories, fail-closed behaviour, `TenancyConventions` helper |
| | [MT-04](multi-tenancy-v1/step-MT-04-tenant-resolution.md) | `TenantResolver` inside the authorization middleware, `[TenantOptional]`, roles-to-claims, `AuthorizeAttribute.Roles` support, error codes |
| | [MT-05](multi-tenancy-v1/step-MT-05-tenant-tasks.md) | Tenant-aware background tasks: core script `020`, filtered task queries, tenant-scoped handler execution |
| | [MT-06](multi-tenancy-v1/step-MT-06-tenancy-shims.md) | Tenancy shims (`/api/me/tenants`), conditional injection via `<PlatformTenancy>` in the targets |
| | [MT-07](multi-tenancy-v1/step-MT-07-gate-d.md) | **Gate D:** `samples/MultiTenantSample` running on `ConfigTenantDirectory`. Two tenants, cross-tenant isolation proven through a running host, not DI-resolution tests (v1 §5 lesson) |
| **2 Management app** | [MT-08](multi-tenancy-v1/step-MT-08-management-backend.md) | `apps/Management` backend: registry schema scripts, `LocalRegistryTenantDirectory`, self-registration, `--bootstrap-admin` |
| | [MT-09](multi-tenancy-v1/step-MT-09-registry-api.md) | `/api/registry/v1`: register (trusted deployers) and memberships (app principals); `ManagementApiTenantDirectory` with cache; `--register` command |
| | [MT-10](multi-tenancy-v1/step-MT-10-admin-api.md) | `/api/admin/*` CRUD + invitations |
| | [MT-11](multi-tenancy-v1/step-MT-11-management-ui.md) | Management Angular UI |
| | [MT-12](multi-tenancy-v1/step-MT-12-gate-e.md) | **Gate E:** management app deployed; bootstrap admin signs in; creates a tenant, a team and an invite for a second account; that account accepts |
| **3 Front-end & template** | [MT-13](multi-tenancy-v1/step-MT-13-tenant-session.md) | `TenantSession` in `@PS/app-client` + tests |
| | [MT-14](multi-tenancy-v1/step-MT-14-tenancy-components.md) | Angular + React tenancy components; starters get the picker route |
| | [MT-15](multi-tenancy-v1/step-MT-15-template-and-deploy.md) | Template `--Tenancy` option, deploy workflow `--register` step, `docs/multi-tenancy.md` |
| | [MT-16](multi-tenancy-v1/step-MT-16-gate-f.md) | **Gate F:** `dotnet new tinyapp --Tenancy multi`, deploy, auto-register, admin assigns user to 2 tenants, picker appears, data isolated per tenant, anonymous endpoint writes via `CreateForTenant` |

## Requirement traceability

| Requirement | Decision | Steps |
|---|---|---|
| Single- and multi-tenant apps; multi-tenant from a few components | D3, D4 | MT-02, MT-03, MT-06, MT-15 |
| One identity in several tenants; standard login flow with tenant selection | D3, D6 | MT-04, MT-06, MT-13, MT-14 |
| Management app (single tenant) with a complete Angular UI and APIs for apps | D1, D7 | MT-08–MT-11 |
| App registers itself and its roles (deployment chosen) | D2 | MT-01, MT-09, MT-15 |
| Anonymous use of part of an app; tenant linking is the app's concern | D3, D4 | MT-03 (`CreateForTenant`), MT-04, MT-07 |
| Tenant-aware and application-wide tables, by base class | D4 | MT-03 |
| Filtered EF connection, and an unfiltered one | D4 | MT-03, MT-05 |
| Application → Tenant → Team → User → Role assignment; Default team | D1, D2, D7 | MT-08, MT-09 |
| Users always have a Microsoft identity; admin UI is the only registration path | D5 | MT-10, MT-11 |
| Management app is its own registration with role `admin`; bootstrap first admin at deploy | D5 | MT-08, MT-12 |

## Out of scope

- SQL row-level security as defence in depth
- Per-application admins (the management app has one global `admin` role)
- Self-service sign-up (the admin interface is the only way to register users)
- Entra group sync

## Existing code the steps must reuse, not reinvent

| Code | Location | Reused for |
|---|---|---|
| `PlatformDbContext.DiscoverEntityTypes` | `src/PS.AppPlatform/Data/PlatformDbContext.cs` | Finding `TenantEntity` types |
| `PlatformDbContextFactoryAdapter` | `src/PS.AppPlatform/Data/PlatformDataExtensions.cs` | The pattern for the new factories |
| `FunctionAuthorizationMiddleware` | `src/PS.AppPlatform/Auth/FunctionAuthorizationMiddleware.cs` | Attribute reflection. Extend it; don't duplicate it |
| `UsePlatform()` | `src/PS.AppPlatform/Hosting/PlatformHostBuilder.cs` | Registering the new middleware in order |
| `MigrationEntryPoint.BuildMigrationServices` | `src/PS.AppPlatform/Hosting/MigrationEntryPoint.cs` | Command-line runs |
| Core script numbering (000–099) | `DatabaseMigrator` | The new core script `020` |
| `ServiceBuilder` discovery | `src/PS.AppPlatform/Hosting/ServiceBuilder.cs` | `AppManifest` is discovered the same way |
| `app.bicep` output `identityPrincipalId` | `infra/app.bicep` | Passed to `--register` |

## Verification

- Every requirement maps to a decision (D1–D7) and to at least one step (see the traceability table).
- Every step document has a copy-pasteable verification command, an expected result, and "Done when" boxes that exercise behaviour through a running host (the lesson in v1 §5).
- Each gate (D, E, F) states a concrete, observable pass condition. Gate D in particular: tenant A's token plus header `B` → 403 `tenant_forbidden`; A's notes are never visible to B; the unscoped factory sees both.
