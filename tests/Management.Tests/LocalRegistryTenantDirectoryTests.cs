using Microsoft.EntityFrameworkCore;
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

    private static LocalRegistryTenantDirectory CreateDirectory(RegistryDbContext db)
    {
        var query = new MembershipQuery(db);
        var manifest = new Manifest();
        return new LocalRegistryTenantDirectory(query, manifest);
    }

    [Fact]
    public async Task GetMembershipsAsync_ReturnsNull_WhenUserNotInDb()
    {
        await using var db = CreateContext();
        var dir = CreateDirectory(db);

        var result = await dir.GetMembershipsAsync(
            new IdentityKey(Guid.NewGuid().ToString(), Guid.NewGuid().ToString()),
            default);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetMembershipsAsync_ReturnsUserMemberships_WhenUserExists()
    {
        await using var db = CreateContext();
        var svc = new RegistryService(db);

        // Register the management app
        var manifest = new Manifest();
        await svc.UpsertApplicationAsync(
            new ApplicationRegistration(manifest.Key, manifest.DisplayName, manifest.Tenancy, manifest.Roles, null),
            default);

        var identity = new IdentityKey(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
        await svc.EnsureAdminAsync(identity, "Test Admin", default);

        var dir = CreateDirectory(db);

        var result = await dir.GetMembershipsAsync(identity, default);
        Assert.NotNull(result);
        Assert.Single(result!.Tenants);
    }
}
