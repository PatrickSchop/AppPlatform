using PS.AppPlatform.Data;

namespace TinyApp.Data;

/// <summary>
/// An example of tenant-owned data. TenantId is set by the platform when the entity is saved and
/// every query is filtered to the current tenant, so code never mentions the tenant.
/// </summary>
public class Item : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
