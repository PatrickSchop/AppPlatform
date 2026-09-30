using Microsoft.EntityFrameworkCore;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using PS.Management.Registry;
using Xunit;

namespace PS.Management.Tests;

public class MembershipQueryTests
{
    private static RegistryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<RegistryDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            new PlatformAssemblies().AddContaining<RegistryService>());

    /// <summary>
    /// Seeds: one app, one tenant, two teams, two roles (roleB deprecated),
    /// one user in both teams with roleA in team1 and roleB in team2.
    /// </summary>
    private static async Task<(RegistryDbContext db, Application app, Tenant tenant, User user)>
        SeedAsync()
    {
        var db = CreateContext();

        var app = new Application { Key = "testapp", DisplayName = "Test App", Tenancy = TenancyMode.Multi };
        db.Applications.Add(app);
        await db.SaveChangesAsync();

        var tenant = new Tenant { ApplicationId = app.Id, Name = "T1" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var team1 = new Team { TenantId = tenant.Id, Name = "Team1", IsDefault = true };
        var team2 = new Team { TenantId = tenant.Id, Name = "Team2" };
        db.Teams.AddRange(team1, team2);
        await db.SaveChangesAsync();

        var roleA = new Role { ApplicationId = app.Id, Name = "roleA", DisplayName = "Role A" };
        var roleB = new Role { ApplicationId = app.Id, Name = "roleB", DisplayName = "Role B", IsDeprecated = true };
        db.Roles.AddRange(roleA, roleB);
        await db.SaveChangesAsync();

        var user = new User
        {
            ObjectId = Guid.NewGuid().ToString(),
            IssuerTenantId = Guid.NewGuid().ToString(),
            DisplayName = "Test User",
            BoundUtc = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var m1 = new TeamMember { TeamId = team1.Id, UserId = user.Id };
        var m2 = new TeamMember { TeamId = team2.Id, UserId = user.Id };
        db.TeamMembers.AddRange(m1, m2);
        await db.SaveChangesAsync();

        db.RoleAssignments.AddRange(
            new RoleAssignment { TeamMemberId = m1.Id, RoleId = roleA.Id },
            new RoleAssignment { TeamMemberId = m2.Id, RoleId = roleB.Id });
        await db.SaveChangesAsync();

        return (db, app, tenant, user);
    }

    [Fact]
    public async Task GetAsync_ReturnsRoleUnionAcrossTeams_ExcludesDeprecated()
    {
        var (db, app, _, user) = await SeedAsync();
        await using (db)
        {
            var q = new MembershipQuery(db);
            var result = await q.GetAsync(app.Key, new IdentityKey(user.ObjectId!, user.IssuerTenantId!), default);

            Assert.NotNull(result);
            Assert.Single(result!.Tenants);
            Assert.Contains("roleA", result.Tenants[0].Roles);
            Assert.DoesNotContain("roleB", result.Tenants[0].Roles);
            // Two teams: teamIds count should be 2
            Assert.Equal(2, result.Tenants[0].TeamIds.Count);
        }
    }

    [Fact]
    public async Task GetAsync_DisabledUser_ReturnsNull()
    {
        var (db, app, _, user) = await SeedAsync();
        await using (db)
        {
            user.IsDisabled = true;
            await db.SaveChangesAsync();

            var result = await new MembershipQuery(db)
                .GetAsync(app.Key, new IdentityKey(user.ObjectId!, user.IssuerTenantId!), default);

            Assert.Null(result);
        }
    }

    [Fact]
    public async Task GetAsync_DisabledTenant_ReturnsNull()
    {
        var (db, app, tenant, user) = await SeedAsync();
        await using (db)
        {
            tenant.IsDisabled = true;
            await db.SaveChangesAsync();

            var result = await new MembershipQuery(db)
                .GetAsync(app.Key, new IdentityKey(user.ObjectId!, user.IssuerTenantId!), default);

            Assert.Null(result);
        }
    }

    [Fact]
    public async Task GetAsync_DisabledApplication_ReturnsNull()
    {
        var (db, app, _, user) = await SeedAsync();
        await using (db)
        {
            app.IsDisabled = true;
            await db.SaveChangesAsync();

            var result = await new MembershipQuery(db)
                .GetAsync(app.Key, new IdentityKey(user.ObjectId!, user.IssuerTenantId!), default);

            Assert.Null(result);
        }
    }

    [Fact]
    public async Task GetAsync_UnboundUser_ReturnsNull()
    {
        var (db, app, _, user) = await SeedAsync();
        await using (db)
        {
            user.BoundUtc = null;
            await db.SaveChangesAsync();

            var result = await new MembershipQuery(db)
                .GetAsync(app.Key, new IdentityKey(user.ObjectId!, user.IssuerTenantId!), default);

            Assert.Null(result);
        }
    }

    [Fact]
    public async Task GetAsync_UserInTeamWithNoRoles_ReturnsTenantWithEmptyRoles()
    {
        await using var db = CreateContext();

        var app = new Application { Key = "testapp", DisplayName = "Test App", Tenancy = TenancyMode.Single };
        db.Applications.Add(app);
        await db.SaveChangesAsync();

        var tenant = new Tenant { ApplicationId = app.Id, Name = "Default" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var team = new Team { TenantId = tenant.Id, Name = "Default", IsDefault = true };
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        var user = new User
        {
            ObjectId = Guid.NewGuid().ToString(),
            IssuerTenantId = Guid.NewGuid().ToString(),
            DisplayName = "No Roles User",
            BoundUtc = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = user.Id });
        await db.SaveChangesAsync();

        var result = await new MembershipQuery(db)
            .GetAsync(app.Key, new IdentityKey(user.ObjectId!, user.IssuerTenantId!), default);

        Assert.NotNull(result);
        Assert.Single(result!.Tenants);
        Assert.Empty(result.Tenants[0].Roles);
    }
}
