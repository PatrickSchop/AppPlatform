using Microsoft.EntityFrameworkCore;
using System.Reflection;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tasks;

namespace PS.AppPlatform.Data;

/// <summary>
/// Base DbContext for platform apps. Holds only the platform's own entities; apps derive
/// from this and add their own DbSets. The derived context must pass PlatformAssemblies
/// through its own constructor, e.g.:
///
/// public class AppDbContext : PlatformDbContext
/// {
///     public AppDbContext(DbContextOptions&lt;AppDbContext&gt; options, PlatformAssemblies assemblies)
///         : base(options, assemblies) { }
///
///     public DbSet&lt;Note&gt; Notes { get; set; } = null!;
/// }
/// </summary>
public abstract class PlatformDbContext : DbContext
{
    private readonly PlatformAssemblies _assemblies;

    protected PlatformDbContext(DbContextOptions options, PlatformAssemblies assemblies)
        : base(options)
    {
        _assemblies = assemblies;
    }

    public DbSet<BackgroundTask> BackgroundTasks { get; set; } = null!;

    public DbSet<T> GetEntitySet<T>() where T : Entity => Set<T>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in DiscoverEntityTypes())
        {
            modelBuilder.Entity(entityType)
                .Property(nameof(Entity.Id))
                .HasDefaultValueSql("NEWID()")
                .ValueGeneratedOnAdd();
        }

        ConfigurePlatformModel(modelBuilder);
    }

    private static void ConfigurePlatformModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BackgroundTask>(e =>
        {
            e.ToTable("BackgroundTasks");
            e.HasIndex(t => t.Status);
            e.HasIndex(t => t.TaskType);
            e.HasIndex(t => t.CreatedDate);
            e.HasIndex(t => new { t.ExecutionManagerId, t.Status });
        });
    }

    private IEnumerable<Type> DiscoverEntityTypes() =>
        _assemblies.All
            .SelectMany(SafeGetTypes)
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(Entity)));

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}

