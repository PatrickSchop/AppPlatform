using Microsoft.EntityFrameworkCore;
using MultiTenantSample.Data;
using PS.AppPlatform.Data;
using PS.AppPlatform.Hosting;

namespace MultiTenantSample.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, PlatformAssemblies assemblies)
    : PlatformDbContext(options, assemblies)
{
    public DbSet<Project> Projects { get; set; } = null!;
    public DbSet<Country> Countries { get; set; } = null!;
    public DbSet<TenantAlias> TenantAliases { get; set; } = null!;
    public DbSet<Feedback> Feedback { get; set; } = null!;
}
