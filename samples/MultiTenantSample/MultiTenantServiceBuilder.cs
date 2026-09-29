using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MultiTenantSample.Api;
using MultiTenantSample.Tasks;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tasks;

namespace MultiTenantSample;

public sealed class MultiTenantServiceBuilder : ServiceBuilder
{
    public override void BuildServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ProjectsEndpoints>();
        services.AddScoped<CountriesEndpoints>();
        services.AddScoped<FeedbackEndpoints>();
        services.AddScoped<ReportsEndpoints>();
    }

    public override void RegisterBackgroundTasks(IBackgroundTaskCollection tasks)
        => tasks.AddBackgroundTask<ProjectCountTaskHandler>("projectcount");
}
