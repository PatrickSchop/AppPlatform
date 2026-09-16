# Step 13 — Functions shim package

**Phase:** 2 — Functions surface
**Depends on:** Step 12
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Build `Wisdi.AppPlatform.Functions`: the `[Function]` shims that must live in the
**consumer's** compilation, shipped as loose `.cs` files and injected by an auto-imported
`.targets` file.

## Why it works this way (analysis §2.1)

Functions worker indexing is on by default, and the function list comes from a Roslyn source
generator. A generator only sees the current compilation, so `[Function]` methods inside a
referenced assembly are invisible. Turning worker indexing off would fix that but costs
`canUsePlaceholder` — cold-start optimisation — which matters most on exactly the zero-cost
consumption plan this platform exists for.

So: logic in the package (Step 12), `[Function]` shims injected as source. A core upgrade
then ships new endpoints automatically, which a scaffolded copy would not.

## Tasks

### 1. `src/Wisdi.AppPlatform.Functions/Wisdi.AppPlatform.Functions.csproj`

A **packaging-only** project. It produces no `lib/` assembly.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <PackageId>Wisdi.AppPlatform.Functions</PackageId>
    <Description>Azure Functions endpoint shims for Wisdi.AppPlatform. Injects [Function] source into the consuming app so worker indexing can see it.</Description>
    <IsPackable>true</IsPackable>

    <!-- No assembly ships: the endpoints are source, injected into the consumer. -->
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <SuppressDependenciesWhenPacking>false</SuppressDependenciesWhenPacking>
    <NoWarn>$(NoWarn);NU5128</NoWarn>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>

  <!-- The shims are content, not compiled here. They are compiled by the consumer. -->
  <ItemGroup>
    <Compile Remove="endpoints\**\*.cs" />
    <None Include="endpoints\**\*.cs" Pack="true" PackagePath="endpoints" />
    <None Include="build\Wisdi.AppPlatform.Functions.targets" Pack="true" PackagePath="build" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Wisdi.AppPlatform\Wisdi.AppPlatform.csproj" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Timer" />
  </ItemGroup>

</Project>
```

`NU5128` is suppressed because packing dependencies without a `lib/` folder trips it; that is
exactly the shape intended here.

The package references are what the injected source needs to compile **in the consumer**, so
they must flow transitively — do not mark them `PrivateAssets="all"`.

### 2. `src/Wisdi.AppPlatform.Functions/build/Wisdi.AppPlatform.Functions.targets`

NuGet auto-imports `build/{PackageId}.targets` into the consuming project.

```xml
<Project>
  <ItemGroup Condition="'$(WisdiAppPlatformInjectEndpoints)' != 'false'">
    <Compile Include="$(MSBuildThisFileDirectory)../endpoints/*.cs"
             Visible="false"
             WisdiAppPlatformEndpoint="true" />
  </ItemGroup>
</Project>
```

`Visible="false"` keeps them out of Solution Explorer — they are not the app's source.
`WisdiAppPlatformInjectEndpoints=false` is the escape hatch for an app that wants to hand-write
its own shims.

### 3. Write the shims

Under `src/Wisdi.AppPlatform.Functions/endpoints/`. Namespace **`Wisdi.AppPlatform.Generated`** —
not the app's namespace, so injected source can never collide with app types.

Every shim follows this shape exactly. No logic, no try/catch, no logging:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Wisdi.AppPlatform.Endpoints;

namespace Wisdi.AppPlatform.Generated;

public class BackgroundTaskFunctions(IBackgroundTaskEndpoints inner)
{
    [Function("GetNotificationTasks")]
    [Authorize]
    public Task<IActionResult> GetNotifications(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/tasks/notifications")] HttpRequest req)
        => inner.GetNotificationsAsync(req, req.HttpContext.RequestAborted);
}
```

`AuthorizationLevel.Anonymous` refers to the Functions **host key** check, which the platform
does not use; the `[Authorize]` attribute is what Step 10's middleware reads. Put that in a
comment at the top of each file — it looks contradictory otherwise.

Create these files:

**`BackgroundTaskFunctions.cs`** — all `[Authorize]`:

| Function name | Route | Method | Delegates to |
|---|---|---|---|
| `GetNotificationTasks` | `api/tasks/notifications` | get | `GetNotificationsAsync` |
| `GetTasks` | `api/tasks` | get | `GetAllAsync` |
| `CreateTask` | `api/tasks` | post | `CreateAsync` |
| `GetTaskStatus` | `api/tasks/{id}` | get | `GetByIdAsync(req, id)` |
| `CheckTasks` | `api/tasks/check` | post | `CheckAsync` |

Note `GetTasks` and `CreateTask` share a route and differ by verb — that is fine, but they
must be two `[Function]`s with distinct names.

**`ConfigurationFunctions.cs`** — `GetWebAppConfiguration`, route `configuration.json`, get,
**`[AllowAnonymous]`**. The SPA fetches this before it has a token, so it cannot be protected.
Add a comment saying so, and repeating that only non-secret values belong under `webApp`.

**`HealthFunctions.cs`** — `GetHealth`, route `api/health`, get, `[AllowAnonymous]`.

**`DatabaseFunctions.cs`** — `InitializeDatabase`, route `api/initializeDatabase`, post,
`[Authorize]`. The service applies the second gate (Step 12).

**`StaticContentFunctions.cs`** — `StaticContent`, route `{*path}`, get, **`[AllowAnonymous]`**.

```csharp
// The SPA shell is public by design. Security is enforced by the API, not by withholding
// the bundle: every /api/* endpoint is default-deny (see Auth/FunctionAuthorizationMiddleware),
// so an unauthenticated visitor can load the app and reach no data at all. Serving static
// HTML and JS to anyone is harmless; handling 401/403 gracefully is the front-end's job.
// This differs deliberately from the source, where Static.cs:25 carries [Authorize].
[Function("StaticContent")]
[AllowAnonymous]
public Task<IActionResult> Run(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{*path}")] HttpRequest req,
    string path = "")
    => inner.HandleAsync(req, path, req.HttpContext.RequestAborted);
```

The catch-all must be registered last in effect; Functions routes literal segments ahead of a
catch-all, so `api/tasks` still wins. Verify that at Step 15 rather than assuming it.

**Why this is the right call and not a weakening.** A browser's initial navigation carries no
bearer token, so `[Authorize]` on the shell can only work if something *in front of* the
worker authenticates and redirects — App Service Easy Auth, or a gateway. Depending on that
couples every app to a hosting-layer feature, and it protects nothing that matters: the data
lives behind `/api/*`, which is default-deny either way.

**The platform's security model, stated once here and enforced everywhere:**

> The API is the security boundary. No data is reachable unauthorized, regardless of who
> downloaded the bundle. The SPA is public; every `/api/*` endpoint is default-deny. A
> front-end that mishandles a 403 is a bug, not a vulnerability.

That model is what makes the `AnonymousFunctions` opt-out in Step 10 safe to have at all, and
it is why `StaticContent`, `GetHealth` and `GetWebAppConfiguration` can be anonymous without
anyone needing to think hard about it.

**`TaskSchedulerFunctions.cs`** — the analysis §7.3 fix:

```csharp
public class TaskSchedulerFunctions(ITaskExecutionManager manager, ILogger<TaskSchedulerFunctions> logger)
{
    /// <summary>
    /// Safety net for queued tasks. The primary trigger is a fire-and-forget self-POST from
    /// BackgroundTaskService, which silently does nothing if apiBaseUrl is wrong, if the app
    /// scaled out to another instance, or if the task was created from the --migrate CLI path.
    /// </summary>
    [Function("ScheduledTaskCheck")]
    public async Task Run([TimerTrigger("0 */5 * * * *")] TimerInfo timer)
    {
        await manager.CheckAndStartTasksAsync();
    }
}
```

Make the schedule configurable: `[TimerTrigger("%backgroundTasks:checkSchedule%")]` reads an
app setting, so an app can tune it without editing package source. Provide the default
`0 */5 * * * *` in the template's settings (Step 18) and document that the `%…%` syntax reads
**app settings**, so it must be set in `local.settings.json`/Function App settings, not only
in `appsettings.json`.

A timer trigger needs `AzureWebJobsStorage`, which the consumption plan already has.
`GetHttpContext()` returns null for it, and Step 10's middleware passes those through.

### 4. Register the shim source in the sample-app case

A `ProjectReference` does not flow `build/*.targets` the way a `PackageReference` does. The
sample app (Step 14) and the template's dev-time configuration therefore include the shims
directly:

```xml
<ItemGroup>
  <Compile Include="..\..\src\Wisdi.AppPlatform.Functions\endpoints\*.cs" Visible="false" />
</ItemGroup>
```

Document this in `src/Wisdi.AppPlatform.Functions/README.md`, because it is a genuine trap:
the targets path is only exercised once Step 16 packs and consumes from a local feed, and
Step 16 must verify it explicitly.

### 5. Add to the solution

```powershell
dotnet sln add src\Wisdi.AppPlatform.Functions\Wisdi.AppPlatform.Functions.csproj
```

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

**Expected:** builds clean. Note that the shims are compiled **nowhere** at this point — that
is by design, and Step 14 is what first compiles them. A typo here will not surface until then.

Also confirm the package has no assembly:

```powershell
dotnet pack src\Wisdi.AppPlatform.Functions\Wisdi.AppPlatform.Functions.csproj -o artifacts
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::OpenRead((Resolve-Path artifacts\Wisdi.AppPlatform.Functions.0.1.0.nupkg)).Entries | Select-Object FullName
```

**Expected entries:** `endpoints/*.cs` and `build/Wisdi.AppPlatform.Functions.targets`.
**There must be no `lib/` entry.** A `lib/net10.0/Wisdi.AppPlatform.Functions.dll` means
`IncludeBuildOutput` did not take effect and the shims would be invisible at runtime — the
precise failure §2.1 exists to avoid.

## Done when

- [ ] `dotnet build` clean
- [ ] The `.nupkg` contains `endpoints/*.cs` and `build/*.targets` and **no** `lib/` folder
- [ ] Every shim is a single expression-bodied delegation with no logic
- [ ] Shims live in `Wisdi.AppPlatform.Generated`, not an app namespace
- [ ] The timer trigger exists, with a configurable schedule
- [ ] `StaticContentFunctions`, `ConfigurationFunctions` and `HealthFunctions` are
      `[AllowAnonymous]`; every data endpoint is `[Authorize]`
- [ ] The security-model paragraph is in the file as a comment, so the next reader does not
      "fix" the anonymous shell
- [ ] The ProjectReference-vs-PackageReference trap is documented

## Commit

```powershell
git add -A
git commit -m "Step 13: Wisdi.AppPlatform.Functions source-injected [Function] shims and timer safety net (fixes 7.3)"
```
