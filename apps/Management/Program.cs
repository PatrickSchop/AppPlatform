using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PS.AppPlatform.Data;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using PS.Management.Api;
using PS.Management.Registry;

namespace PS.Management;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (PlatformCommandLine.IsCommandRun(args))
            return await PlatformCommandLine.RunAsync<RegistryDbContext>(args, configureServices: ManagementCommands.Register);

        var builder = FunctionsApplication.CreateBuilder(args);

        builder.Configuration.AddPlatformConfiguration(typeof(Program).Assembly);

        var assemblies = new PlatformAssemblies();

        builder.Services.AddSingleton(assemblies);

        builder.Services.AddPlatform(builder.Configuration, assemblies);
        builder.Services.AddPlatformData<RegistryDbContext>(builder.Configuration);
        builder.Services.AddPlatformTenancy(builder.Configuration);

        builder.Services.Configure<RegistryOptions>(builder.Configuration.GetSection(RegistryOptions.SectionName));

        builder.Services.AddScoped<LocalRegistryTenantDirectory>();
        builder.Services.AddSingleton<ITenantDirectory>(sp =>
            new CachingTenantDirectory(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IOptions<TenancyOptions>>(),
                sp.GetRequiredService<ILogger<CachingTenantDirectory>>(),
                typeof(LocalRegistryTenantDirectory)));
        builder.Services.AddScoped<MembershipQuery>();
        builder.Services.AddScoped<RegistryService>();
        builder.Services.AddScoped<RegistryEndpoints>();

        // Validate registry configuration in non-dev environments
        if (!builder.Environment.IsDevelopment())
        {
            var registryOptions = builder.Configuration.GetSection(RegistryOptions.SectionName).Get<RegistryOptions>();
            if (registryOptions?.TrustedTenantId == "")
                throw new InvalidOperationException("RegistryOptions.TrustedTenantId must be configured in non-development environments");
        }

        builder.ConfigureFunctionsWebApplication().UsePlatform();

        await builder.Build().RunAsync();
        return 0;
    }
}
