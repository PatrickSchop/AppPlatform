using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SampleApp.Data;
using PS.AppPlatform.Data;
using PS.AppPlatform.Hosting;

namespace SampleApp;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (MigrationEntryPoint.IsMigrationRun(args))
            return await MigrationEntryPoint.RunAsync<AppDbContext>(args);

        var builder = FunctionsApplication.CreateBuilder(args);

        builder.Configuration.AddPlatformConfiguration(typeof(Program).Assembly);

        var assemblies = new PlatformAssemblies();
        builder.Services.AddPlatform(builder.Configuration, assemblies);
        builder.Services.AddPlatformData<AppDbContext>(builder.Configuration);

        builder.ConfigureFunctionsWebApplication().UsePlatform();

        await builder.Build().RunAsync();
        return 0;
    }
}

