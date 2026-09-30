using Microsoft.EntityFrameworkCore;
using PS.AppPlatform.Data;
using PS.AppPlatform.Hosting;

namespace PS.Management.Registry;

public class RegistryDbContext(DbContextOptions<RegistryDbContext> options, PlatformAssemblies assemblies)
    : PlatformDbContext(options, assemblies)
{
    public DbSet<Application> Applications { get; set; } = null!;
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<Tenant> Tenants { get; set; } = null!;
    public DbSet<Team> Teams { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<TeamMember> TeamMembers { get; set; } = null!;
    public DbSet<RoleAssignment> RoleAssignments { get; set; } = null!;
    public DbSet<Invitation> Invitations { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Application>(e =>
        {
            e.ToTable("Applications");
            e.HasIndex(a => a.Key).IsUnique();
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("Roles");
            e.HasIndex(r => new { r.ApplicationId, r.Name }).IsUnique();
            e.HasOne<Application>().WithMany().HasForeignKey(r => r.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("Tenants");
            e.HasIndex(t => new { t.ApplicationId, t.Name }).IsUnique();
            e.HasOne<Application>().WithMany().HasForeignKey(t => t.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Team>(e =>
        {
            e.ToTable("Teams");
            e.HasIndex(t => new { t.TenantId, t.Name }).IsUnique();
            // Filtered unique index: only one IsDefault team per tenant
            e.HasIndex(t => t.TenantId)
                .IsUnique()
                .HasFilter("[IsDefault] = 1");
            e.HasOne<Tenant>().WithMany().HasForeignKey(t => t.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.Property(u => u.ObjectId).HasMaxLength(64);
            e.Property(u => u.IssuerTenantId).HasMaxLength(64);
            // Filtered unique index: only when ObjectId is not null
            e.HasIndex(u => new { u.ObjectId, u.IssuerTenantId })
                .IsUnique()
                .HasFilter("[ObjectId] IS NOT NULL");
        });

        modelBuilder.Entity<TeamMember>(e =>
        {
            e.ToTable("TeamMembers");
            e.HasIndex(tm => new { tm.TeamId, tm.UserId }).IsUnique();
            e.HasOne<Team>().WithMany().HasForeignKey(tm => tm.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(tm => tm.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RoleAssignment>(e =>
        {
            e.ToTable("RoleAssignments");
            e.HasIndex(ra => new { ra.TeamMemberId, ra.RoleId }).IsUnique();
            e.HasOne<TeamMember>().WithMany().HasForeignKey(ra => ra.TeamMemberId)
                .OnDelete(DeleteBehavior.Cascade);
            // NO ACTION: deleting an assigned role must fail at the database
            e.HasOne<Role>().WithMany().HasForeignKey(ra => ra.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Invitation>(e =>
        {
            e.ToTable("Invitations");
            e.Property(i => i.TokenHash).HasMaxLength(64).IsFixedLength();
            e.HasIndex(i => i.TokenHash).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(i => i.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(i => i.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
