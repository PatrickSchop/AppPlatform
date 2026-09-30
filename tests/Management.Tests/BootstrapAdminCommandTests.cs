using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using PS.Management.Registry;
using Xunit;

namespace PS.Management.Tests;

public class BootstrapAdminCommandTests
{
    /// <summary>
    /// Builds a minimal service provider with an InMemory RegistryDbContext,
    /// mirroring the shape the command expects from the platform command pipeline.
    /// </summary>
    private static async Task<IServiceProvider> BuildServicesWithManagementAppAsync()
    {
        var db = new RegistryDbContext(
            new DbContextOptionsBuilder<RegistryDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            new PlatformAssemblies().AddContaining<RegistryService>());

        var svc = new RegistryService(db);
        var manifest = new Manifest();
        await svc.UpsertApplicationAsync(
            new ApplicationRegistration(manifest.Key, manifest.DisplayName, manifest.Tenancy, manifest.Roles, null),
            default);

        var sc = new ServiceCollection();
        sc.AddSingleton(db);
        sc.AddScoped<RegistryService>(sp => new RegistryService(sp.GetRequiredService<RegistryDbContext>()));
        return sc.BuildServiceProvider();
    }

    [Fact]
    public async Task RunAsync_ValidArgs_ExitsZero_AdminExists()
    {
        var services = await BuildServicesWithManagementAppAsync();
        var command = new BootstrapAdminCommand();

        var oid = Guid.NewGuid().ToString();
        var tid = Guid.NewGuid().ToString();
        var args = new PlatformCommandArgs(["--bootstrap-admin", "--oid", oid, "--tid", tid, "--name", "Test Admin"]);

        var exitCode = await command.RunAsync(args, services, default);

        Assert.Equal(0, exitCode);

        // Verify admin user was created
        var db = services.GetRequiredService<RegistryDbContext>();
        var user = await db.Users.SingleAsync();
        Assert.Equal(oid, user.ObjectId);
        Assert.Equal(tid, user.IssuerTenantId);
        Assert.NotNull(user.BoundUtc);
        Assert.False(user.IsDisabled);

        // Verify role assignment
        Assert.Single(await db.TeamMembers.ToListAsync());
        Assert.Single(await db.RoleAssignments.ToListAsync());
    }

    [Fact]
    public async Task RunAsync_CalledTwice_IsIdempotent_ExitsZero()
    {
        var services = await BuildServicesWithManagementAppAsync();
        var command = new BootstrapAdminCommand();

        var oid = Guid.NewGuid().ToString();
        var tid = Guid.NewGuid().ToString();
        var args = new PlatformCommandArgs(["--bootstrap-admin", "--oid", oid, "--tid", tid, "--name", "Test Admin"]);

        var exit1 = await command.RunAsync(args, services, default);
        var exit2 = await command.RunAsync(args, services, default);

        Assert.Equal(0, exit1);
        Assert.Equal(0, exit2);

        var db = services.GetRequiredService<RegistryDbContext>();
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.TeamMembers.ToListAsync());
        Assert.Single(await db.RoleAssignments.ToListAsync());
    }

    [Fact]
    public async Task RunAsync_DisabledUser_ReEnables_ExitsZero()
    {
        var services = await BuildServicesWithManagementAppAsync();
        var command = new BootstrapAdminCommand();

        var oid = Guid.NewGuid().ToString();
        var tid = Guid.NewGuid().ToString();
        var args = new PlatformCommandArgs(["--bootstrap-admin", "--oid", oid, "--tid", tid, "--name", "Test Admin"]);

        // First run: creates user + membership + assignment
        var exit1 = await command.RunAsync(args, services, default);
        Assert.Equal(0, exit1);

        // Disable the user and persist
        var db = services.GetRequiredService<RegistryDbContext>();
        var user = await db.Users.SingleAsync();
        user.IsDisabled = true;
        await db.SaveChangesAsync();

        // Second run: should re-enable
        var exit2 = await command.RunAsync(args, services, default);
        Assert.Equal(0, exit2);

        // Clear identity map so the next query reads from the store, not the tracker
        db.ChangeTracker.Clear();
        var stored = await db.Users.SingleAsync();
        Assert.False(stored.IsDisabled);

        // Still exactly 1 user, 1 membership, 1 assignment
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.TeamMembers.ToListAsync());
        Assert.Single(await db.RoleAssignments.ToListAsync());
    }

    [Fact]
    public async Task RunAsync_InvalidOid_ReturnsNonZero_NamesOidInMessage()
    {
        var services = await BuildServicesWithManagementAppAsync();
        var command = new BootstrapAdminCommand();

        var args = new PlatformCommandArgs(
            ["--bootstrap-admin", "--oid", "not-a-guid", "--tid", Guid.NewGuid().ToString(), "--name", "Admin"]);

        // Capture stderr
        var originalErr = Console.Error;
        using var sw = new System.IO.StringWriter();
        Console.SetError(sw);
        try
        {
            var exitCode = await command.RunAsync(args, services, default);
            Assert.NotEqual(0, exitCode);
            Assert.Contains("--oid", sw.ToString());
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    [Fact]
    public async Task RunAsync_InvalidTid_ReturnsNonZero()
    {
        var services = await BuildServicesWithManagementAppAsync();
        var command = new BootstrapAdminCommand();

        var args = new PlatformCommandArgs(
            ["--bootstrap-admin", "--oid", Guid.NewGuid().ToString(), "--tid", "not-a-guid", "--name", "Admin"]);

        var exitCode = await command.RunAsync(args, services, default);

        Assert.NotEqual(0, exitCode);
    }
}
