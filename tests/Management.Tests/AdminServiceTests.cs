using Microsoft.EntityFrameworkCore;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using PS.Management.Registry;
using Xunit;

namespace PS.Management.Tests;

public class AdminServiceTests
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
    public async Task CreateTenant_CreatesDefaultTeam_SingleAppRefusesSecond()
    {
        await using var db = CreateContext();
        var svc = new RegistryService(db);

        // Register a Single app
        var singleResult = await svc.UpsertApplicationAsync(Reg("single", TenancyMode.Single, "admin"), default);
        var singleApp = await db.Applications.FirstAsync(a => a.Key == "single");

        // Try to create second tenant on single app - should throw 409 single_tenant_app
        var ex1 = await Assert.ThrowsAsync<RegistryConflictException>(
            () => svc.CreateTenantAsync(singleApp.Id, "Second", default));
        Assert.Equal("single_tenant_app", ex1.Code);

        // Register a Multi app
        var multiResult = await svc.UpsertApplicationAsync(Reg("multi", TenancyMode.Multi, "admin"), default);
        var multiApp = await db.Applications.FirstAsync(a => a.Key == "multi");

        // Create tenant on multi app - should succeed
        var tenant = await svc.CreateTenantAsync(multiApp.Id, "Custom", default);
        Assert.NotEqual(Guid.Empty, tenant.Id);

        // Verify default team was created
        var team = await db.Teams.FirstOrDefaultAsync(t => t.TenantId == tenant.Id && t.IsDefault);
        Assert.NotNull(team);
        Assert.True(team.IsDefault);
    }

    [Fact]
    public async Task CrossAppRoleAssignment_Returns400_DeprecatedRole_Returns400()
    {
        await using var db = CreateContext();
        var svc = new RegistryService(db);

        // Register two apps
        await svc.UpsertApplicationAsync(Reg("app-a", TenancyMode.Multi, "admin", "user"), default);
        await svc.UpsertApplicationAsync(Reg("app-b", TenancyMode.Multi, "admin", "viewer"), default);

        var appA = await db.Applications.FirstAsync(a => a.Key == "app-a");
        var appB = await db.Applications.FirstAsync(a => a.Key == "app-b");

        // Create a user
        var user = await svc.CreateUserAsync("Test User", "test@example.com", default);

        // Get teams
        var tenantA = await db.Tenants.FirstAsync(t => t.ApplicationId == appA.Id);
        var teamA = await db.Teams.FirstAsync(t => t.TenantId == tenantA.Id && t.IsDefault);

        var tenantB = await db.Tenants.FirstAsync(t => t.ApplicationId == appB.Id);
        var roleB = await db.Roles.FirstAsync(r => r.ApplicationId == appB.Id && r.Name == "viewer");

        // Add member to app-A
        await svc.AddMemberAsync(teamA.Id, user.Id, new List<Guid>(), default);

        // Try to add member with role from app-B - should throw 400
        var ex1 = await Assert.ThrowsAsync<RegistryValidationException>(
            () => svc.AddMemberAsync(teamA.Id, user.Id, [roleB.Id], default));
        Assert.Equal("roleIds", ex1.Field);

        // Deprecate a role in app-A and try to use it - should throw 400
        var userRole = await db.Roles.FirstAsync(r => r.ApplicationId == appA.Id && r.Name == "user");
        userRole.IsDeprecated = true;
        await db.SaveChangesAsync();

        var ex2 = await Assert.ThrowsAsync<RegistryValidationException>(
            () => svc.AddMemberAsync(teamA.Id, user.Id, [userRole.Id], default));
        Assert.Equal("roleIds", ex2.Field);
    }

    [Fact]
    public async Task LastAdminGuard_RemoveRole_RemoveMembership_DisableUser()
    {
        await using var db = CreateContext();
        var svc = new RegistryService(db);

        // Bootstrap management app with admin
        await svc.UpsertApplicationAsync(Reg("management", TenancyMode.Single, "admin"), default);
        var adminUser = await svc.EnsureAdminAsync(
            new IdentityKey("oid1", "tid1"),
            "Admin User",
            default);

        var management = await db.Applications.FirstAsync(a => a.Key == "management");
        var defaultTenant = await db.Tenants.FirstAsync(t => t.ApplicationId == management.Id && t.Name == "Default");
        var defaultTeam = await db.Teams.FirstAsync(t => t.TenantId == defaultTenant.Id && t.IsDefault);
        var adminRole = await db.Roles.FirstAsync(r => r.ApplicationId == management.Id && r.Name == "admin");
        var member = await db.TeamMembers.FirstAsync(tm => tm.TeamId == defaultTeam.Id && tm.UserId == adminUser);

        // Try removing their admin role - should throw 409 last_admin
        var ex1 = await Assert.ThrowsAsync<RegistryConflictException>(
            () => svc.SetMemberRolesAsync(member.Id, [], default));
        Assert.Equal("last_admin", ex1.Code);

        // Try removing membership - should throw 409 last_admin
        var ex2 = await Assert.ThrowsAsync<RegistryConflictException>(
            () => svc.RemoveMemberAsync(member.Id, default));
        Assert.Equal("last_admin", ex2.Code);

        // Try disabling user - should throw 409 last_admin
        var ex3 = await Assert.ThrowsAsync<RegistryConflictException>(
            () => svc.UpdateUserAsync(adminUser, null, null, true, default));
        Assert.Equal("last_admin", ex3.Code);
    }

    private static async Task<(RegistryService Svc, RegistryDbContext Db, Guid P, Guid Q, Guid PMember, Guid QMember)> TwoAdmins()
    {
        var db = CreateContext();
        var svc = new RegistryService(db);
        await svc.UpsertApplicationAsync(Reg("management", TenancyMode.Single, "admin"), default);
        var p = await svc.EnsureAdminAsync(new IdentityKey("oid-p", "tid"), "P", default);
        var q = await svc.EnsureAdminAsync(new IdentityKey("oid-q", "tid"), "Q", default);
        return (svc, db, p, q,
            (await db.TeamMembers.FirstAsync(m => m.UserId == p)).Id,
            (await db.TeamMembers.FirstAsync(m => m.UserId == q)).Id);
    }

    [Fact]
    public async Task LastAdminGuard_WithTwoAdmins_AllowsRemovingTheFirst_RefusesTheSecond()
    {
        // Gate E check 10: remove P's admin, then Q's.
        var (svc, db, _, _, pMember, qMember) = await TwoAdmins();
        await using var _ = db;

        await svc.SetMemberRolesAsync(pMember, [], default);

        var ex = await Assert.ThrowsAsync<RegistryConflictException>(
            () => svc.SetMemberRolesAsync(qMember, [], default));
        Assert.Equal("last_admin", ex.Code);
        Assert.Single(await db.RoleAssignments.Where(a => a.TeamMemberId == qMember).ToListAsync());
    }

    [Fact]
    public async Task LastAdminGuard_WithTwoAdmins_AllowsRemovingAMembershipAndDisablingAUser()
    {
        var (svc, db, _, q, pMember, _) = await TwoAdmins();
        await using var _ = db;

        await svc.UpdateUserAsync(q, null, null, true, default);          // disable Q: P remains
        await Assert.ThrowsAsync<RegistryConflictException>(
            () => svc.RemoveMemberAsync(pMember, default));                // P is now the only one
    }

    [Fact]
    public async Task LastAdminGuard_DoesNotCountAnInvitedAdmin_ButDoesNotBlockDemotingThem()
    {
        // An invited admin has not accepted yet and cannot sign in. They must not count as the
        // administrator who keeps the portal reachable, and demoting them is not "the last admin".
        await using var db = CreateContext();
        var svc = new RegistryService(db);
        await svc.UpsertApplicationAsync(Reg("management", TenancyMode.Single, "admin"), default);
        var p = await svc.EnsureAdminAsync(new IdentityKey("oid-p", "tid"), "P", default);
        var app = await db.Applications.FirstAsync(a => a.Key == "management");
        var adminRole = await db.Roles.FirstAsync(r => r.ApplicationId == app.Id && r.Name == "admin");
        var created = await svc.CreateInvitedUserAsync(app.Id, null, [adminRole.Id], "Invited", null, p,
            new RegistryOptions { PublicBaseUrl = "https://x.test" }, default);
        var invitedMember = await db.TeamMembers.FirstAsync(m => m.UserId == created.User.Id);
        var pMember = await db.TeamMembers.FirstAsync(m => m.UserId == p);

        // P is the only admin who can act: P cannot be demoted just because an invited admin exists.
        var ex = await Assert.ThrowsAsync<RegistryConflictException>(
            () => svc.SetMemberRolesAsync(pMember.Id, [], default));
        Assert.Equal("last_admin", ex.Code);

        // The invited admin can be demoted: P remains.
        await svc.SetMemberRolesAsync(invitedMember.Id, [], default);
        Assert.Empty(await db.RoleAssignments.Where(a => a.TeamMemberId == invitedMember.Id).ToListAsync());
    }

    [Fact]
    public async Task DefaultTeam_CannotBeRenamedOrDeleted_LastTenant_CannotBeDeleted()
    {
        await using var db = CreateContext();
        var svc = new RegistryService(db);

        // Register app
        await svc.UpsertApplicationAsync(Reg("myapp", TenancyMode.Multi, "admin"), default);
        var app = await db.Applications.FirstAsync(a => a.Key == "myapp");
        var tenant = await db.Tenants.FirstAsync(t => t.ApplicationId == app.Id && t.Name == "Default");
        var team = await db.Teams.FirstAsync(t => t.TenantId == tenant.Id && t.IsDefault);

        // Try to rename Default team - should throw 409 default_team
        var ex1 = await Assert.ThrowsAsync<RegistryConflictException>(
            () => svc.UpdateTeamAsync(team.Id, "NewName", default));
        Assert.Equal("default_team", ex1.Code);

        // Try to delete Default team - should throw 409 default_team
        var ex2 = await Assert.ThrowsAsync<RegistryConflictException>(
            () => svc.DeleteTeamAsync(team.Id, default));
        Assert.Equal("default_team", ex2.Code);

        // Try to delete the only tenant - should throw 409 last_tenant
        var ex3 = await Assert.ThrowsAsync<RegistryConflictException>(
            () => svc.DeleteTenantAsync(tenant.Id, default));
        Assert.Equal("last_tenant", ex3.Code);
    }
}
