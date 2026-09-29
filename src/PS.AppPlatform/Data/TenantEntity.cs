namespace PS.AppPlatform.Data;

/// <summary>
/// Base class for per-tenant data. Rows are filtered to the current tenant on every query
/// and stamped on insert. The table MUST have [TenantId] UNIQUEIDENTIFIER NOT NULL.
/// </summary>
public abstract class TenantEntity : Entity
{
    public Guid TenantId { get; set; }
}
