using PS.AppPlatform.Data;

namespace PS.Management.Registry;

/// <remarks>
/// This is a registry row for a tenant in any application, NOT a TenantEntity. The management
/// app manages every application's tenants and must never filter them by the current tenant scope.
/// </remarks>
public class Tenant : Entity
{
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDisabled { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
