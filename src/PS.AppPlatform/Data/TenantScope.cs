namespace PS.AppPlatform.Data;

public readonly record struct TenantScope(bool Enabled, Guid? TenantId)
{
    public static TenantScope Disabled => new(false, null);
    public static TenantScope For(Guid? tenantId) => new(true, tenantId);
}

public sealed class TenantContextMissingException : InvalidOperationException
{
    public TenantContextMissingException()
        : base(
            "No tenant is resolved for this context. Use " +
            "IScopedDbContextFactory.CreateForTenant(...) or IUnscopedDbContextFactory for code " +
            "that runs without a request tenant (timers, anonymous endpoints, reporting).")
    {
    }
}

public sealed class TenantMismatchException : InvalidOperationException
{
    public TenantMismatchException(string message)
        : base(message)
    {
    }
}
