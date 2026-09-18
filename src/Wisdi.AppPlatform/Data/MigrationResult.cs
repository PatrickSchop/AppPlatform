namespace Wisdi.AppPlatform.Data;

public class MigrationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> AppliedMigrations { get; set; } = new();
    public bool CanConnect { get; set; }
    public string? Error { get; set; }
}
