# Step MT-09 — Registry API, `ManagementApiTenantDirectory` and `--register`

**Phase:** 2 — Management app
**Depends on:** MT-08
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Close the loop between apps and the management app:
- **deployers** register an application and its roles (D2);
- **apps** look up a user's memberships at runtime with their managed identity (D1);
- the platform ships the client side of both: the `ManagementApiTenantDirectory` and the
  `--register` command.

## Tasks

### 1. Caller authorization (`apps/Management/Registry/RegistryCallers.cs`)

Both registry endpoints use `[Authorize(Policy = PlatformPolicies.AuthenticatedOnly)]` and
`[TenantOptional]`. The callers are service principals that are neither admins nor registry
users, so `requiredRole` and tenant resolution must not reject them first. Each endpoint then
checks its caller explicitly:

```csharp
public sealed class RegistryOptions
{
    public const string SectionName = "registry";
    public string TrustedTenantId { get; set; } = "";       // the operator's Entra tenant
    public string[] TrustedDeployers { get; set; } = [];    // object ids of deploy principals
}
```

| Endpoint | Caller must be |
|---|---|
| register | `tid == TrustedTenantId` **and** `oid ∈ TrustedDeployers` |
| memberships | `tid == TrustedTenantId` **and** `oid == Application.ServicePrincipalId` of the `{appKey}` in the route |

Anything else → 403 `{"error":"caller_not_allowed"}`, logged with oid. Fail at startup if
`TrustedTenantId` is empty outside development.

### 2. Endpoints (`apps/Management/Api/RegistryEndpoints.cs` + shim)

**`PUT /api/registry/v1/applications/{key}`**

```json
{ "displayName": "Recipes", "tenancy": "Multi",
  "roles": [ { "name": "editor", "displayName": "Editor" } ],
  "servicePrincipalId": "…" }
```

Validates with the same rules as `AppManifest` (MT-02; share the validator by moving it to a
`public static class AppManifestValidator` in the platform), requires route `key` to match
nothing else in the body, then calls `RegistryService.UpsertApplicationAsync` (MT-08).
Responds 200 with `RegistrationResult`.

**`GET /api/registry/v1/applications/{key}/users/{oid}/memberships?tid={tid}`**

Responds 200 with the MT-02 `UserMemberships` shape (camelCase), or **404** when
`MembershipQuery` returns null. A 404 is an answer, not an error: the caller caches it.

Both set `Cache-Control: no-store`.

### 3. Platform: `Tenancy/ManagementApiTenantDirectory.cs`

```csharp
public sealed class ManagementDirectoryOptions   // tenancy:management
{
    public string Url { get; set; } = "";         // https://management-api.azurewebsites.net
    public string Audience { get; set; } = "";    // api://<PS Apps API client id>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20); // covers a cold start
}
```

- Token: `IAzureIdentityProvider`'s credential (the app's user-assigned managed identity in
  Azure) → `GetTokenAsync(new TokenRequestContext([$"{Audience}/.default"]))`. Cache the token
  until 5 minutes before expiry.
- `HttpClient` from `IHttpClientFactory` (named client `"ps-registry"`), `Timeout` as above.
- 200 → memberships; 404 → null; anything else, or a timeout → throw
  `TenantDirectoryUnavailableException`.
- Register it for `tenancy:directory = management` in `AddPlatformTenancy`, replacing MT-02's
  `NotSupportedException`. Validate `Url` and `Audience` at startup.

### 4. Platform: `Tenancy/CachingTenantDirectory.cs`

A decorator applied to `management` and `local` directories (not `config`), keyed by
`(oid, tid)`:
- positive results cached for `tenancy:cacheDuration` (default 5 min);
- `null` cached for `min(cacheDuration, 1 min)`, so a newly accepted invitation takes effect quickly;
- **stale-if-error:** if the inner call throws `TenantDirectoryUnavailableException` and an
  expired entry younger than `tenancy:staleIfError` (default 1 h) exists, return it and log a
  warning. A management-app outage does not lock every user out, at the cost of revocations
  taking up to that long during the outage. Document the trade-off.
- concurrent misses for the same key share one inner call (`Lazy<Task<>>` in the cache entry).

`LocalRegistryTenantDirectory` (MT-08) drops its own cache and relies on this decorator.

### 5. `TenantResolver`: registry unavailable

Catch `TenantDirectoryUnavailableException` → `Stop(503, "registry_unavailable")` with
`Retry-After: 5`. Fail closed; never treat it as "not registered".

### 6. Platform: `--register` (`Tenancy/RegisterCommand.cs`)

```powershell
dotnet Recipes.dll --register --principal-id <managed identity principal id>
```

- `Name = "register"`, registered by the platform in the command graph whenever a manifest
  exists (tenancy `Single`/`Multi`). Reuses `ManagementDirectoryOptions` for URL and audience.
- Credential: `AzureCliCredential`, explicitly. The command runs on a GitHub runner after
  `azure/login` or on a developer machine after `az login`. Never the app's managed identity;
  D2 is precisely that apps cannot register themselves at runtime.
- Sends the manifest (MT-02) and `--principal-id` (required, GUID) to the PUT endpoint. Prints
  the `RegistrationResult`; exit 0. HTTP 403 → exit 1 with a message naming
  `registry:trustedDeployers` and the caller's oid (read it from the token).

### 7. Management app settings

`appsettings.json` gains `registry: { trustedTenantId: "", trustedDeployers: [] }` (set per
environment). Development: your own tenant id, and your own oid as a trusted deployer, so
`--register` can be tried locally.

## Tests to add

Management (`tests/Management.Tests`, through the `Invoke` harness from MT-04 so caller rules
run inside the real pipeline):
1. PUT by a trusted deployer → 200, application created; by an admin user who is not a
   deployer → 403 `caller_not_allowed`; from another `tid` → 403.
2. GET memberships by the app's principal → 200; by another app's principal → 403; unknown
   user → 404.
3. PUT with an invalid role name → 400 naming the role.

Platform (`tests/PS.AppPlatform.Tests`), with a fake `HttpMessageHandler` and a fake credential:
4. 200 / 404 / 500 / timeout map to memberships / null / exception / exception.
5. Caching: two calls within the window → one HTTP call; null is cached for the shorter period;
   concurrent misses → one HTTP call.
6. Stale-if-error returns the expired entry and logs; beyond `staleIfError` it throws.
7. Resolver: directory unavailable → 503 `registry_unavailable` (through `Invoke`).
8. `RegisterCommand` invoked end to end against a fake handler: correct body, exit 0; 403 → exit 1.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

Local end to end, with the management app running (`func start` in `apps\Management`, port
7071) and `MultiTenantSample` switched to `tenancy:directory = management`,
`tenancy:management:url = http://localhost:7071`, running on port 7072:

```powershell
cd samples\MultiTenantSample
dotnet run -- --register --principal-id <your own oid>   # locally you stand in for the app identity
```

**Expected:** exit 0, `Created: true`. A second run reports no changes. With your own oid as
the "service principal", `MultiTenantSample`'s lookups succeed. `GET /api/me/tenants` returns
the `Default` tenant only after you add yourself to it (via SQL until MT-10). Before that, it
returns `registered: false`.

## Done when

- [ ] Only trusted deployers can register; only an application's own identity can read its memberships
- [ ] Apps fail closed with 503 when the registry is unreachable and nothing stale is available
- [ ] `--register` works from a developer machine and is ready for the workflow (MT-15)

## Commit

```powershell
git add -A
git commit -m "MT-09: registry API, ManagementApiTenantDirectory with caching, --register command"
```
