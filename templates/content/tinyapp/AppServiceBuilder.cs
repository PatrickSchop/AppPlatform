using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tasks;

namespace TinyApp;

public sealed class AppServiceBuilder : ServiceBuilder
{
    public override void BuildServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register your domain endpoints here.
        // Example: services.AddScoped<YourEndpoints>();
    }

    public override void RegisterBackgroundTasks(IBackgroundTaskCollection tasks)
    {
        // Register your background task handlers here.
        // Example: tasks.AddBackgroundTask<YourTaskHandler>("your-task-name");
    }
}

