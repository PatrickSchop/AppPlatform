using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PS.AppPlatform.Tenancy;

namespace PS.AppPlatform.Data;

public interface IScopedDbContextFactory<TContext> : IDbContextFactory<TContext>
    where TContext : PlatformDbContext
{
    /// <summary>Filtered to an explicit tenant, e.g. one resolved by an anonymous endpoint.</summary>
    TContext CreateForTenant(Guid tenantId);
}

public interface IUnscopedDbContextFactory<TContext> where TContext : PlatformDbContext
{
    /// <summary>NO tenant filter. Cross-tenant reporting, platform internals, anonymous flows.</summary>
    TContext CreateDbContext();

    Task<TContext> CreateDbContextAsync(CancellationToken ct = default);
}

public sealed class ScopedDbContextFactory<TContext> : IScopedDbContextFactory<TContext>
    where TContext : PlatformDbContext
{
    private static bool? _hasTenantEntities;
    private readonly IServiceProvider _serviceProvider;
    private readonly TenancyMode _tenancyMode;
    private readonly ITenantContext? _tenantContext;

    public ScopedDbContextFactory(IServiceProvider serviceProvider, TenancyMode tenancyMode)
    {
        _serviceProvider = serviceProvider;
        _tenancyMode = tenancyMode;
        _tenantContext = tenancyMode != TenancyMode.None ? serviceProvider.GetService<ITenantContext>() : null;
    }

    public TContext CreateDbContext()
    {
        ValidateNoneMode();
        var scope = _tenancyMode == TenancyMode.None
            ? TenantScope.Disabled
            : TenantScope.For(_tenantContext?.TenantId);
        return CreateContextWithScope(scope);
    }

    public TContext CreateForTenant(Guid tenantId)
    {
        if (_tenancyMode == TenancyMode.None)
        {
            throw new InvalidOperationException(
                "CreateForTenant() cannot be used in TenancyMode.None. Use CreateDbContext() instead.");
        }

        return CreateContextWithScope(TenantScope.For(tenantId));
    }

    public Task<TContext> CreateDbContextAsync(CancellationToken ct = default)
    {
        return Task.FromResult(CreateDbContext());
    }

    private TContext CreateContextWithScope(TenantScope scope)
    {
        var context = ActivatorUtilities.CreateInstance<TContext>(_serviceProvider);
        context.ApplyTenantScope(scope);
        return context;
    }

    private void ValidateNoneMode()
    {
        if (_tenancyMode != TenancyMode.None)
        {
            return;
        }

        _hasTenantEntities ??= HasTenantEntities();

        if (_hasTenantEntities == true)
        {
            throw new InvalidOperationException(
                $"TenantEntity in {typeof(TContext).Name} requires AddPlatformTenancy to be called. " +
                $"The model contains tenant-aware entities but TenancyMode is None.");
        }
    }

    private bool HasTenantEntities()
    {
        var tempContext = ActivatorUtilities.CreateInstance<TContext>(_serviceProvider);
        tempContext.ApplyTenantScope(TenantScope.Disabled);

        try
        {
            var tenantEntityType = typeof(TenantEntity);
            var entityTypes = tempContext.Model.GetEntityTypes();
            return entityTypes.Any(et => tenantEntityType.IsAssignableFrom(et.ClrType));
        }
        finally
        {
            tempContext.Dispose();
        }
    }
}

public sealed class UnscopedDbContextFactory<TContext> : IUnscopedDbContextFactory<TContext>
    where TContext : PlatformDbContext
{
    private readonly IServiceProvider _serviceProvider;

    public UnscopedDbContextFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public TContext CreateDbContext()
    {
        var context = ActivatorUtilities.CreateInstance<TContext>(_serviceProvider);
        context.ApplyTenantScope(TenantScope.Disabled);
        return context;
    }

    public Task<TContext> CreateDbContextAsync(CancellationToken ct = default)
    {
        return Task.FromResult(CreateDbContext());
    }
}

public sealed class PlatformDbContextFactoryAdapter<TContext> : IDbContextFactory<PlatformDbContext>
    where TContext : PlatformDbContext
{
    private readonly IScopedDbContextFactory<TContext> _innerFactory;

    public PlatformDbContextFactoryAdapter(IScopedDbContextFactory<TContext> innerFactory)
    {
        _innerFactory = innerFactory;
    }

    public PlatformDbContext CreateDbContext()
    {
        return _innerFactory.CreateDbContext();
    }

    public async Task<PlatformDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        return await _innerFactory.CreateDbContextAsync(cancellationToken);
    }
}

public sealed class PlatformUnscopedDbContextFactoryAdapter<TContext> : IUnscopedDbContextFactory<PlatformDbContext>
    where TContext : PlatformDbContext
{
    private readonly IUnscopedDbContextFactory<TContext> _innerFactory;

    public PlatformUnscopedDbContextFactoryAdapter(IUnscopedDbContextFactory<TContext> innerFactory)
    {
        _innerFactory = innerFactory;
    }

    public PlatformDbContext CreateDbContext()
    {
        return _innerFactory.CreateDbContext();
    }

    public async Task<PlatformDbContext> CreateDbContextAsync(CancellationToken ct = default)
    {
        return await _innerFactory.CreateDbContextAsync(ct);
    }
}
