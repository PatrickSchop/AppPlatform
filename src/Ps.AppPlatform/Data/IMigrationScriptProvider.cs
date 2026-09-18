namespace PS.AppPlatform.Data;

/// <summary>
/// Provides migration scripts for database initialization.
/// </summary>
public interface IMigrationScriptProvider
{
    /// <summary>
    /// Gets all available migration scripts.
    /// </summary>
    IEnumerable<MigrationScript> GetScripts();
}

