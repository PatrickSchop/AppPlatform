using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace PS.AppPlatform.Hosting;

public static class PlatformConfiguration
{
    /// <summary>
    /// Replaces the default JSON sources with assembly-relative appsettings files.
    /// The Functions host sets a base path that is not the assembly directory, which is
    /// why the defaults are removed rather than added to.
    /// </summary>
    public static IConfigurationBuilder AddPlatformConfiguration(
        this IConfigurationBuilder builder,
        Assembly anchorAssembly,
        string? environmentName = null,
        string? extraSettingsFile = null)
    {
        var jsonSources = builder.Sources.OfType<JsonConfigurationSource>().ToList();
        foreach (var jsonSource in jsonSources)
        {
            builder.Sources.Remove(jsonSource);
        }

        var basePath = Path.GetDirectoryName(anchorAssembly.Location) ?? "";
        environmentName ??= ResolveEnvironmentName();

        builder.AddJsonFile(Path.Combine(basePath, "appsettings.json"), optional: true, reloadOnChange: true);
        builder.AddJsonFile(Path.Combine(basePath, $"appsettings.{environmentName}.json"), optional: true, reloadOnChange: true);

        if (extraSettingsFile is not null)
        {
            var extraPath = Path.IsPathRooted(extraSettingsFile)
                ? extraSettingsFile
                : Path.Combine(basePath, extraSettingsFile);
            builder.AddJsonFile(extraPath, optional: false, reloadOnChange: true);
        }

        builder.AddEnvironmentVariables();

        return builder;
    }

    public static string ResolveEnvironmentName() =>
        (Environment.GetEnvironmentVariable("DEV_ENVIRONMENT") ?? "production").ToLowerInvariant();
}

