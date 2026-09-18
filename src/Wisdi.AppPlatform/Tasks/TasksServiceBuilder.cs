using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wisdi.AppPlatform.Hosting;

namespace Wisdi.AppPlatform.Tasks;

public sealed class TasksServiceBuilder : ServiceBuilder
{
    public override void BuildServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ExecutionManagerIdentity>();
        services.AddSingleton<TaskCheckGate>();
    }
}
