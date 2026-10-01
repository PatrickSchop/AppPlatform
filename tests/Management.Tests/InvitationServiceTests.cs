using Microsoft.EntityFrameworkCore;
using PS.AppPlatform.Auth;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using PS.Management.Registry;
using Xunit;

namespace PS.Management.Tests;

public class InvitationServiceTests
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
    public async Task Test5_InvitationLifecycle()
    {
        await using var db = CreateContext();
        var svc = new RegistryService(db);
        var options = new RegistryOptions { InvitationLifetimeMinutes = 10080, PublicBaseUrl = "https://example.com" };

        // Create an app
        await svc.UpsertApplicationAsync(Reg("testapp", TenancyMode.Single, "admin"), default);

        // Create users
        var user = await svc.CreateUserAsync("Test User", null, default);
        var actorUser = await svc.CreateUserAsync("Admin User", null, default);

        // Test: Create a user (unbound). Call CreateInvitationAsync
        Assert.Null(user.BoundUtc);
        var (invId1, token1, expires1) = await svc.CreateInvitationAsync(user.Id, actorUser.Id, options, default);
        Assert.NotEqual(Guid.Empty, invId1);
        Assert.NotEmpty(token1);

        var inv1 = await db.Invitations.FirstAsync(i => i.Id == invId1);
        var expectedHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token1));
        var expectedHashHex = Convert.ToHexString(expectedHash).ToLower();
        Assert.Equal(expectedHashHex, inv1.TokenHash);

        // Test: Create a second invitation -> first invitation is revoked
        var (invId2, token2, expires2) = await svc.CreateInvitationAsync(user.Id, actorUser.Id, options, default);
        inv1 = await db.Invitations.FirstAsync(i => i.Id == invId1);
        Assert.NotNull(inv1.RevokedUtc);

        // Test: AcceptInvitationAsync with valid token -> user gets BoundUtc, invitation gets AcceptedUtc
        var identity = new IdentityKey("test-oid-123", "test-tid-456");
        await svc.AcceptInvitationAsync(token2, identity, default);

        user = await db.Users.FirstAsync(u => u.Id == user.Id);
        Assert.NotNull(user.BoundUtc);
        Assert.Equal("test-oid-123", user.ObjectId);
        Assert.Equal("test-tid-456", user.IssuerTenantId);

        var inv2 = await db.Invitations.FirstAsync(i => i.Id == invId2);
        Assert.NotNull(inv2.AcceptedUtc);

        // Test: AcceptInvitationAsync again -> 410 invitation_used
        var ex = await Assert.ThrowsAsync<RegistryConflictException>(
            () => svc.AcceptInvitationAsync(token2, identity, default));
        Assert.Equal("invitation_used", ex.Code);

        // Test: Create user, create invitation, manually set ExpiresUtc to past -> preview returns invitation_expired
        var user3 = await svc.CreateUserAsync("User 3", null, default);

        var (invId3, token3, _) = await svc.CreateInvitationAsync(user3.Id, actorUser.Id, options, default);
        var inv3 = await db.Invitations.FirstAsync(i => i.Id == invId3);
        inv3.ExpiresUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        var ex2 = await Assert.ThrowsAsync<RegistryConflictException>(
            () => svc.PreviewInvitationAsync(token3, default));
        Assert.Equal("invitation_expired", ex2.Code);

        // Test: Create user A, create invitation for user A, accept with (oid1, tid1) -> 409 identity_already_registered
        var userA = await svc.CreateUserAsync("User A", null, default);
        var userB = await svc.CreateUserAsync("User B", null, default);
        userB.ObjectId = "oid1";
        userB.IssuerTenantId = "tid1";
        userB.BoundUtc = DateTime.UtcNow;
        db.Users.Update(userB);
        await db.SaveChangesAsync();

        var (invIdA, tokenA, _) = await svc.CreateInvitationAsync(userA.Id, actorUser.Id, options, default);
        var identityA = new IdentityKey("oid1", "tid1");
        var ex3 = await Assert.ThrowsAsync<RegistryConflictException>(
            () => svc.AcceptInvitationAsync(tokenA, identityA, default));
        Assert.Equal("identity_already_registered", ex3.Code);
    }

    [Fact]
    public async Task Test7_PipelineUnregisteredUserCanPreviewInvitation()
    {
        await using var db = CreateContext();
        var svc = new RegistryService(db);
        var options = new RegistryOptions { InvitationLifetimeMinutes = 10080, PublicBaseUrl = "https://example.com" };

        // Create an app
        await svc.UpsertApplicationAsync(Reg("testapp", TenancyMode.Single, "admin"), default);

        // Create users
        var invitedUser = await svc.CreateUserAsync("Invited User", null, default);
        var adminUser = await svc.CreateUserAsync("Admin User", null, default);

        // Create invitation
        var (invId, token, expires) = await svc.CreateInvitationAsync(invitedUser.Id, adminUser.Id, options, default);

        // Test: An unregistered authenticated user can preview invitation (should succeed, not throw)
        var (displayName, applications, expiresUtc) = await svc.PreviewInvitationAsync(token, default);
        Assert.Equal("Invited User", displayName);
        Assert.NotNull(applications);
        Assert.IsType<List<string>>(applications);
    }
}
