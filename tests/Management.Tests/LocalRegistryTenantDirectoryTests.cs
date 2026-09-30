using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using PS.Management.Registry;
using Xunit;

namespace PS.Management.Tests;

public class LocalRegistryTenantDirectoryTests
{
    private static RegistryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<RegistryDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            new PlatformAssemblies().AddContaining<RegistryService>());

    private static LocalRegistryTenantDirectory CreateDirectory(
        RegistryDbContext db,
        IMemoryCache cache,
        string appKey = "management")
    {
        var query = new MembershipQuery(db);
        var manifest = new Manifest();
        var options = Options.Create(new TenancyOptions { CacheDuration = TimeSpan.FromMinutes(5) });
        return new LocalRegistryTenantDirectory(query, manifest, cache, options);
    }

    [Fact]
    public async Task GetMembershipsAsync_ReturnsNull_WhenUserNotInDb()
    {
        await using var db = CreateContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var dir = CreateDirectory(db, cache);

        var result = await dir.GetMembershipsAsync(
            new IdentityKey(Guid.NewGuid().ToString(), Guid.NewGuid().ToString()),
            default);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetMembershipsAsync_CachesResult_SecondCallDoesNotReloadFromDb()
    {
        await using var db = CreateContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new RegistryService(db);

        // Register the management app
        var manifest = new Manifest();
        await svc.UpsertApplicationAsync(
            new ApplicationRegistration(manifest.Key, manifest.DisplayName, manifest.Tenancy, manifest.Roles, null),
            default);

        var identity = new IdentityKey(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
        await svc.EnsureAdminAsync(identity, "Test Admin", default);

        var dir = CreateDirectory(db, cache);

        // First call — populates cache
        var r1 = await dir.GetMembershipsAsync(identity, default);
        Assert.NotNull(r1);

        // Disable the user in DB — the cached result should still be returned
        var user = await db.Users.SingleAsync();
        user.IsDisabled = true;
        await db.SaveChangesAsync();

        // Second call — should return cached (non-null) result
        var r2 = await dir.GetMembershipsAsync(identity, default);
        Assert.NotNull(r2);
        Assert.Equal(r1!.UserId, r2!.UserId);
    }
}
