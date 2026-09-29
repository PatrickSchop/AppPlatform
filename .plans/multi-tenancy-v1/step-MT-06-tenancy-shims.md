# Step MT-06 — Tenancy endpoints and conditional shim injection

**Phase:** 1 — Core tenancy
**Depends on:** MT-05
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Give every `Single`/`Multi` app, with no code of its own:
- `GET /api/me/tenants`, the endpoint the login flow is built on (MT-13);
- a `tenancy` block in `/configuration.json`, so the SPA knows whether to run the tenant flow.

Apps that do not opt in must not get the shim, because it would fail DI resolution on its
first call.

## Tasks

### 1. `Endpoints/ITenancyEndpoints.cs` + `TenancyEndpoints.cs`

```csharp
public interface ITenancyEndpoints
{
    Task<IActionResult> GetMyTenantsAsync(HttpRequest request, CancellationToken ct = default);
}
```

Response (camelCase JSON, the contract MT-13 consumes):

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

- Unregistered user → `200 { "registered": false, "displayName": null, "tenants": [] }`.
  The SPA needs a clean "you are not registered" state, not an error to decode.
- Tenants ordered by name (ordinal, case-insensitive).
- Uses `ITenantDirectory` and `ClaimsPrincipal.GetIdentityKey()` (MT-04); it does not use
  `ITenantContext`, because the endpoint is `[TenantOptional]` and no tenant may be selected.

Register it in `AddPlatformTenancy`, **not** in `AddEndpointServices`.

### 2. The shim `src/PS.AppPlatform.Functions/endpoints/tenancy/TenancyFunctions.cs`

```csharp
public class TenancyFunctions(ITenancyEndpoints inner)
{
    [Function("GetMyTenants")]
    [Authorize]
    [TenantOptional]
    public Task<IActionResult> GetMyTenants(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/me/tenants")] HttpRequest req)
        => inner.GetMyTenantsAsync(req, req.HttpContext.RequestAborted);
}
```

Use the same namespace and `using` style as the existing shims in
`src/PS.AppPlatform.Functions/endpoints/`.

### 3. Conditional injection in `build/Ps.AppPlatform.Functions.targets`

```xml
<Project>
  <ItemGroup Condition="'$(PSAppPlatformInjectEndpoints)' != 'false'">
    <Compile Include="$(MSBuildThisFileDirectory)../endpoints/*.cs" Visible="false" PSAppPlatformEndpoint="true" />
  </ItemGroup>
  <ItemGroup Condition="'$(PSAppPlatformInjectEndpoints)' != 'false' And '$(PlatformTenancy)' == 'true'">
    <Compile Include="$(MSBuildThisFileDirectory)../endpoints/tenancy/*.cs" Visible="false" PSAppPlatformEndpoint="true" />
  </ItemGroup>
</Project>
```

`endpoints/*.cs` is top-level only, so the subfolder is not picked up by the first item.
**Verify** that `PS.AppPlatform.Functions.csproj`'s pack items include `endpoints/tenancy/*.cs`
by listing the nupkg contents (see Verification).

### 4. The pairing rule

**`AddPlatformTenancy` and `<PlatformTenancy>true</PlatformTenancy>` go together.** One without
the other gives either a missing endpoint or one that fails DI on its first call. Nothing can
read the MSBuild property at runtime, so:
- document the rule in `docs/multi-tenancy.md`;
- Gate D checks `functions.metadata` for `GetMyTenants`;
- the MT-15 template sets both from the same `--Tenancy` symbol, so generated apps cannot
  drift.

### 5. `/configuration.json` gains `tenancy`

In `ConfigurationEndpoints.GetWebAppConfigurationAsync`, when `TenancyMode != None`, add:

```json
"tenancy": { "mode": "Multi", "header": "X-Tenant-Id" }
```

to the returned object. An app-supplied `webApp:tenancy` key is overwritten, and the endpoint
logs a warning. Both values are non-secret (v1 §4.1a: everything here is public).

## Tests to add

1. `GetMyTenantsAsync` for a registered user returns both tenants, sorted, with roles.
2. Unregistered → 200 with `registered: false`.
3. Through the MT-04 `Invoke` harness: a user in two tenants with **no** header gets 200 (not
   409), because `[TenantOptional]` applies to the real shim class.
4. `/configuration.json` includes `tenancy` in `Multi` mode and omits it in `None`.
5. Architecture test: `endpoints/tenancy/*.cs` contains `[Function]` only in the Functions
   project, never in `PS.AppPlatform` (the existing guard must still pass).

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
dotnet pack src\PS.AppPlatform.Functions -c Release -o .\nupkg-check
Expand-Archive .\nupkg-check\PS.AppPlatform.Functions.*.nupkg .\nupkg-check\x -Force
Get-ChildItem .\nupkg-check\x -Recurse -Filter TenancyFunctions.cs
Remove-Item .\nupkg-check -Recurse -Force
```

**Expected:** tests pass; `TenancyFunctions.cs` is present under `endpoints/tenancy/` in the
package. `SampleApp` (no `PlatformTenancy`) builds, and its `functions.metadata` does **not**
list `GetMyTenants`.

## Done when

- [x] `GET /api/me/tenants` exists only in apps that opt in
- [x] Its response shape matches this document exactly (MT-13 depends on it)
- [x] The SPA can learn the tenancy mode from `/configuration.json`

## Commit

```powershell
git add -A
git commit -m "MT-06: /api/me/tenants shim, conditional tenancy shim injection, tenancy in configuration.json"
```
