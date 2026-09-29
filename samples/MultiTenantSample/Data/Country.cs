using PS.AppPlatform.Data;

namespace MultiTenantSample.Data;

/// <summary>Application-wide, not per-tenant: every tenant sees the same list.</summary>
public class Country : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
