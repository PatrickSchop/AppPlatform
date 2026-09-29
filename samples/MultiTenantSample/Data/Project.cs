using PS.AppPlatform.Data;

namespace MultiTenantSample.Data;

public class Project : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
