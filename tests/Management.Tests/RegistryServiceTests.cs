using Microsoft.EntityFrameworkCore;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using PS.Management.Registry;
using Xunit;

namespace PS.Management.Tests;

public class RegistryServiceTests
{
    private static RegistryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<RegistryDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            new PlatformAssemblies().AddContaining<RegistryService>());

    private static ApplicationRegistration Reg(
        string key,
        TenancyMode tenancy,
        params string[] roleNames) =>
        new(key, $"{key} app", tenancy,
            roleNames.Select(n => new AppRoleDefinition(n, n)).ToList(),
            null);

    [Fact]
    public async Task FirstUpsert_CreatesApplication_DefaultTenant_DefaultTeam()
    {
        await using var db = CreateContext();
        var svc = new RegistryService(db);

        var result = await svc.UpsertApplicationAsync(Reg("myapp", TenancyMode.Single, "admin"), default);

        Assert.True(result.Created);
        Assert.Equal(["admin"], result.RolesAdded);
        Assert.Empty(result.RolesDeprecated);
        Assert.Empty(result.RolesReactivated);

        var app = await db.Applications.SingleAsync();
        Assert.Equal("myapp", app.Key);

        var tenant = await db.Tenants.SingleAsync();
        Assert.Equal("Default", tenant.Name);
        Assert.Equal(app.Id, tenant.ApplicationId);

        var team = await db.Teams.SingleAsync();
        Assert.Equal("Default", team.Name);
        Assert.True(team.IsDefault);
        Assert.Equal(tenant.Id, team.TenantId);
    }

    [Fact]
    public async Task SecondIdenticalUpsert_ReportsNoChanges()
    {
        await using var db = CreateContext();
        var svc = new RegistryService(db);
        var reg = Reg("myapp", TenancyMode.Single, "admin");

        await svc.UpsertApplicationAsync(reg, default);
        var r2 = await svc.UpsertApplicationAsync(reg, default);

        Assert.False(r2.Created);
        Assert.Empty(r2.RolesAdded);
        Assert.Empty(r2.RolesDeprecated);
        Assert.Empty(r2.RolesReactivated);
    }

    [Fact]
    public async Task RemovingRole_DeprecatesIt_DoesNotDelete()
    {
        await using var db = CreateContext();
        var svc = new RegistryService(db);

        await svc.UpsertApplicationAsync(Reg("myapp", TenancyMode.Single, "admin", "editor"), default);

        var r2 = await svc.UpsertApplicationAsync(Reg("myapp", TenancyMode.Single, "admin"), default);

        Assert.Contains("editor", r2.RolesDeprecated);
        Assert.Empty(r2.RolesAdded);

        var editorRole = await db.Roles.SingleAsync(r => r.Name == "editor");
        Assert.True(editorRole.IsDeprecated);
        // Still exists (not deleted)
        Assert.Equal(2, await db.Roles.CountAsync());
    }

    [Fact]
    public async Task ReAddingDeprecatedRole_ReactivatesIt()
    {
        await using var db = CreateContext();
        var svc = new RegistryService(db);

        await svc.UpsertApplicationAsync(Reg("myapp", TenancyMode.Single, "admin", "editor"), default);
        await svc.UpsertApplicationAsync(Reg("myapp", TenancyMode.Single, "admin"), default);

        var r3 = await svc.UpsertApplicationAsync(Reg("myapp", TenancyMode.Single, "admin", "editor"), default);

        Assert.Contains("editor", r3.RolesReactivated);
        Assert.Empty(r3.RolesDeprecated);
        var editorRole = await db.Roles.SingleAsync(r => r.Name == "editor");
        Assert.False(editorRole.IsDeprecated);
    }

    [Fact]
    public async Task MultiToSingle_WithTwoEnabledTenants_IsRejected()
    {
        await using var db = CreateContext();
        var svc = new RegistryService(db);

        var r1 = await svc.UpsertApplicationAsync(Reg("myapp", TenancyMode.Multi, "admin"), default);

        // Manually add a second enabled tenant
        db.Tenants.Add(new Tenant { ApplicationId = r1.ApplicationId, Name = "Tenant2" });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.UpsertApplicationAsync(Reg("myapp", TenancyMode.Single, "admin"), default));
    }

    [Fact]
    public async Task EnsureAdminAsync_Idempotent_OneUserOneMembershipOneAssignment()
    {
        await using var db = CreateContext();
        var svc = new RegistryService(db);

        // Set up the management app first (required by EnsureAdminAsync)
        var manifest = new Manifest();
        await svc.UpsertApplicationAsync(
            new ApplicationRegistration(manifest.Key, manifest.DisplayName, manifest.Tenancy, manifest.Roles, null),
            default);

        var identity = new IdentityKey(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

        var id1 = await svc.EnsureAdminAsync(identity, "Admin User", default);
        var id2 = await svc.EnsureAdminAsync(identity, "Admin User", default);

        Assert.Equal(id1, id2);
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.TeamMembers.ToListAsync());
        Assert.Single(await db.RoleAssignments.ToListAsync());
    }
}
