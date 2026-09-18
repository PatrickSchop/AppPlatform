using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SampleApp.Api;
using SampleApp.Tasks;
using Wisdi.AppPlatform.Hosting;
using Wisdi.AppPlatform.Tasks;

namespace SampleApp;

public sealed class SampleServiceBuilder : ServiceBuilder
{
    public override void BuildServices(IServiceCollection services, IConfiguration configuration)
        => services.AddScoped<NotesEndpoints>();

    public override void RegisterBackgroundTasks(IBackgroundTaskCollection tasks)
        => tasks.AddBackgroundTask<WordCountTaskHandler>("wordcount");
}
