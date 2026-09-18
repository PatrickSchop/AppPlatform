using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wisdi.AppPlatform.Data;
using Wisdi.AppPlatform.Hosting;
using Wisdi.AppPlatform.Tasks;
using Xunit;

namespace Wisdi.AppPlatform.Tests;

public class DataTests
{
    private class TestEntity : Entity
    {
        public string? Name { get; set; }
    }

    private class TestDbContext : PlatformDbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options, PlatformAssemblies assemblies)
            : base(options, assemblies)
        {
        }

        public DbSet<TestEntity> TestEntities { get; set; } = null!;
    }

    [Fact]
    public void DerivedContextDiscoveryPicksUpTestAssemblyEntities()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var assemblies = new PlatformAssemblies().AddContaining<DataTests>();

        using var context = new TestDbContext(options, assemblies);

        var testEntityType = context.Model.FindEntityType(typeof(TestEntity));
        Assert.NotNull(testEntityType);
    }

    [Fact]
    public void DerivedContextIncludesPlatformEntities()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var assemblies = new PlatformAssemblies().AddContaining<DataTests>();

        using var context = new TestDbContext(options, assemblies);

        var backgroundTaskType = context.Model.FindEntityType(typeof(BackgroundTask));
        Assert.NotNull(backgroundTaskType);
    }

    [Fact]
    public void AddPlatformDataThrowsWhenDatabaseSectionMissing()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddPlatformData<TestDbContext>(configuration)
        );

        Assert.Contains("database", ex.Message);
    }
}
