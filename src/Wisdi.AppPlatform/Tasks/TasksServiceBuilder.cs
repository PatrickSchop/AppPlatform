using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PS.AppPlatform.Hosting;

namespace PS.AppPlatform.Tasks;

public sealed class TasksServiceBuilder : ServiceBuilder
{
    public override void BuildServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ExecutionManagerIdentity>();
        services.AddSingleton<TaskCheckGate>();
    }
}

