using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Wisdi.AppPlatform.Hosting;

public sealed class HostingServiceBuilder : ServiceBuilder
{
    public override void BuildServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IHostingEnvironment, HostingEnvironment>();
        services.AddSingleton<IAzureIdentityProvider, AzureIdentityProvider>();
        services.AddHttpClient();
    }
}
