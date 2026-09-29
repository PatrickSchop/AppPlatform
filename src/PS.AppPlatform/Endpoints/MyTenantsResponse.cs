namespace PS.AppPlatform.Endpoints;

/// <summary>
/// Response model for GET /api/me/tenants. Property names match the MT-13 client contract
/// (lowercase).
/// </summary>
public sealed record MyTenantsResponse(
    bool registered,
    string? displayName,
    IReadOnlyList<MyTenantResponse> tenants);

public sealed record MyTenantResponse(
    Guid tenantId,
    string name,
    IReadOnlyList<string> roles);
