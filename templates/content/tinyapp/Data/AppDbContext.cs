using Microsoft.EntityFrameworkCore;
using PS.AppPlatform.Data;
using PS.AppPlatform.Hosting;

namespace TinyApp.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, PlatformAssemblies assemblies)
    : PlatformDbContext(options, assemblies)
{
    // Add your entity DbSets here.
//#if (TenancyEnabled)
    public DbSet<Item> Items { get; set; } = null!;
//#endif
    // Example: public DbSet<YourEntity> YourEntities { get; set; } = null!;
}

