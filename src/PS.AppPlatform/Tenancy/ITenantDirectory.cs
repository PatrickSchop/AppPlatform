namespace PS.AppPlatform.Tenancy;

public sealed record IdentityKey(string ObjectId, string IssuerTenantId);

public sealed record TenantMembership(
    Guid TenantId,
    string TenantName,
    IReadOnlyList<Guid> TeamIds,
    IReadOnlyList<string> Roles);

public sealed record UserMemberships(
    Guid UserId,
    string DisplayName,
    IReadOnlyList<TenantMembership> Tenants);

public interface ITenantDirectory
{
    /// <summary>Null when the identity is not a registered, enabled user of this application.</summary>
    Task<UserMemberships?> GetMembershipsAsync(IdentityKey identity, CancellationToken ct);
}
