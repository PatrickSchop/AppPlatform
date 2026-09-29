using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PS.AppPlatform.Data;
using PS.AppPlatform.Hosting;
using Xunit;

namespace PS.AppPlatform.Tests;

public class CommandLineTests
{
    [Theory]
    [InlineData(new[] { "--migrate" }, true)]
    [InlineData(new[] { "-migrate" }, true)]
    [InlineData(new[] { "--other" }, false)]
    [InlineData(new string[] { }, false)]
    [InlineData(new[] { "some", "arg" }, false)]
    public void IsCommandRun_CorrectlyIdentifiesCommandArgs(string[] args, bool expected)
    {
        Assert.Equal(expected, PlatformCommandLine.IsCommandRun(args));
    }

    [Theory]
    [InlineData("--oid", "value1")]
    [InlineData("-oid", "value2")]
    public void PlatformCommandArgs_Get_ReadsValues(string flagName, string value)
    {
        var args = new PlatformCommandArgs(new[] { flagName, value, "--other", "data" });
        Assert.Equal(value, args.Get("oid"));
    }

    [Fact]
    public void PlatformCommandArgs_Get_ReturnsNullWhenMissing()
    {
        var args = new PlatformCommandArgs(new[] { "--other", "value" });
        Assert.Null(args.Get("oid"));
    }

    [Fact]
    public void PlatformCommandArgs_Require_ThrowsWhenMissing()
    {
        var args = new PlatformCommandArgs(new[] { "--other", "value" });
        var ex = Assert.Throws<ArgumentException>(() => args.Require("oid"));
        Assert.Contains("--oid", ex.Message);
    }

    [Fact]
    public void UnknownCommand_SelectsNoCommand()
    {
        // When a command selection logic finds no matching command, RunAsync returns exit code 2.
        // We can't easily test RunAsync end-to-end without file system configuration, so we test
        // the command resolution and invocation separately via BuildCommandServices.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["database:connectionString"] = "Server=(localdb)\\mssqllocaldb;Database=test;Trusted_Connection=true;",
            })
            .Build();

        var services = PlatformCommandLine.BuildCommandServices<TestCommandContext>(configuration);
        services.AddSingleton<IPlatformCommand, MigrateCommand>();
        using var provider = services.BuildServiceProvider();

        var commands = provider.GetServices<IPlatformCommand>();
        var fakeCommand = commands.FirstOrDefault(c => c.Name == "unknown");

        Assert.Null(fakeCommand);
    }

    [Fact]
    public async Task FakeCommand_IsInvokedWhenRegistered()
    {
        // Test that a registered IPlatformCommand is found and invoked.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["database:connectionString"] = "Server=(localdb)\\mssqllocaldb;Database=test;Trusted_Connection=true;",
            })
            .Build();

        var fakeCommandRun = false;
        var fakeCommand = new FakeCommand(() => { fakeCommandRun = true; });

        var services = PlatformCommandLine.BuildCommandServices<TestCommandContext>(configuration);
        services.AddSingleton<IPlatformCommand>(fakeCommand);
        using var provider = services.BuildServiceProvider();

        var commands = provider.GetServices<IPlatformCommand>();
        var found = commands.FirstOrDefault(c => c.Name == "fake");

        Assert.NotNull(found);
        Assert.Equal(fakeCommand, found);

        var args = new PlatformCommandArgs(new[] { "--fake" });
        var exitCode = await found!.RunAsync(args, provider, CancellationToken.None);

        Assert.True(fakeCommandRun);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void BuildCommandServices_ResolvesIdentityProvider()
    {
        // A deployed app authenticates to SQL with a managed identity, so a command run
        // must construct AzureIdentityProvider, which takes IConfiguration.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["database:connectionString"] = "Server=tcp:example.database.windows.net,1433;Database=x;Encrypt=True;",
                ["database:useManagedIdentity"] = "true",
                ["azureIdentity:type"] = "azureCli",
            })
            .Build();

        var services = PlatformCommandLine.BuildCommandServices<TestCommandContext>(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IAzureIdentityProvider>());
        Assert.NotNull(provider.GetRequiredService<IConfiguration>());
    }

    private class TestCommandContext : PlatformDbContext
    {
        public TestCommandContext(DbContextOptions<TestCommandContext> options, PlatformAssemblies assemblies)
            : base(options, assemblies) { }
    }

    private class FakeCommand : IPlatformCommand
    {
        private readonly Action _onRun;

        public FakeCommand(Action onRun)
        {
            _onRun = onRun;
        }

        public string Name => "fake";

        public Task<int> RunAsync(PlatformCommandArgs args, IServiceProvider services, CancellationToken ct)
        {
            _onRun();
            return Task.FromResult(0);
        }
    }
}
