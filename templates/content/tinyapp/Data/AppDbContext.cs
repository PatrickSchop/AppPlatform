using Microsoft.EntityFrameworkCore;
using Wisdi.AppPlatform.Data;
using Wisdi.AppPlatform.Hosting;

namespace TinyApp.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, PlatformAssemblies assemblies)
    : PlatformDbContext(options, assemblies)
{
    // Add your entity DbSets here.
    // Example: public DbSet<YourEntity> YourEntities { get; set; } = null!;
}
