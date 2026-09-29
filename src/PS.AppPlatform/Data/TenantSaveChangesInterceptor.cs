using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PS.AppPlatform.Data;

public sealed class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ValidateChanges(eventData.Context);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ValidateChanges(eventData.Context);
        return result;
    }

    private static void ValidateChanges(DbContext? dbContext)
    {
        if (dbContext is not PlatformDbContext platformContext)
        {
            return;
        }

        var entries = platformContext.ChangeTracker
            .Entries()
            .Where(e => e.Entity is TenantEntity)
            .ToList();

        foreach (var entry in entries)
        {
            var tenantEntity = (TenantEntity)entry.Entity;

            switch (entry.State)
            {
                case EntityState.Added:
                    HandleAdd(entry, tenantEntity, platformContext);
                    break;

                case EntityState.Modified:
                    HandleModify(entry, tenantEntity, platformContext);
                    break;

                case EntityState.Deleted:
                    HandleDelete(entry, tenantEntity, platformContext);
                    break;
            }
        }
    }

    private static void HandleAdd(EntityEntry entry, TenantEntity tenantEntity, PlatformDbContext platformContext)
    {
        if (platformContext.TenantFilterEnabled)
        {
            if (tenantEntity.TenantId == Guid.Empty)
            {
                tenantEntity.TenantId = platformContext.CurrentTenantId;
            }
            else if (tenantEntity.TenantId != platformContext.CurrentTenantId)
            {
                throw new TenantMismatchException(
                    $"Cannot insert {entry.Entity.GetType().Name} for a different tenant. " +
                    $"Requested tenant: {tenantEntity.TenantId}, current tenant: {platformContext.CurrentTenantId}");
            }
        }
        else
        {
            if (tenantEntity.TenantId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    $"Unscoped insert of {entry.Entity.GetType().Name} must set TenantId explicitly. " +
                    $"Use IScopedDbContextFactory.CreateForTenant(...) or set TenantId before saving.");
            }
        }
    }

    private static void HandleModify(EntityEntry entry, TenantEntity tenantEntity, PlatformDbContext platformContext)
    {
        var tenantIdProperty = entry.Property(nameof(TenantEntity.TenantId));

        if (tenantIdProperty.IsModified)
        {
            throw new TenantMismatchException(
                $"Cannot modify TenantId of {entry.Entity.GetType().Name}. " +
                $"Rows never move between tenants.");
        }

        if (platformContext.TenantFilterEnabled)
        {
            var originalTenantId = (Guid)tenantIdProperty.OriginalValue!;
            if (originalTenantId != platformContext.CurrentTenantId)
            {
                throw new TenantMismatchException(
                    $"Cannot modify {entry.Entity.GetType().Name} from a different tenant. " +
                    $"Original tenant: {originalTenantId}, current tenant: {platformContext.CurrentTenantId}");
            }
        }
    }

    private static void HandleDelete(EntityEntry entry, TenantEntity tenantEntity, PlatformDbContext platformContext)
    {
        if (platformContext.TenantFilterEnabled)
        {
            var originalTenantId = (Guid)(entry.Property(nameof(TenantEntity.TenantId)).OriginalValue ?? tenantEntity.TenantId);
            if (originalTenantId != platformContext.CurrentTenantId)
            {
                throw new TenantMismatchException(
                    $"Cannot delete {entry.Entity.GetType().Name} from a different tenant. " +
                    $"Original tenant: {originalTenantId}, current tenant: {platformContext.CurrentTenantId}");
            }
        }
    }
}
