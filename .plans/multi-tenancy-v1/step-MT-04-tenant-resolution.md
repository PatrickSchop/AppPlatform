# Step MT-04 — Tenant resolution and registry roles

**Phase:** 1 — Core tenancy
**Depends on:** MT-03
**Working directory:** `C:\Dev\AppPlatform`

## Goal

On every protected HTTP invocation of a `Single` or `Multi` app: identify the registered user,
pick and validate the tenant, fill `ITenantContext`, and make the user's registry roles the
principal's roles, all **before** authorization runs. That way `requiredRole` and
`[Authorize(Roles = "...")]` enforce registry roles with no new authorization code in apps.

## Where it goes

`FunctionAuthorizationMiddleware` (`src/PS.AppPlatform/Auth/FunctionAuthorizationMiddleware.cs`)
already does anonymous check → authenticate → authorize in one `Invoke`. Tenant resolution
must sit between authenticate and authorize, so it is a **service the middleware calls**, not
a separate middleware:

```
anonymous? ── yes ──> next()
   │ no
authenticate (unchanged)
   │
TenantResolver.ResolveAsync  ── Stop(status, code) ──> write JSON error, return
   │ Continue
authorize (policy now includes Roles)  ── fail ──> 401 / 403 (unchanged)
   │
next()
```

In mode `None` the resolver is never invoked, so behaviour is byte-for-byte v1.

## Tasks

### 1. `Tenancy/TenantOptionalAttribute.cs`

```csharp
/// <summary>
/// The endpoint needs an authenticated user but not a selected tenant, e.g. GET /api/me/tenants.
/// It runs for unregistered users too, with ITenantContext unresolved.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class TenantOptionalAttribute : Attribute { }
```

### 2. `Auth/ClaimsPrincipalIdentityExtensions.cs`

```csharp
public static IdentityKey? GetIdentityKey(this ClaimsPrincipal user);
```

Read `oid`, falling back to `http://schemas.microsoft.com/identity/claims/objectidentifier`;
read `tid`, falling back to `http://schemas.microsoft.com/identity/claims/tenantid`.
Microsoft.Identity.Web may map inbound claim types, so check both. Return null if either is
missing.

### 3. `Tenancy/TenantResolver.cs`

```csharp
public abstract record TenantResolution
{
    public sealed record Continue : TenantResolution;
    public sealed record Stop(int StatusCode, string Error) : TenantResolution;
}

public sealed class TenantResolver(
    TenancyOptions options, AppManifest manifest, ITenantDirectory directory, TenantContext context)
{
    public Task<TenantResolution> ResolveAsync(HttpContext http, bool tenantOptional, CancellationToken ct);
}
```

Algorithm:

1. User not authenticated → `Continue` (authorization produces the 401, as today).
2. `GetIdentityKey()` null → `Stop(403, "not_registered")`.
3. `directory.GetMembershipsAsync(...)` returns null, or no tenants:
   - `tenantOptional` → `Continue`, context left unresolved;
   - otherwise → `Stop(403, "not_registered")`.
4. Read header `options.TenantHeader`:
   - present but not a GUID, or not among the user's tenants → `Stop(403, "tenant_forbidden")`,
     **even when `tenantOptional`**, because a forged header is never silently ignored;
   - present and valid → that tenant.
5. No header:
   - exactly one tenant → that tenant (this covers every `Single`-mode user);
   - several and `tenantOptional` → user only (`context.SetUser(userId)`), `Continue`;
   - several otherwise → `Stop(409, "tenant_required")`.
6. `context.Set(userId, tenantId, teamIds, roles)`.
7. **Replace roles on the principal:** build a new `ClaimsIdentity` that copies every claim
   of the authenticated identity **except** `roles` and `ClaimTypes.Role`, adds one `roles`
   claim per registry role, and sets `roleType: "roles"`. Assign it as `http.User`. Dropping
   token roles makes the registry the only authority; an Entra App Role on the shared API
   registration must not grant anything in a registry app.

Add `SetUser(Guid userId)` to `TenantContext` for the user-without-tenant case (MT-02 type).

### 4. Wire into `FunctionAuthorizationMiddleware`

- After `httpContext.User = authResult?.Principal ?? new();` resolve `TenancyMode`; when not
  `None`, resolve `TenantResolver` from `context.InstanceServices` (the per-invocation scope,
  the same rule as the scope-leak fix in v1 Step 10) and call it.
- On `Stop`: status, `Content-Type: application/json`, body `{"error":"<code>"}`, log at
  Information with function name and oid, return. Keep the body shape identical to the
  existing `unauthorized` and `forbidden` bodies.
- `tenantOptional` = `[TenantOptional]` on the method or the declaring type.
- **Cache `GetTargetFunctionMethod` per function name** in a static `ConcurrentDictionary`.
  It scans every loaded assembly on every request today, and this step adds attribute lookups
  to the hot path.

### 5. Honour `AuthorizeAttribute.Roles`

Replace `GetAuthorizationPolicy`'s policy-name lookup with ASP.NET Core's own combiner, so
`Policy`, `Roles` and `AuthenticationSchemes` on method **and** class all apply, as they do
in ASP.NET Core:

```csharp
var authorizeData = method.GetCustomAttributes<AuthorizeAttribute>()
    .Concat(method.DeclaringType?.GetCustomAttributes<AuthorizeAttribute>() ?? []);
var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authorizeData)
             ?? await policyProvider.GetDefaultPolicyAsync();
```

**`CombineAsync` does not include the default policy** when an attribute carries only
`Roles`. Combine it explicitly when **no attribute names a `Policy`**:
`AuthorizationPolicy.Combine(defaultPolicy, combined)`. Without that, `[Authorize(Roles="x")]`
would drop `requiredRole`. Test 7 covers this.

A named `Policy` **replaces** the default, as in ASP.NET Core. That is the only way to opt out
of `requiredRole`, and it has to be written explicitly. Register one platform policy for it in
`AddPlatformAuthentication`:

```csharp
public static class PlatformPolicies
{
    public const string Default = "";
    /// <summary>Authenticated, ignoring requiredRole. For callers that cannot hold the app's role:
    /// deploy principals, other apps' managed identities, invitees (MT-09, MT-10).</summary>
    public const string AuthenticatedOnly = "platform:authenticated";
}
```

### 6. Register

`AddPlatformTenancy` registers `TenantResolver` scoped. `AddPlatformAuth` needs no change.

## Tests to add

`tests/PS.AppPlatform.Tests/TenantResolutionTests.cs`.

**These must invoke `FunctionAuthorizationMiddleware.Invoke`**, not just construct the
resolver (v1 §5). Build a small harness in the test project:
- a substituted `FunctionContext` whose `InstanceServices` is a real scoped provider;
- `Items["HttpRequestContext"]` holding a `DefaultHttpContext`, which is how `GetHttpContext()`
  finds it;
- a `FunctionDefinition.EntryPoint` pointing at a test shim class with **instance** methods;
- a real `IAuthenticationService` replaced by a stub scheme that authenticates from test
  headers, so no JWT is needed.

Scenarios (`ConfigTenantDirectory` with tenants A and B; user U in both, user V in A only):

1. Mode `None`: behaviour identical to today (no token 401, token 200); the resolver is never called.
2. V, no header → 200, `ITenantContext.TenantId == A` observed by the shim.
3. U, no header → 409 `tenant_required`.
4. U, header B → 200 with tenant B; header C (not a member) → 403 `tenant_forbidden`; header
   `garbage` → 403 `tenant_forbidden`.
5. Unknown oid → 403 `not_registered`; known oid with a different tid → 403 `not_registered`.
6. `[TenantOptional]` shim: U with no header → 200, user set, tenant null; unknown oid → 200,
   unresolved; U with a bad header → 403.
7. `[Authorize(Roles="editor")]`: U in A (editor) → 200; U in B (viewer) → 403. With
   `requiredRole` also set, the role-only attribute still enforces `requiredRole`.
8. A token carrying an Entra `roles: editor` claim for a user who is only a viewer → 403.
   Token roles are dropped.
8a. With `requiredRole` set, a shim with `[Authorize(Policy = PlatformPolicies.AuthenticatedOnly)]`
   → 200 for an authenticated user without the role, 401 without a token.
9. `[AllowAnonymous]` shim → 200 with no token and no directory call.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

**Expected:** all tests pass, including the pre-existing `AuthorizationTests`. `SampleApp` is
unchanged: start it with `func start` and confirm `/api/health` 200, `/api/notes` 401 with no
token.

## Done when

- [x] Tenant resolution runs between authentication and authorization, only in `Single`/`Multi`
- [x] The five outcomes (`tenant_required`, `tenant_forbidden`, `not_registered`, 401, 403)
      are asserted through `Invoke`
- [x] Registry roles replace token roles on the principal
- [x] `[Authorize(Roles=...)]` works and never bypasses the default policy
- [x] Target-method lookup is cached

## Commit

```powershell
git add -A
git commit -m "MT-04: tenant resolution in the authorization pipeline, registry roles, Authorize(Roles)"
```
