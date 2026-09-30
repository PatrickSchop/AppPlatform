using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PS.AppPlatform.Tenancy;

namespace PS.Management.Registry;

/// <summary>
/// ITenantDirectory implementation for the management app. Looks up memberships in the
/// local RegistryDbContext, wrapped in IMemoryCache with the platform cache duration.
/// </summary>
public sealed class LocalRegistryTenantDirectory(
    MembershipQuery query,
    AppManifest manifest,
    IMemoryCache cache,
    IOptions<TenancyOptions> options) : ITenantDirectory
{
    public async Task<UserMemberships?> GetMembershipsAsync(IdentityKey identity, CancellationToken ct)
    {
        var cacheKey = $"LocalRegistryTenantDirectory:{manifest.Key}:{identity.ObjectId}:{identity.IssuerTenantId}";

        if (cache.TryGetValue(cacheKey, out UserMemberships? cached))
            return cached;

        var result = await query.GetAsync(manifest.Key, identity, ct);

        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(options.Value.CacheDuration);

        cache.Set(cacheKey, result, cacheEntryOptions);

        return result;
    }
}
