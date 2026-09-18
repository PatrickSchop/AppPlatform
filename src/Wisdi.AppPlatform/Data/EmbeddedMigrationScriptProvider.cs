using System.Reflection;

namespace Wisdi.AppPlatform.Data;

/// <summary>
/// Provides migration scripts embedded as resources in the platform assembly.
/// </summary>
public class EmbeddedMigrationScriptProvider : IMigrationScriptProvider
{
    private readonly Assembly _assembly;

    public EmbeddedMigrationScriptProvider()
    {
        _assembly = typeof(EmbeddedMigrationScriptProvider).Assembly;
    }

    public IEnumerable<MigrationScript> GetScripts()
    {
        var prefix = "Wisdi.AppPlatform.Data.Scripts.";
        var resourceNames = _assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix) && name.EndsWith(".sql"))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

        foreach (var resourceName in resourceNames)
        {
            var scriptName = resourceName.Substring(prefix.Length);
            yield return new MigrationScript(
                scriptName,
                "core",
                async ct => await ReadResourceAsync(resourceName, ct));
        }
    }

    private async Task<string> ReadResourceAsync(string resourceName, CancellationToken ct)
    {
        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }
}
