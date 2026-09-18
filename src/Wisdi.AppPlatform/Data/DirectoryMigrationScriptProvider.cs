using System.Reflection;

namespace PS.AppPlatform.Data;

/// <summary>
/// Provides migration scripts from a directory on disk.
/// Looks in {assemblyDirectory}\Database\Scripts or {currentDirectory}\Database\Scripts.
/// </summary>
public class DirectoryMigrationScriptProvider : IMigrationScriptProvider
{
    private readonly string? _scriptsDirectory;

    public DirectoryMigrationScriptProvider()
    {
        _scriptsDirectory = FindScriptsDirectory();
    }

    public IEnumerable<MigrationScript> GetScripts()
    {
        if (_scriptsDirectory == null || !Directory.Exists(_scriptsDirectory))
        {
            return Enumerable.Empty<MigrationScript>();
        }

        var scripts = Directory
            .GetFiles(_scriptsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(scriptName => new MigrationScript(
                scriptName,
                "app",
                ct => ReadFileAsync(Path.Combine(_scriptsDirectory, scriptName), ct)))
            .ToList();

        return scripts;
    }

    private string? FindScriptsDirectory()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrEmpty(assemblyLocation))
        {
            var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
            if (!string.IsNullOrEmpty(assemblyDirectory))
            {
                var scriptsPath = Path.Combine(assemblyDirectory, "Database", "Scripts");
                if (Directory.Exists(scriptsPath))
                {
                    return scriptsPath;
                }
            }
        }

        var currentDirectory = Directory.GetCurrentDirectory();
        var scriptsPathFromCurrent = Path.Combine(currentDirectory, "Database", "Scripts");
        if (Directory.Exists(scriptsPathFromCurrent))
        {
            return scriptsPathFromCurrent;
        }

        return null;
    }

    private static async Task<string> ReadFileAsync(string path, CancellationToken ct)
    {
        return await File.ReadAllTextAsync(path, ct);
    }
}

