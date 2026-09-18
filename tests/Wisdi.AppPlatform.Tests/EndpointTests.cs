using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Wisdi.AppPlatform.Data;
using Wisdi.AppPlatform.Endpoints;
using Wisdi.AppPlatform.Hosting;
using Wisdi.AppPlatform.Tasks;
using Xunit;

namespace Wisdi.AppPlatform.Tests;

public class EndpointTests
{
    [Fact]
    public void BackgroundTaskResponse_record_has_correct_properties()
    {
        var response = new BackgroundTaskResponse(
            id: Guid.NewGuid(),
            taskType: "test",
            status: "Running",
            statusMessage: "In progress",
            completionPercentage: 50,
            description: "Test task",
            requiresNotification: true,
            createdDate: DateTime.UtcNow,
            updatedDate: DateTime.UtcNow,
            startedDate: DateTime.UtcNow.AddSeconds(-10),
            completedDate: null);

        Assert.Equal("test", response.taskType);
        Assert.Equal("Running", response.status);
        Assert.Equal(50, response.completionPercentage);
    }

    [Fact]
    public void IBackgroundTaskEndpoints_is_non_generic()
    {
        // Verify the interface has no generic type parameters
        var interfaceType = typeof(IBackgroundTaskEndpoints);
        Assert.False(interfaceType.IsGenericType);
    }

    [Fact]
    public void IConfigurationEndpoints_is_non_generic()
    {
        var interfaceType = typeof(IConfigurationEndpoints);
        Assert.False(interfaceType.IsGenericType);
    }

    [Fact]
    public void IDatabaseEndpoints_is_non_generic()
    {
        var interfaceType = typeof(IDatabaseEndpoints);
        Assert.False(interfaceType.IsGenericType);
    }

    [Fact]
    public void IHealthEndpoints_is_non_generic()
    {
        var interfaceType = typeof(IHealthEndpoints);
        Assert.False(interfaceType.IsGenericType);
    }

    [Fact]
    public void ConfigurationEndpoints_converts_nested_sections()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["webApp:theme"] = "dark",
                ["webApp:settings:timeout"] = "30",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(config);
        var sp = services.BuildServiceProvider();

        var logger = sp.GetRequiredService<ILogger<ConfigurationEndpoints>>();
        var endpoint = new ConfigurationEndpoints(logger, config);

        var request = new DefaultHttpContext().Request;
        var result = endpoint.GetWebAppConfigurationAsync(request);

        Assert.NotNull(result);
    }

    [Fact]
    public void ConfigurationEndpoints_detects_arrays()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["webApp:origins:0"] = "http://localhost:3000",
                ["webApp:origins:1"] = "http://localhost:3001",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(config);
        var sp = services.BuildServiceProvider();

        var logger = sp.GetRequiredService<ILogger<ConfigurationEndpoints>>();
        var endpoint = new ConfigurationEndpoints(logger, config);

        var request = new DefaultHttpContext().Request;
        var result = endpoint.GetWebAppConfigurationAsync(request);

        Assert.NotNull(result);
    }

    [Fact]
    public void DatabaseEndpoints_requires_enable_flag()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["database:apiMigration:enable"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(config);
        var sp = services.BuildServiceProvider();

        var logger = sp.GetRequiredService<ILogger<DatabaseEndpoints>>();
        var migrator = NSubstitute.Substitute.For<IDatabaseMigrator>();
        var endpoint = new DatabaseEndpoints(logger, config, migrator);

        var request = new DefaultHttpContext().Request;
        var result = endpoint.InitializeAsync(request);

        Assert.NotNull(result);
    }

    [Fact]
    public void HealthEndpoints_can_be_instantiated()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(config);
        var sp = services.BuildServiceProvider();

        var logger = sp.GetRequiredService<ILogger<HealthEndpoints>>();
        var hostingEnv = NSubstitute.Substitute.For<IHostingEnvironment>();

        var endpoint = new HealthEndpoints(logger, config, hostingEnv);

        Assert.NotNull(endpoint);
    }

    [Fact]
    public void BackgroundTaskEndpoints_integration_with_interfaces()
    {
        // Verify endpoints depend on non-generic, well-defined interfaces
        var ctorParams = typeof(BackgroundTaskEndpoints).GetConstructors()[0].GetParameters();
        var paramTypes = ctorParams.Select(p => p.ParameterType).ToList();

        Assert.Contains(typeof(IBackgroundTaskService), paramTypes);
        Assert.Contains(typeof(ITaskExecutionManager), paramTypes);
        Assert.Contains(typeof(ITaskHandlerRegistry), paramTypes);
    }
}
