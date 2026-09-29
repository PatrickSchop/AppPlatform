using PS.AppPlatform.Data;

namespace MultiTenantSample.Data;

/// <summary>
/// App-owned lookup from a public slug to a tenant. Application-wide (Entity, not
/// TenantEntity) so an anonymous caller can resolve it before any tenant is known; TenantId
/// is a plain field here, not a navigation, so the tenant query filter never hides it.
/// </summary>
public class TenantAlias : Entity
{
    public string Slug { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
}
