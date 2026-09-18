using Microsoft.EntityFrameworkCore;
using SampleApp.Data;
using Wisdi.AppPlatform.Data;
using Wisdi.AppPlatform.Hosting;

namespace SampleApp.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, PlatformAssemblies assemblies)
    : PlatformDbContext(options, assemblies)
{
    public DbSet<Note> Notes { get; set; } = null!;
}
