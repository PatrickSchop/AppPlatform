using Microsoft.EntityFrameworkCore;
using PS.AppPlatform.Tenancy;

namespace PS.Management.Registry;

public sealed class MembershipQuery(RegistryDbContext db)
{
    /// <summary>
    /// Returns the memberships of an identity within an application, or null when the user
    /// is unknown, disabled, or has no membership. Disabled application or tenants are excluded.
    /// </summary>
    public async Task<UserMemberships?> GetAsync(
        string appKey,
        IdentityKey identity,
        CancellationToken ct)
    {
        // Resolve application
        var app = await db.Applications
            .FirstOrDefaultAsync(a => a.Key == appKey && !a.IsDisabled, ct);
        if (app == null) return null;

        // Resolve user — must be bound and not disabled
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.ObjectId == identity.ObjectId &&
                 u.IssuerTenantId == identity.IssuerTenantId &&
                 u.BoundUtc != null &&
                 !u.IsDisabled, ct);
        if (user == null) return null;

        // Load all active tenants for this application
        var tenants = await db.Tenants
            .Where(t => t.ApplicationId == app.Id && !t.IsDisabled)
            .ToListAsync(ct);

        if (tenants.Count == 0) return null;

        var tenantIds = tenants.Select(t => t.Id).ToList();

        // Load all teams in those tenants
        var teams = await db.Teams
            .Where(t => tenantIds.Contains(t.TenantId))
            .ToListAsync(ct);

        var teamIds = teams.Select(t => t.Id).ToList();

        // Load team memberships for this user
        var teamMembers = await db.TeamMembers
            .Where(tm => teamIds.Contains(tm.TeamId) && tm.UserId == user.Id)
            .ToListAsync(ct);

        if (teamMembers.Count == 0) return null;

        var memberIds = teamMembers.Select(tm => tm.Id).ToList();

        // Load non-deprecated role assignments for this user's memberships
        var roleAssignments = await (
            from ra in db.RoleAssignments
            join r in db.Roles on ra.RoleId equals r.Id
            where memberIds.Contains(ra.TeamMemberId) && !r.IsDeprecated
            select new { ra.TeamMemberId, RoleName = r.Name }
        ).ToListAsync(ct);

        // Build per-tenant membership
        var tenantMemberships = new List<TenantMembership>();

        foreach (var tenant in tenants)
        {
            var teamsInTenant = teams.Where(t => t.TenantId == tenant.Id).ToList();
            var membersInTenant = teamMembers
                .Where(tm => teamsInTenant.Any(t => t.Id == tm.TeamId))
                .ToList();

            if (membersInTenant.Count == 0) continue;

            var teamIdsInTenant = membersInTenant.Select(tm => tm.TeamId).Distinct().ToList();
            var roles = roleAssignments
                .Where(ra => membersInTenant.Any(tm => tm.Id == ra.TeamMemberId))
                .Select(ra => ra.RoleName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r)
                .ToList();

            tenantMemberships.Add(new TenantMembership(
                tenant.Id,
                tenant.Name,
                teamIdsInTenant,
                roles));
        }

        if (tenantMemberships.Count == 0) return null;

        return new UserMemberships(user.Id, user.DisplayName, tenantMemberships);
    }
}
