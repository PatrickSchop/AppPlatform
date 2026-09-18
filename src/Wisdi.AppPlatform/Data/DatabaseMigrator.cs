using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace Wisdi.AppPlatform.Data;

/// <summary>
/// Manages database migrations with version tracking. Supports embedded core scripts (000-099)
/// and app scripts on disk (100+).
/// </summary>
public interface IDatabaseMigrator
{
    Task<MigrationResult> InitializeDatabaseAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAvailableScriptsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAppliedScriptsAsync(CancellationToken ct = default);
    Task<bool> CanConnectAsync(CancellationToken ct = default);
}

public class DatabaseMigrator<TContext> : IDatabaseMigrator
    where TContext : PlatformDbContext
{
    private readonly IDbContextFactory<TContext> _dbContextFactory;
    private readonly IEnumerable<IMigrationScriptProvider> _providers;
    private readonly ILogger<DatabaseMigrator<TContext>> _logger;

    public DatabaseMigrator(
        IDbContextFactory<TContext> dbContextFactory,
        IEnumerable<IMigrationScriptProvider> providers,
        ILogger<DatabaseMigrator<TContext>> logger)
    {
        _dbContextFactory = dbContextFactory;
        _providers = providers;
        _logger = logger;
    }

    public async Task<MigrationResult> InitializeDatabaseAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting database initialization...");

        try
        {
            using var context = await _dbContextFactory.CreateDbContextAsync(ct);

            if (!await context.Database.CanConnectAsync(ct))
            {
                _logger.LogError("Cannot connect to database.");
                return new MigrationResult
                {
                    Success = false,
                    Message = "Cannot connect to database. Database must exist before running migrations.",
                    CanConnect = false
                };
            }

            await ExecuteSchemaVersionsBootstrapAsync(context, ct);

            var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var appliedNames = await GetAppliedScriptNamesAsync(context, ct);
            foreach (var name in appliedNames)
            {
                applied.Add(name);
            }

            var allScripts = await GetOrderedScriptsAsync(ct);
            ValidateScriptNumbers(allScripts);

            var result = new MigrationResult { Success = true, CanConnect = true };

            foreach (var script in allScripts)
            {
                if (applied.Contains(script.Name))
                {
                    _logger.LogInformation($"Already applied: {script.Name}");
                    continue;
                }

                _logger.LogInformation($"Applying {script.Source} script: {script.Name}");
                var content = await script.ReadAsync(ct);

                var batches = SplitBatches(content);
                using var transaction = await context.Database.BeginTransactionAsync(ct);

                try
                {
                    foreach (var batch in batches)
                    {
                        await context.Database.ExecuteSqlRawAsync(batch, cancellationToken: ct);
                    }

                    var checksum = ComputeChecksum(content);
                    await context.Database.ExecuteSqlRawAsync(
                        "INSERT INTO [dbo].[__SchemaVersions] (ScriptName, Checksum) VALUES ({0}, {1})",
                        new object[] { script.Name, checksum },
                        cancellationToken: ct);

                    await transaction.CommitAsync(ct);
                    result.AppliedMigrations.Add(script.Name);
                    _logger.LogInformation($"Successfully applied: {script.Name}");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(ct);
                    _logger.LogError(ex, $"Error applying script {script.Name}");
                    result.Success = false;
                    result.Message = $"Error applying {script.Name}: {ex.Message}";
                    result.Error = ex.ToString();
                    return result;
                }
            }

            if (result.AppliedMigrations.Count > 0)
            {
                result.Message = $"Successfully applied {result.AppliedMigrations.Count} migration(s)";
            }
            else
            {
                result.Message = "Database is up to date";
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing database");
            return new MigrationResult
            {
                Success = false,
                Message = ex.Message,
                Error = ex.ToString(),
                CanConnect = false
            };
        }
    }

    public async Task<IReadOnlyList<string>> GetAvailableScriptsAsync(CancellationToken ct = default)
    {
        var scripts = await GetOrderedScriptsAsync(ct);
        return scripts.Select(s => s.Name).ToList();
    }

    public async Task<IReadOnlyList<string>> GetAppliedScriptsAsync(CancellationToken ct = default)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(ct);

        if (!await context.Database.CanConnectAsync(ct))
        {
            return new List<string>();
        }

        return await GetAppliedScriptNamesAsync(context, ct);
    }

    private async Task ExecuteSchemaVersionsBootstrapAsync(TContext context, CancellationToken ct)
    {
        var bootstrapSql = @"
IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[__SchemaVersions]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[__SchemaVersions] (
        [ScriptName]  NVARCHAR(255) NOT NULL PRIMARY KEY,
        [AppliedUtc]  DATETIME2     NOT NULL CONSTRAINT [DF___SchemaVersions_AppliedUtc] DEFAULT GETUTCDATE(),
        [Checksum]    NVARCHAR(64)  NULL
    );
END";

        await context.Database.ExecuteSqlRawAsync(bootstrapSql, cancellationToken: ct);
    }

    private async Task<List<string>> GetAppliedScriptNamesAsync(TContext context, CancellationToken ct)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ScriptName FROM [dbo].[__SchemaVersions]";
        using var reader = await command.ExecuteReaderAsync(ct);

        var scripts = new List<string>();
        while (await reader.NextResultAsync(ct))
        {
            if (reader.HasRows)
            {
                while (await reader.ReadAsync(ct))
                {
                    scripts.Add(reader.GetString(0));
                }
            }
        }

        return scripts;
    }

    private async Task<List<MigrationScript>> GetOrderedScriptsAsync(CancellationToken ct)
    {
        var coreScripts = new List<MigrationScript>();
        var appScripts = new List<MigrationScript>();

        foreach (var provider in _providers)
        {
            var scripts = provider.GetScripts();
            foreach (var script in scripts)
            {
                if (script.Source == "core")
                {
                    coreScripts.Add(script);
                }
                else
                {
                    appScripts.Add(script);
                }
            }
        }

        coreScripts.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        appScripts.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var allScripts = new List<MigrationScript>(coreScripts);
        allScripts.AddRange(appScripts);

        return allScripts;
    }

    private void ValidateScriptNumbers(List<MigrationScript> scripts)
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var script in scripts)
        {
            if (!seenNames.Add(script.Name))
            {
                throw new InvalidOperationException($"Duplicate script name: {script.Name}");
            }

            var leadingNumber = ExtractLeadingNumber(script.Name);

            if (script.Source == "core")
            {
                if (leadingNumber < 0 || leadingNumber > 99)
                {
                    throw new InvalidOperationException(
                        $"Core script '{script.Name}' has invalid number: must be 000-099");
                }
            }
            else if (script.Source == "app")
            {
                if (leadingNumber < 100)
                {
                    throw new InvalidOperationException(
                        $"App script '{script.Name}' has invalid number: must be 100+");
                }
            }
        }
    }

    private int ExtractLeadingNumber(string scriptName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(scriptName, @"^(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var num))
        {
            return num;
        }

        return -1;
    }

    private List<string> SplitBatches(string content)
    {
        var batches = new List<string>();
        var currentBatch = new StringBuilder();

        using var reader = new StringReader(content);
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            var trimmedLine = line.Trim();

            if (trimmedLine.Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                var batch = currentBatch.ToString().Trim();
                if (!string.IsNullOrEmpty(batch))
                {
                    batches.Add(batch);
                }

                currentBatch.Clear();
            }
            else
            {
                if (currentBatch.Length > 0)
                {
                    currentBatch.AppendLine();
                }

                currentBatch.Append(line);
            }
        }

        var finalBatch = currentBatch.ToString().Trim();
        if (!string.IsNullOrEmpty(finalBatch))
        {
            batches.Add(finalBatch);
        }

        return batches;
    }

    private static string ComputeChecksum(string content)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    public async Task<bool> CanConnectAsync(CancellationToken ct = default)
    {
        try
        {
            using var context = await _dbContextFactory.CreateDbContextAsync(ct);
            return await context.Database.CanConnectAsync(ct);
        }
        catch
        {
            return false;
        }
    }
}
