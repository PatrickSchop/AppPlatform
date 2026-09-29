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
    private TenantScope _tenantScope = TenantScope.Disabled;
    private bool _scopeApplied;

    protected PlatformDbContext(DbContextOptions options, PlatformAssemblies assemblies)
        : base(options)
    {
        _assemblies = assemblies;
    }

    public DbSet<BackgroundTask> BackgroundTasks { get; set; } = null!;

    /// <summary>Called once by the platform factories, before first use.</summary>
    public void ApplyTenantScope(TenantScope scope)
    {
        if (_scopeApplied)
        {
            throw new InvalidOperationException("ApplyTenantScope() called more than once on this context");
        }

        _scopeApplied = true;
        _tenantScope = scope;
    }

    public bool TenantFilterEnabled => _tenantScope.Enabled;

    /// <summary>Guid.Empty when filtering is disabled; throws when enabled without a tenant.</summary>
    public Guid CurrentTenantId => !_tenantScope.Enabled
        ? Guid.Empty
        : _tenantScope.TenantId ?? throw new TenantContextMissingException();

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

        ConfigurePlatformModel(this, modelBuilder);
    }

    /// <summary>Derived contexts that override this must call base, or TenantEntity loses its filter.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Conventions.Add(_ => new TenantQueryFilterConvention(this));
    }

    private static void ConfigurePlatformModel(PlatformDbContext context, ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BackgroundTask>(e =>
        {
            e.ToTable("BackgroundTasks");
            e.HasIndex(t => t.Status);
            e.HasIndex(t => t.TaskType);
            e.HasIndex(t => t.CreatedDate);
            e.HasIndex(t => new { t.ExecutionManagerId, t.Status });
            e.HasIndex(t => new { t.TenantId, t.CreatedDate });

            // With the filter enabled, tasks with a null TenantId (system tasks) are invisible
            // to users; that is intended. EF re-binds the DbContext constant to the executing
            // context on every query, the same as TenantQueryFilterConvention.
            e.HasQueryFilter(t => !context.TenantFilterEnabled || t.TenantId == context.CurrentTenantId);
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

