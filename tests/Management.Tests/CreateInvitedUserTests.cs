using Microsoft.EntityFrameworkCore;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using PS.Management.Registry;
using Xunit;

namespace PS.Management.Tests;

public class CreateInvitedUserTests
{
    private static readonly RegistryOptions Options = new() { PublicBaseUrl = "https://example.test" };

    private static RegistryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<RegistryDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            new PlatformAssemblies().AddContaining<RegistryService>());

    private static async Task<(RegistryService Svc, Application App, User Actor)> Setup(RegistryDbContext db, TenancyMode tenancy)
    {
        var svc = new RegistryService(db);
        await svc.UpsertApplicationAsync(
            new ApplicationRegistration("app", "App", tenancy,
                [new AppRoleDefinition("admin", "Admin"), new AppRoleDefinition("viewer", "Viewer")], null),
            default);
        var app = await db.Applications.FirstAsync(a => a.Key == "app");
        var actor = await svc.CreateUserAsync("Actor", null, default);
        return (svc, app, actor);
    }

    [Fact]
    public async Task SingleTenantApp_PlacesTheUserInTheDefaultTeamWithRoles_AndIssuesTheFirstInvitation()
    {
        await using var db = CreateContext();
        var (svc, app, actor) = await Setup(db, TenancyMode.Single);
        var admin = await db.Roles.FirstAsync(r => r.ApplicationId == app.Id && r.Name == "admin");

        var created = await svc.CreateInvitedUserAsync(app.Id, null, [admin.Id], "Q", "q@example.test", actor.Id, Options, default);

        Assert.Null(created.User.BoundUtc);
        var member = await db.TeamMembers.SingleAsync(m => m.UserId == created.User.Id);
        Assert.True((await db.Teams.FirstAsync(t => t.Id == member.TeamId)).IsDefault);
        Assert.Equal([admin.Id], await db.RoleAssignments.Where(a => a.TeamMemberId == member.Id).Select(a => a.RoleId).ToListAsync());
        var invitation = await db.Invitations.SingleAsync(i => i.UserId == created.User.Id);
        Assert.Equal(created.InvitationId, invitation.Id);
        Assert.False(string.IsNullOrEmpty(created.Token));
        Assert.Null(invitation.RevokedUtc);
    }

    [Fact]
    public async Task NoRoles_IsAllowed_TheUserJoinsWithoutARole()
    {
        await using var db = CreateContext();
        var (svc, app, actor) = await Setup(db, TenancyMode.Single);

        var created = await svc.CreateInvitedUserAsync(app.Id, null, [], "Q", null, actor.Id, Options, default);

        var member = await db.TeamMembers.SingleAsync(m => m.UserId == created.User.Id);
        Assert.Empty(await db.RoleAssignments.Where(a => a.TeamMemberId == member.Id).ToListAsync());
    }

    [Fact]
    public async Task MultipleTenants_WithoutATeam_IsRejected_AndNothingIsCreated()
    {
        await using var db = CreateContext();
        var (svc, app, actor) = await Setup(db, TenancyMode.Multi);
        await svc.CreateTenantAsync(app.Id, "A", default);
        await svc.CreateTenantAsync(app.Id, "B", default);
        var usersBefore = await db.Users.CountAsync();

        var ex = await Assert.ThrowsAsync<RegistryValidationException>(
            () => svc.CreateInvitedUserAsync(app.Id, null, [], "Q", null, actor.Id, Options, default));

        Assert.Equal("team_required", ex.Message);
        Assert.Equal(usersBefore, await db.Users.CountAsync());
    }

    [Fact]
    public async Task TeamOfAnotherApplication_IsRejected_AndNothingIsCreated()
    {
        await using var db = CreateContext();
        var (svc, app, actor) = await Setup(db, TenancyMode.Single);
        await svc.UpsertApplicationAsync(
            new ApplicationRegistration("other", "Other", TenancyMode.Single, [new AppRoleDefinition("admin", "Admin")], null), default);
        var otherApp = await db.Applications.FirstAsync(a => a.Key == "other");
        var otherTenant = await db.Tenants.FirstAsync(t => t.ApplicationId == otherApp.Id);
        var otherTeam = await db.Teams.FirstAsync(t => t.TenantId == otherTenant.Id);
        var usersBefore = await db.Users.CountAsync();

        var ex = await Assert.ThrowsAsync<RegistryValidationException>(
            () => svc.CreateInvitedUserAsync(app.Id, otherTeam.Id, [], "Q", null, actor.Id, Options, default));

        Assert.Equal("team_not_in_application", ex.Message);
        Assert.Equal(usersBefore, await db.Users.CountAsync());
    }

    [Fact]
    public async Task DeprecatedOrForeignRole_IsRejected_AndNothingIsCreated()
    {
        await using var db = CreateContext();
        var (svc, app, actor) = await Setup(db, TenancyMode.Single);
        var viewer = await db.Roles.FirstAsync(r => r.ApplicationId == app.Id && r.Name == "viewer");
        viewer.IsDeprecated = true;
        await db.SaveChangesAsync();
        var usersBefore = await db.Users.CountAsync();

        var deprecated = await Assert.ThrowsAsync<RegistryValidationException>(
            () => svc.CreateInvitedUserAsync(app.Id, null, [viewer.Id], "Q", null, actor.Id, Options, default));
        var unknown = await Assert.ThrowsAsync<RegistryValidationException>(
            () => svc.CreateInvitedUserAsync(app.Id, null, [Guid.NewGuid()], "Q", null, actor.Id, Options, default));

        Assert.Equal("invalid_roles", deprecated.Message);
        Assert.Equal("invalid_roles", unknown.Message);
        Assert.Equal(usersBefore, await db.Users.CountAsync());
    }

    [Fact]
    public async Task FindUser_MatchesTheIdentity_NotTheUserId()
    {
        // The acting admin must be found by (oid, tid). The registry user id is a different guid.
        await using var db = CreateContext();
        var svc = new RegistryService(db);
        var user = await svc.CreateUserAsync("Admin", null, default);
        user.ObjectId = "00000000-0000-0000-3a07-fe3e675110b9";
        user.IssuerTenantId = "9188040d-6c67-4c5b-b112-36a304b66dad";
        user.BoundUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var found = await svc.FindUserAsync(new IdentityKey(user.ObjectId, user.IssuerTenantId!), default);
        var otherTenant = await svc.FindUserAsync(new IdentityKey(user.ObjectId, "someone-else"), default);

        Assert.Equal(user.Id, found?.Id);
        Assert.Null(otherTenant);
        Assert.NotEqual(user.Id.ToString(), user.ObjectId);
    }
}
