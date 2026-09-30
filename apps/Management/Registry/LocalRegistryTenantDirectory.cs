using PS.AppPlatform.Tenancy;

namespace PS.Management.Registry;

/// <summary>
/// ITenantDirectory implementation for the management app. Looks up memberships in the
/// local RegistryDbContext. Caching is handled by CachingTenantDirectory decorator.
/// </summary>
public sealed class LocalRegistryTenantDirectory(MembershipQuery query, AppManifest manifest) : ITenantDirectory
{
    public async Task<UserMemberships?> GetMembershipsAsync(IdentityKey identity, CancellationToken ct)
    {
        return await query.GetAsync(manifest.Key, identity, ct);
    }
}
