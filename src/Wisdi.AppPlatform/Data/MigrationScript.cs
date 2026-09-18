namespace Wisdi.AppPlatform.Data;

/// <summary>
/// Represents a migration script to be applied to the database.
/// </summary>
/// <param name="Name">The script file name (e.g. "000_CreateSchemaVersions.sql")</param>
/// <param name="Source">Either "core" or "app" - used for logging</param>
/// <param name="ReadAsync">Async function to read the script content</param>
public sealed record MigrationScript(
    string Name,
    string Source,
    Func<CancellationToken, Task<string>> ReadAsync);
