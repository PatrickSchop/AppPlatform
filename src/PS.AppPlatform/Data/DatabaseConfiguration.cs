namespace PS.AppPlatform.Data;

public class DatabaseConfiguration
{
    public string? ConnectionString { get; set; }
    public bool UseManagedIdentity { get; set; }

    public ApiMigrationConfiguration? ApiMigration { get; set; }
}

public class ApiMigrationConfiguration
{
    public bool Enable { get; set; } = false;
}

