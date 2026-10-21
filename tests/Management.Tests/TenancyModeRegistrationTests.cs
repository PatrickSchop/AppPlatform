using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PS.AppPlatform.Data;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using PS.Management.Registry;
using Xunit;

namespace PS.Management.Tests;

public class TenancyModeRegistrationTests
{
    [Fact]
    public void ManagementApp_RunsInSingleMode_WhenDataIsRegisteredBeforeTenancy()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["database:connectionString"] = "Server=localhost;Database=x",
                ["tenancy:mode"] = "Single",
                ["tenancy:directory"] = "local",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(new PlatformAssemblies().Add(typeof(Manifest).Assembly));

        // Same order as Program.cs
        services.AddPlatformData<RegistryDbContext>(configuration);
        services.AddPlatformTenancy(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.Equal(TenancyMode.Single, provider.GetRequiredService<TenancyMode>());
    }
}
