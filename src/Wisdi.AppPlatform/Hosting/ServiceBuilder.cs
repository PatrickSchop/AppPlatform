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

    public virtual void RegisterBackgroundTasks(IBackgroundTaskCollection backgroundTasks) { }
}
