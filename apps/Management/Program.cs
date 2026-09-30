using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PS.AppPlatform.Data;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using PS.Management.Registry;

namespace PS.Management;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (PlatformCommandLine.IsCommandRun(args))
            return await PlatformCommandLine.RunAsync<RegistryDbContext>(args);

        var builder = FunctionsApplication.CreateBuilder(args);

        builder.Configuration.AddPlatformConfiguration(typeof(Program).Assembly);

        var assemblies = new PlatformAssemblies();

        builder.Services.AddSingleton(assemblies);
        builder.Services.AddPlatformTenancy(builder.Configuration);

        builder.Services.AddPlatform(builder.Configuration, assemblies);
        builder.Services.AddPlatformData<RegistryDbContext>(builder.Configuration);

        // LocalRegistryTenantDirectory is registered in MT-08b.
        // builder.Services.AddScoped<ITenantDirectory, LocalRegistryTenantDirectory>();

        builder.ConfigureFunctionsWebApplication().UsePlatform();

        await builder.Build().RunAsync();
        return 0;
    }
}
