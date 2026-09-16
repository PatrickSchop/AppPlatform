# Step 03 — Hosting and Azure identity

**Phase:** 1 — Core engine
**Depends on:** Step 02
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Port the bootstrap layer: the `ServiceBuilder` DI-module convention, the **explicit
assembly list** that replaces `Assembly.GetExecutingAssembly()`, assembly-relative config
layering, the hosting environment, and `AzureIdentityProvider` (the four-mode credential
switch — the single most reusable file in the source repo).

## Reference material (read-only)

| Source | Use |
|---|---|
| `C:\Dev\StockAnalysis\App\ServiceBuilder.cs` | The base class shape |
| `C:\Dev\StockAnalysis\App\Program.cs` lines 150-190 | `ConfigureConfigFiles`, `BuildServices` |
| `C:\Dev\StockAnalysis\App\Host\AzureIdentityProvider.cs` | Port nearly verbatim |
| `C:\Dev\StockAnalysis\App\Host\HostingEnvironment.cs`, `EnvironmentType.cs`, `IHostingEnvironment.cs` | Port verbatim |

## Tasks

### 1. `Hosting/EnvironmentType.cs`

```csharp
namespace Wisdi.AppPlatform.Hosting;

public enum EnvironmentType
{
    Development,
    Production
}
```

### 2. `Hosting/IHostingEnvironment.cs` and `Hosting/HostingEnvironment.cs`

Port from the source. **Change `internal` to `public`** on both — the source marks
`IHostingEnvironment` internal, which would make it unusable from a consuming app.

`HostingEnvironment` reads the `DEV_ENVIRONMENT` configuration value; `"development"`
(case-insensitive) maps to `EnvironmentType.Development`, anything else to `Production`.

### 3. `Hosting/IAzureIdentityProvider.cs` and `Hosting/AzureIdentityProvider.cs`

Port from `App\Host\AzureIdentityProvider.cs`. Make the class `public`.

Behaviour to preserve exactly — it reads the `azureIdentity` config section and switches
on `type`:

| `type` | Credential |
|---|---|
| `systemAssigned` | `new ManagedIdentityCredential()` |
| `userAssigned` | `new ManagedIdentityCredential(clientId)` |
| `azureCli` | `new AzureCliCredential()` |
| absent/empty, and environment is Development | `new DefaultAzureCredential()` |
| anything else | throw `InvalidOperationException` |

Keep the log line for each branch — it is how you diagnose a credential problem in
production.

### 4. `Hosting/ServiceBuilder.cs`

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wisdi.AppPlatform.Tasks;

namespace Wisdi.AppPlatform.Hosting;

/// <summary>
/// A DI module. Subclasses are discovered by reflection across the assemblies
/// registered in PlatformAssemblies and instantiated with a parameterless constructor.
/// </summary>
/// <remarks>
/// Discovery order is reflection order and therefore NOT deterministic. Modules must
/// not depend on another module having run first. Cross-module wiring goes through
/// resolver lambdas (services.AddSingleton&lt;IFoo&gt;(sp =&gt; ...)), which defer
/// resolution to first use. This is a contract, not an accident.
/// </remarks>
public abstract class ServiceBuilder
{
    public virtual void BuildServices(IServiceCollection services, IConfiguration configuration) { }

    /// <summary>Register background task handlers. Called after BuildServices on all modules.</summary>
    public virtual void RegisterBackgroundTasks(IBackgroundTaskCollection backgroundTasks) { }
}
```

**Two deliberate changes from the source** (analysis §7.5, §7.6):
- The non-determinism is now documented as a contract in the XML remarks.
- The dead `BuildConfiguration` hook is **dropped**, not ported. It was never called.

`IBackgroundTaskCollection` does not exist until Step 06. Until then, either comment out
that method and the `using`, or create a one-line placeholder interface in
`Tasks/IBackgroundTaskCollection.cs` that Step 06 will fill in. **Prefer the placeholder**
so this file is never edited again.

### 5. `Hosting/PlatformAssemblies.cs`

This is the fix for analysis §2.2 — every reflection scan takes an explicit list.

```csharp
using System.Reflection;

namespace Wisdi.AppPlatform.Hosting;

/// <summary>
/// The set of assemblies scanned for ServiceBuilder modules and Entity subclasses.
/// Always contains the platform assembly and the entry assembly; apps add extras.
/// </summary>
public sealed class PlatformAssemblies
{
    private readonly List<Assembly> _assemblies = new();

    public PlatformAssemblies()
    {
        Add(typeof(PlatformAssemblies).Assembly);
        var entry = Assembly.GetEntryAssembly();
        if (entry is not null) Add(entry);
    }

    public PlatformAssemblies Add(Assembly assembly)
    {
        if (!_assemblies.Contains(assembly)) _assemblies.Add(assembly);
        return this;
    }

    public PlatformAssemblies AddContaining<T>() => Add(typeof(T).Assembly);

    public IReadOnlyList<Assembly> All => _assemblies;
}
```

`Assembly.GetEntryAssembly()` returns null in some test hosts, hence the null guard.

### 6. `Hosting/PlatformConfiguration.cs`

Port `ConfigureConfigFiles` from `Program.cs`, generalised.

```csharp
public static class PlatformConfiguration
{
    /// <summary>
    /// Replaces the default JSON sources with assembly-relative appsettings files.
    /// The Functions host sets a base path that is not the assembly directory, which is
    /// why the defaults are removed rather than added to.
    /// </summary>
    public static IConfigurationBuilder AddPlatformConfiguration(
        this IConfigurationBuilder builder,
        Assembly anchorAssembly,
        string? environmentName = null,
        string? extraSettingsFile = null);

    /// <summary>Reads DEV_ENVIRONMENT, defaulting to "production". Always lower-cased.</summary>
    public static string ResolveEnvironmentName();
}
```

Behaviour:
1. Remove every existing `JsonConfigurationSource` from `builder.Sources`.
2. `basePath = Path.GetDirectoryName(anchorAssembly.Location)`.
3. Add `appsettings.json` (optional) then `appsettings.{environment}.json` (optional).
4. If `extraSettingsFile` is given, add it as **required** — resolve it against `basePath`
   when it is a relative path. This is the `--settingsFile` path used by `--migrate`.
5. Add environment variables last.

### 7. `Hosting/PlatformHostBuilder.cs`

The reflection-based module discovery, lifted out of `Program.BuildServices` and given an
explicit assembly list.

```csharp
public static class PlatformHostBuilder
{
    /// <summary>
    /// Discovers every non-abstract ServiceBuilder in the given assemblies, runs
    /// BuildServices then RegisterBackgroundTasks on each, and registers the resulting
    /// task handlers.
    /// </summary>
    public static IServiceCollection AddPlatform(
        this IServiceCollection services,
        IConfiguration configuration,
        PlatformAssemblies assemblies);
}
```

Implementation notes:
- Register `assemblies` itself as a singleton so `PlatformDbContext` (Step 04) can use it.
- Collect `ServiceBuilder` subclasses from `assemblies.All`, skipping abstract types and
  types without a public parameterless constructor.
- Two passes, exactly as the source does: all `BuildServices` first, then all
  `RegisterBackgroundTasks` into one `BackgroundTaskCollection`, then
  `collection.RegisterBackgroundTaskHandlers(services)`.
- Wrap `assembly.GetTypes()` in a try/catch for `ReflectionTypeLoadException` and use
  `ex.Types.Where(t => t is not null)`. A consuming app may reference an assembly whose
  dependencies are not all present.

Until Step 06 exists, the background-task half can be a no-op — but leave the two-pass
structure in place so Step 06 only fills in a body.

### 8. `Hosting/HostingServiceBuilder.cs`

The platform's own module, registering what this step produced.

```csharp
public sealed class HostingServiceBuilder : ServiceBuilder
{
    public override void BuildServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IHostingEnvironment, HostingEnvironment>();
        services.AddSingleton<IAzureIdentityProvider, AzureIdentityProvider>();
        services.AddHttpClient();
    }
}
```

**Do not port the `AzureOpenAIClient` registration here.** In the source it lives in
`Host/ServiceBuilder.cs`, but it belongs with the LLM module — Step 11.

## Tests to add

In `tests/Wisdi.AppPlatform.Tests/HostingTests.cs`:

1. `HostingEnvironment` maps `"development"`, `"Development"`, `"production"`, `null` and
   `"nonsense"` correctly.
2. `AzureIdentityProvider` throws `InvalidOperationException` when `type` is unknown, and
   when `type` is empty in a Production environment.
3. `PlatformAssemblies` always contains the platform assembly and de-duplicates `Add`.
4. `AddPlatform` discovers a test-local `ServiceBuilder` subclass and runs it.

Use `ConfigurationBuilder().AddInMemoryCollection(...)` to build the configuration; do
not touch any file or any real Azure endpoint.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

**Expected:** zero warnings, zero errors; all tests pass (1 from Step 02 plus the new ones).

## Done when

- [ ] Build clean, all tests pass
- [ ] `IHostingEnvironment` and `AzureIdentityProvider` are `public`, not `internal`
- [ ] No file in `Hosting/` calls `Assembly.GetExecutingAssembly()`
- [ ] The `BuildConfiguration` hook was dropped, not ported
- [ ] `ServiceBuilder` carries the discovery-order contract in its XML remarks

## Commit

```powershell
git add -A
git commit -m "Step 03: hosting, config layering, explicit assembly scanning, Azure identity"
```
