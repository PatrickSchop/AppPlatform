using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PS.AppPlatform.Data;
using PS.AppPlatform.Tenancy;

namespace PS.AppPlatform.Hosting;

/// <summary>Dispatcher for CLI commands like --migrate, --register, --bootstrap-admin.</summary>
public static class PlatformCommandLine
{
    /// <summary>
    /// True when args select any registered command (--migrate, --register, ...).
    /// </summary>
    public static bool IsCommandRun(string[] args)
    {
        return args.Contains("--migrate") || args.Contains("-migrate") ||
               args.Contains("--register") || args.Contains("-register") ||
               args.Contains("--bootstrap-admin") || args.Contains("-bootstrap-admin");
    }

    /// <summary>
    /// Builds the command service graph (config layering + BuildCommandServices), finds the
    /// command whose Name matches, runs it, returns its exit code. Unknown command → exit 2.
    /// </summary>
    public static async Task<int> RunAsync<TContext>(
        string[] args,
        Action<PlatformAssemblies>? configureAssemblies = null,
        Action<IServiceCollection, IConfiguration>? configureServices = null)
        where TContext : PlatformDbContext
    {
        var settingsFile = ExtractSettingsFile(args);
        // The anchor must be the app, not this library: it locates the appsettings files
        // next to the entry assembly.
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddPlatformConfiguration(assembly, extraSettingsFile: settingsFile);

        var configuration = configBuilder.Build();

        var services = BuildCommandServices<TContext>(configuration, configureAssemblies);

        // Register built-in commands
        services.AddSingleton<IPlatformCommand, MigrateCommand>();
        services.Configure<ManagementDirectoryOptions>(configuration.GetSection(ManagementDirectoryOptions.SectionName));
        services.AddSingleton<IPlatformCommand, RegisterCommand>();

        // Allow caller to register additional commands
        configureServices?.Invoke(services, configuration);

        var serviceProvider = services.BuildServiceProvider();

        // Extract the command name from args
        var commandName = ExtractCommandName(args);
        if (commandName == null)
        {
            Console.Error.WriteLine("Error: no command specified");
            return 2;
        }

        // Find and run the command
        var commands = serviceProvider.GetServices<IPlatformCommand>();
        var command = commands.FirstOrDefault(c => c.Name == commandName);

        if (command == null)
        {
            Console.Error.WriteLine($"Error: unknown command --{commandName}");
            return 2;
        }

        var commandArgs = new PlatformCommandArgs(args);
        var cts = new CancellationTokenSource();
        try
        {
            return await command.RunAsync(commandArgs, serviceProvider, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Command cancelled.");
            return 1;
        }
    }

    /// <summary>
    /// The service graph a command run needs. Separated so a test can assert it resolves
    /// without running an actual command.
    /// </summary>
    internal static ServiceCollection BuildCommandServices<TContext>(
        IConfiguration configuration,
        Action<PlatformAssemblies>? configureAssemblies = null)
        where TContext : PlatformDbContext
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var assemblies = new PlatformAssemblies();
        configureAssemblies?.Invoke(assemblies);

        services.AddSingleton(assemblies);
        // AzureIdentityProvider takes IConfiguration, so it has to be resolvable here.
        // Without this, commands throw before touching the database whenever the app
        // authenticates to SQL with a managed identity -- which is every deployed app.
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostingEnvironment, HostingEnvironment>();
        services.AddSingleton<IAzureIdentityProvider, AzureIdentityProvider>();
        services.AddPlatformData<TContext>(configuration);
        return services;
    }

    private static string? ExtractSettingsFile(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if ((args[i] == "--settingsFile" || args[i] == "-settingsFile") &&
                i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string? ExtractCommandName(string[] args)
    {
        // Commands are flags like --migrate, --register, --bootstrap-admin
        foreach (var arg in args)
        {
            if (arg.StartsWith("--"))
            {
                var name = arg.Substring(2);
                if (IsCommandName(name))
                {
                    return name;
                }
            }
            else if (arg.StartsWith("-"))
            {
                var name = arg.Substring(1);
                if (IsCommandName(name))
                {
                    return name;
                }
            }
        }

        return null;
    }

    private static bool IsCommandName(string name)
    {
        return name == "migrate" || name == "register" || name == "bootstrap-admin";
    }
}
