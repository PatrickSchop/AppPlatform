using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PS.AppPlatform.Tenancy;
using Xunit;

namespace PS.AppPlatform.Tests;

public class CachingTenantDirectoryTests
{
    private sealed class FakeTenantDirectory : ITenantDirectory
    {
        public int CallCount { get; private set; }
        public Func<IdentityKey, CancellationToken, Task<UserMemberships?>>? Handler { get; set; }
        public bool ShouldThrow { get; set; }
        public SemaphoreSlim? BlockUntil { get; set; }

        public async Task<UserMemberships?> GetMembershipsAsync(IdentityKey identity, CancellationToken ct)
        {
            if (BlockUntil != null)
                await BlockUntil.WaitAsync(ct);

            CallCount++;

            if (ShouldThrow)
                throw new TenantDirectoryUnavailableException("Service unavailable");

            if (Handler != null)
                return await Handler(identity, ct);

            return null;
        }
    }

    private static TenancyOptions CreateOptions(
        TimeSpan? cacheDuration = null,
        TimeSpan? nullCacheDuration = null,
        TimeSpan? staleIfError = null)
    {
        return new TenancyOptions
        {
            CacheDuration = cacheDuration ?? TimeSpan.FromMinutes(5),
            NullCacheDuration = nullCacheDuration ?? TimeSpan.FromMinutes(1),
            StaleIfError = staleIfError ?? TimeSpan.FromHours(1)
        };
    }

    [Fact]
    public async Task GetMembershipsAsync_CacheHit_WithinWindow_CallsInnerOnce()
    {
        var result = new UserMemberships(Guid.NewGuid(), "Test", []);
        var inner = new FakeTenantDirectory { Handler = (_, _) => Task.FromResult<UserMemberships?>(result) };
        var logger = NullLogger<CachingTenantDirectory>.Instance;
        var cache = new CachingTenantDirectory(inner, Options.Create(CreateOptions()), logger);
        var identity = new IdentityKey("oid1", "tid1");

        var r1 = await cache.GetMembershipsAsync(identity, default);
        var r2 = await cache.GetMembershipsAsync(identity, default);

        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task GetMembershipsAsync_NullCached_ForShorterPeriod()
    {
        var inner = new FakeTenantDirectory { Handler = (_, _) => Task.FromResult<UserMemberships?>(null) };
        var logger = NullLogger<CachingTenantDirectory>.Instance;
        var options = CreateOptions(
            cacheDuration: TimeSpan.FromMinutes(5),
            nullCacheDuration: TimeSpan.FromSeconds(1));
        var cache = new CachingTenantDirectory(inner, Options.Create(options), logger);
        var identity = new IdentityKey("oid1", "tid1");

        var r1 = await cache.GetMembershipsAsync(identity, default);
        Assert.Null(r1);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task GetMembershipsAsync_ConcurrentMisses_CoalescedToOneInnerCall()
    {
        var inner = new FakeTenantDirectory
        {
            BlockUntil = new SemaphoreSlim(0),
            Handler = (_, _) => Task.FromResult<UserMemberships?>(new UserMemberships(Guid.NewGuid(), "Test", []))
        };
        var logger = NullLogger<CachingTenantDirectory>.Instance;
        var cache = new CachingTenantDirectory(inner, Options.Create(CreateOptions()), logger);
        var identity = new IdentityKey("oid1", "tid1");

        var t1 = cache.GetMembershipsAsync(identity, default);
        var t2 = cache.GetMembershipsAsync(identity, default);

        await Task.Delay(10);
        inner.BlockUntil!.Release();
        await Task.WhenAll(t1, t2);

        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task GetMembershipsAsync_FreshMissWithThrow_Rethrows()
    {
        var inner = new FakeTenantDirectory { ShouldThrow = true };
        var logger = NullLogger<CachingTenantDirectory>.Instance;
        var cache = new CachingTenantDirectory(inner, Options.Create(CreateOptions()), logger);
        var identity = new IdentityKey("oid1", "tid1");

        await Assert.ThrowsAsync<TenantDirectoryUnavailableException>(
            () => cache.GetMembershipsAsync(identity, default));
    }

    [Fact]
    public async Task GetMembershipsAsync_FailedLazyEvicted_NextCallRetries()
    {
        var inner = new FakeTenantDirectory { ShouldThrow = true };
        var logger = NullLogger<CachingTenantDirectory>.Instance;
        var cache = new CachingTenantDirectory(inner, Options.Create(CreateOptions()), logger);
        var identity = new IdentityKey("oid1", "tid1");

        await Assert.ThrowsAsync<TenantDirectoryUnavailableException>(
            () => cache.GetMembershipsAsync(identity, default));

        inner.ShouldThrow = false;
        inner.Handler = (_, _) => Task.FromResult<UserMemberships?>(new UserMemberships(Guid.NewGuid(), "Test", []));

        var r2 = await cache.GetMembershipsAsync(identity, default);
        Assert.NotNull(r2);
        Assert.Equal(2, inner.CallCount);
    }
}
