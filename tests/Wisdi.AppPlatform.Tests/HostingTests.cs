using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wisdi.AppPlatform.Data;
using Wisdi.AppPlatform.Hosting;
using Xunit;

namespace Wisdi.AppPlatform.Tests;

public class HostingTests
{
    [Theory]
    [InlineData("development", EnvironmentType.Development)]
    [InlineData("Development", EnvironmentType.Development)]
    [InlineData("production", EnvironmentType.Production)]
    [InlineData("nonsense", EnvironmentType.Production)]
    [InlineData(null, EnvironmentType.Production)]
    public void HostingEnvironment_MapsEnvironmentCorrectly(string? envValue, EnvironmentType expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(envValue is null ? new Dictionary<string, string?>() : new[] { new KeyValuePair<string, string?>("DEV_ENVIRONMENT", envValue) })
            .Build();

        var environment = new HostingEnvironment(config);

        Assert.Equal(expected, environment.EnvironmentType);
    }

    [Fact]
    public void AzureIdentityProvider_ThrowsOnUnknownType()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] { new KeyValuePair<string, string?>("azureIdentity:type", "unknown") })
            .AddInMemoryCollection(new[] { new KeyValuePair<string, string?>("DEV_ENVIRONMENT", "production") })
            .Build();

        var hostingEnvironment = new HostingEnvironment(config);
        var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<AzureIdentityProvider>>();

        Assert.Throws<InvalidOperationException>(() => new AzureIdentityProvider(config, hostingEnvironment, logger));
    }

    [Fact]
    public void AzureIdentityProvider_ThrowsOnEmptyTypeInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] { new KeyValuePair<string, string?>("DEV_ENVIRONMENT", "production") })
            .Build();

        var hostingEnvironment = new HostingEnvironment(config);
        var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<AzureIdentityProvider>>();

        Assert.Throws<InvalidOperationException>(() => new AzureIdentityProvider(config, hostingEnvironment, logger));
    }

    [Fact]
    public void PlatformAssemblies_ContainsPlatformAssembly()
    {
        var assemblies = new PlatformAssemblies();

        Assert.Contains(typeof(PlatformAssemblies).Assembly, assemblies.All);
    }

    [Fact]
    public void PlatformAssemblies_Deduplicates()
    {
        var assemblies = new PlatformAssemblies();
        var platformAssembly = typeof(PlatformAssemblies).Assembly;

        assemblies.Add(platformAssembly);

        Assert.Single(assemblies.All, a => a == platformAssembly);
    }

    [Fact]
    public void AddPlatform_DiscoverServiceBuilder()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        var assemblies = new PlatformAssemblies().AddContaining<TestServiceBuilder>();

        services.AddPlatform(config, assemblies);

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IHostingEnvironment>());
    }

    public sealed class TestServiceBuilder : ServiceBuilder
    {
        public override void BuildServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<TestService>();
        }
    }

    public class TestService { }
}
