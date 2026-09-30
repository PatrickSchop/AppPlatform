using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using PS.Management.Registry;
using Xunit;

namespace PS.Management.Tests;

public class SelfRegistrationStepTests
{
    private static (RegistryDbContext db, IServiceProvider services) BuildServices()
    {
        var db = new RegistryDbContext(
            new DbContextOptionsBuilder<RegistryDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            new PlatformAssemblies().AddContaining<RegistryService>());

        var sc = new ServiceCollection();
        sc.AddSingleton(db);
        sc.AddScoped<RegistryService>(sp => new RegistryService(sp.GetRequiredService<RegistryDbContext>()));
        var provider = sc.BuildServiceProvider();
        return (db, provider);
    }

    [Fact]
    public async Task RunAsync_RegistersManagementApp_WithDefaultTenantAndTeam()
    {
        var (db, services) = BuildServices();
        await using (db)
        {
            var manifest = new Manifest();
            var step = new SelfRegistrationStep(manifest);

            var exitCode = await step.RunAsync(services, default);

            Assert.Equal(0, exitCode);

            var app = await db.Applications.SingleAsync();
            Assert.Equal("management", app.Key);

            var tenant = await db.Tenants.SingleAsync();
            Assert.Equal("Default", tenant.Name);

            var team = await db.Teams.SingleAsync();
            Assert.Equal("Default", team.Name);
        }
    }

    [Fact]
    public async Task RunAsync_CalledTwice_IsIdempotent()
    {
        var (db, services) = BuildServices();
        await using (db)
        {
            var manifest = new Manifest();
            var step = new SelfRegistrationStep(manifest);

            var exit1 = await step.RunAsync(services, default);
            var exit2 = await step.RunAsync(services, default);

            Assert.Equal(0, exit1);
            Assert.Equal(0, exit2);

            // Still only one app registered
            Assert.Single(await db.Applications.ToListAsync());
        }
    }
}
