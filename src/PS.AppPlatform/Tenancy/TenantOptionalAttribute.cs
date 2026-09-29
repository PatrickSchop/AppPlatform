namespace PS.AppPlatform.Tenancy;

/// <summary>
/// The endpoint needs an authenticated user but not a selected tenant, e.g. GET /api/me/tenants.
/// It runs for unregistered users too, with ITenantContext unresolved.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class TenantOptionalAttribute : Attribute { }
