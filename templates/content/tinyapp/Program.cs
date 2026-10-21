using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TinyApp.Data;
using PS.AppPlatform.Data;
using PS.AppPlatform.Hosting;
//#if (TenancyEnabled)
using Microsoft.Extensions.DependencyInjection;
using PS.AppPlatform.Tenancy;
//#endif

namespace TinyApp;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (PlatformCommandLine.IsCommandRun(args))
            return await PlatformCommandLine.RunAsync<AppDbContext>(args);

        var builder = FunctionsApplication.CreateBuilder(args);

        builder.Configuration.AddPlatformConfiguration(typeof(Program).Assembly);

        var assemblies = new PlatformAssemblies();
//#if (TenancyEnabled)
        // The registry (users, roles, tenants) is consulted on every request; see docs/multi-tenancy.md.
        builder.Services.AddSingleton(assemblies);
        builder.Services.AddPlatformTenancy(builder.Configuration);

//#endif
        builder.Services.AddPlatform(builder.Configuration, assemblies);
        builder.Services.AddPlatformData<AppDbContext>(builder.Configuration);

        builder.ConfigureFunctionsWebApplication().UsePlatform();

        await builder.Build().RunAsync();
        return 0;
    }
}

