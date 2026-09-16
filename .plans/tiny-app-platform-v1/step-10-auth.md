# Step 10 — Authentication and authorization

**Phase:** 1 — Core engine
**Depends on:** Step 09
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Replace the source's non-functional auth with a Functions-native, **default-deny**
authorization pipeline. This is the step that most needs doing properly, because every app
built on the platform inherits it.

## The problems being solved (analysis §6)

1. **No enforcement.** Exactly one `[Authorize]` exists in the whole source backend, on the
   static-content function. Every `/api/*` endpoint is `AuthorizationLevel.Anonymous` with
   no `[Authorize]`, including `api/tasks/check` and `api/initializeDatabase`. The
   `RequireAuthenticatedUser` policy at `Program.cs:63-66` is never referenced.
2. **The middleware cannot work as written.** `App\Host\AuthorizationMiddleware.cs` wraps
   ASP.NET Core's `AuthorizationMiddleware`, which reads policy from **endpoint metadata**.
   In the isolated worker there is no ASP.NET `Endpoint` on the `HttpContext`, so
   `[Authorize]` on a `[Function]` method is never seen. It always passes.
3. **CORS runs last** (`Program.cs:72-75`), so a 401 carries no `Access-Control-Allow-Origin`
   and the browser reports an opaque CORS error instead of the real status.
4. **Both middleware resolve from the root provider**, not the per-invocation scope.

## Design

Read `[Authorize]`/`[AllowAnonymous]` off the target method via
`FunctionContext.GetTargetFunctionMethod()` and evaluate policies directly through
`IAuthorizationService`. **Deny unless a function opts out.** Order: CORS → Authentication
→ Authorization.

## Tasks

### 1. `Auth/PlatformAuthenticationOptions.cs`

Bound from the `authentication` configuration section.

```csharp
public sealed class PlatformAuthenticationOptions
{
    public const string SectionName = "authentication";

    /// <summary>When false, all authorization passes. Defaults to true when an azureEntraId section exists.</summary>
    public bool? Enabled { get; set; }

    public AzureEntraIdOptions? AzureEntraId { get; set; }

    /// <summary>App Role every caller must hold, e.g. "stock.user". Null disables the role check.</summary>
    public string? RequiredRole { get; set; }

    /// <summary>Function names that are anonymous without needing [AllowAnonymous] in source.</summary>
    public string[] AnonymousFunctions { get; set; } = [];
}

public sealed class AzureEntraIdOptions
{
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    /// <summary>Extra accepted audiences, e.g. "api://{clientId}".</summary>
    public string[] AdditionalAudiences { get; set; } = [];
}
```

### 2. `Auth/PlatformAuthExtensions.cs`

```csharp
public static IServiceCollection AddPlatformAuthentication(this IServiceCollection services, IConfiguration configuration);
```

Behaviour:

1. Bind `PlatformAuthenticationOptions`.
2. `Enabled` resolves to `options.Enabled ?? (options.AzureEntraId is not null)`.
3. **When disabled:** register nothing but the options, and log **one Warning at startup**:
   `"Platform authentication is DISABLED. Every endpoint is publicly reachable. Set authentication:azureEntraId to enable."`
   This must be a warning, not information — a production app running unauthenticated by
   accident is the failure mode to make loud.
4. **When enabled:**
   - `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddMicrosoftIdentityWebApi(...)`,
     porting the source's `Program.cs:44-59` configuration: validate issuer, audience and
     lifetime; `Instance = "https://login.microsoftonline.com/"`; `TenantId`; `ClientId`.
   - Add `AdditionalAudiences` to `TokenValidationParameters.ValidAudiences` alongside
     `ClientId`. SPA tokens for a Web API are commonly issued for `api://{clientId}`, and
     accepting only the bare client id is a frequent cause of a valid token being rejected.
   - **Fail fast** if `TenantId` or `ClientId` is missing, or if `TenantId == ClientId`.
     That equality is the exact copy-paste error present at
     `C:\Dev\StockAnalysis\App\appsettings.json` (both are `6d91dfaa-…`, analysis §6), and it
     produces a confusing audience-validation failure at request time rather than at startup.
     Throw `InvalidOperationException` naming both keys and pointing at `docs/auth-setup.md`.
5. Register the authorization policies:
   - `PlatformPolicies.Default` — `RequireAuthenticatedUser()`, plus
     `RequireRole(options.RequiredRole)` when `RequiredRole` is set.
   - Set it as the `FallbackPolicy` **and** the `DefaultPolicy`.

   Use `RequireAssertion` over the `roles` and `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`
   claims rather than `RequireRole`, since Entra emits App Roles in the `roles` claim and
   the mapping is not always what `RequireRole` expects. Match ordinal-ignore-case.

### 3. `Auth/FunctionAuthorizationMiddleware.cs`

The core of the step. Replaces both source middleware.

```csharp
public sealed class FunctionAuthorizationMiddleware : IFunctionsWorkerMiddleware
{
    public Task Invoke(FunctionContext context, FunctionExecutionDelegate next);
}
```

Algorithm:

1. `var httpContext = context.GetHttpContext();` — if null (a non-HTTP trigger such as the
   Step 13 timer), call `next` and return. Timer triggers are not user-reachable.
2. Resolve **everything from `context.InstanceServices`**, the per-invocation scope — never
   from a captured root `IServiceProvider`. This is fix (4).
3. If auth is disabled, call `next` and return.
4. `var method = context.GetTargetFunctionMethod();`
5. Opt-out check, in this order:
   - `method` has `[AllowAnonymous]`, or its declaring type does → anonymous
   - `context.FunctionDefinition.Name` is in `options.AnonymousFunctions` → anonymous

   `AnonymousFunctions` exists because the shims in Step 13 are shipped source, and an app
   needs a way to open one up without editing package content.
6. Otherwise **authorization is required** (default-deny). Determine the policy:
   - `[Authorize(Policy = "X")]` on the method, else on the declaring type → policy `X`
   - no attribute at all → `PlatformPolicies.Default`

   That last line is the whole point: an endpoint with no attribute is **protected**, not open.
7. Authenticate: `var result = await httpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);`
   and set `httpContext.User = result.Principal` when it succeeds.
8. Evaluate: `await authorizationService.AuthorizeAsync(httpContext.User, resource: null, policyName)`.
9. On failure, short-circuit — do **not** call `next`:
   - not authenticated → `401`, with `WWW-Authenticate: Bearer`
   - authenticated but not authorized → `403`
   - write a small JSON body `{"error":"unauthorized"}` / `{"error":"forbidden"}`
   - log at Information with the function name and, on 403, the caller's `oid`. Never log
     the token.
10. On success, call `next`.

Do **not** wrap ASP.NET's `AuthorizationMiddleware`. That is problem (2) and the reason this
is written from scratch.

### 4. `Auth/PlatformAuthenticationMiddleware.cs`

Not needed as a separate middleware — step 7 above does the authentication inline, where the
result can actually be used. Do not port `App\Host\AuthenticationMiddleware.cs`.

Record that decision in a comment in `FunctionAuthorizationMiddleware`, so the next reader
does not "restore" it.

### 5. `Auth/CorsMiddleware.cs`

Port `App\Host\CORSMiddleware.cs` with its behaviour intact: read
`httpAccessControl:allowOrigin`, disable when empty, short-circuit `OPTIONS` with 204 and
the three `Access-Control-*` headers, and set `Access-Control-Allow-Origin` on the way out.
Keep the `catch (ObjectDisposedException)` — it handles a cancelled request.

Changes:
- Make it `public`.
- Add `Access-Control-Allow-Headers` value from config, defaulting to
  `Content-Type, Authorization`, and add `Vary: Origin` to the response.
- Support a comma-separated `allowOrigin` list and echo the matching request `Origin`.
  A single value plus a credentialed request is workable, but the Angular dev server and
  the deployed origin both need to work during development.
- **Set the CORS headers on the short-circuit paths too.** This is fix (3): the middleware
  must run *before* authorization, and the 401/403 responses that authorization writes must
  still carry `Access-Control-Allow-Origin`. Since CORS runs first and sets the header on
  the way out, this happens naturally — but add a test for it, because it is the exact
  regression being fixed.

### 6. Middleware ordering

In `Hosting/PlatformHostBuilder.cs`, add:

```csharp
/// <summary>
/// Registers platform middleware in the only correct order:
/// CORS first so that 401/403 responses carry Access-Control-Allow-Origin and preflight
/// is answered before authorization; authorization second.
/// </summary>
public static IFunctionsWorkerApplicationBuilder UsePlatform(this IFunctionsWorkerApplicationBuilder app)
{
    app.UseMiddleware<CorsMiddleware>();
    app.UseMiddleware<FunctionAuthorizationMiddleware>();
    return app;
}
```

The source order was Authentication → Authorization → CORS (`Program.cs:72-75`), which is
exactly backwards.

### 7. `Auth/AuthServiceBuilder.cs`

Register `CorsMiddleware` and `FunctionAuthorizationMiddleware` as singletons (Functions
middleware are singletons; per-request state comes from `FunctionContext.InstanceServices`),
and call `AddPlatformAuthentication`.

## Tests to add

`tests/Wisdi.AppPlatform.Tests/AuthorizationTests.cs`. Build a fake `FunctionContext` with a
`DefaultHttpContext` and a service provider containing the options and a real
`IAuthorizationService` from `AddAuthorization`.

1. **`Endpoint_without_attributes_is_denied`** — the headline default-deny test. An
   unauthenticated call to a method with no attributes gets 401 and `next` is never invoked.
2. `[AllowAnonymous]` on the method passes through.
3. `[AllowAnonymous]` on the declaring type passes through.
4. A function named in `AnonymousFunctions` passes through.
5. An authenticated principal with no required role gets **403**, not 401, when
   `RequiredRole` is set.
6. An authenticated principal holding the required role in the `roles` claim passes.
7. The role check is case-insensitive.
8. With auth disabled, an attribute-free endpoint passes and a warning was logged.
9. A null `HttpContext` (timer trigger) passes through.
10. **`Denied_response_carries_cors_headers`** — run `CorsMiddleware` then
    `FunctionAuthorizationMiddleware` over a denied request and assert the 401 response has
    `Access-Control-Allow-Origin`. This is fix (3).
11. `OPTIONS` short-circuits at 204 without reaching authorization.
12. `AddPlatformAuthentication` throws when `TenantId == ClientId`, and the message names
    both keys.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

**Expected:** zero warnings, zero errors, all tests pass.

## Done when

- [ ] Build clean, all tests pass
- [ ] `Endpoint_without_attributes_is_denied` passes — default-deny is real
- [ ] Attributes are read via `GetTargetFunctionMethod()`, not endpoint metadata
- [ ] `Denied_response_carries_cors_headers` passes — CORS runs first
- [ ] All services resolve from `context.InstanceServices`
- [ ] `tenantId == clientId` fails at startup with a message naming both keys
- [ ] The wrapped ASP.NET `AuthenticationMiddleware`/`AuthorizationMiddleware` were not ported
- [ ] Disabled auth logs a Warning, not an Information line

## Commit

```powershell
git add -A
git commit -m "Step 10: default-deny Functions-native authorization, CORS-first ordering, scoped resolution (fixes section 6)"
```
