using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace PS.AppPlatform.Data;

/// <summary>
/// Adds the tenant filter to every TenantEntity root in the finished model, however the entity
/// got there (assembly discovery, DbSet, or configuration after base.OnModelCreating). An
/// existing filter is AND-ed with it, never replaced.
/// </summary>
internal sealed class TenantQueryFilterConvention(PlatformDbContext context) : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> conventionContext)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            if (entityType.BaseType != null || !typeof(TenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            // EF re-binds a DbContext constant to the executing context on every query.
            var e = Expression.Parameter(entityType.ClrType, "e");
            var ctx = Expression.Constant(context);
            Expression body = Expression.OrElse(
                Expression.Not(Expression.Property(ctx, nameof(PlatformDbContext.TenantFilterEnabled))),
                Expression.Equal(
                    Expression.Property(e, nameof(TenantEntity.TenantId)),
                    Expression.Property(ctx, nameof(PlatformDbContext.CurrentTenantId))));

            if (entityType.GetQueryFilter() is { } existing)
            {
                var existingBody = new ReplaceParameter(existing.Parameters[0], e).Visit(existing.Body);
                body = Expression.AndAlso(body, existingBody);
            }

            entityType.SetQueryFilter(Expression.Lambda(body, e));
            entityType.Builder.HasIndex([nameof(TenantEntity.TenantId)]);
        }
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}
