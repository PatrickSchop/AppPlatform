using Microsoft.EntityFrameworkCore;
using PS.AppPlatform.Tenancy;

namespace PS.Management.Registry;

public sealed record ApplicationRegistration(
    string Key,
    string DisplayName,
    TenancyMode Tenancy,
    IReadOnlyList<AppRoleDefinition> Roles,
    Guid? ServicePrincipalId);

public sealed record RegistrationResult(
    Guid ApplicationId,
    bool Created,
    IReadOnlyList<string> RolesAdded,
    IReadOnlyList<string> RolesDeprecated,
    IReadOnlyList<string> RolesReactivated);

public sealed class RegistryService(RegistryDbContext db)
{
    public async Task<RegistrationResult> UpsertApplicationAsync(
        ApplicationRegistration registration,
        CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var rolesAdded = new List<string>();
            var rolesDeprecated = new List<string>();
            var rolesReactivated = new List<string>();

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var app = await db.Applications
                .FirstOrDefaultAsync(a => a.Key == registration.Key, ct);

            bool created = app == null;

            if (created)
            {
                app = new Application
                {
                    Key = registration.Key,
                    DisplayName = registration.DisplayName,
                    Tenancy = registration.Tenancy,
                    ServicePrincipalId = registration.ServicePrincipalId,
                    RegisteredUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow,
                };
                db.Applications.Add(app);
                await db.SaveChangesAsync(ct);

                var defaultTenant = new Tenant
                {
                    ApplicationId = app.Id,
                    Name = "Default",
                    CreatedUtc = DateTime.UtcNow,
                };
                db.Tenants.Add(defaultTenant);
                await db.SaveChangesAsync(ct);

                var defaultTeam = new Team
                {
                    TenantId = defaultTenant.Id,
                    Name = "Default",
                    IsDefault = true,
                };
                db.Teams.Add(defaultTeam);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                // Tenancy downgrade check: Multi → Single requires only one enabled tenant
                if (app!.Tenancy == TenancyMode.Multi && registration.Tenancy == TenancyMode.Single)
                {
                    var enabledTenantCount = await db.Tenants
                        .CountAsync(t => t.ApplicationId == app.Id && !t.IsDisabled, ct);
                    if (enabledTenantCount > 1)
                    {
                        throw new InvalidOperationException(
                            $"Cannot change tenancy from Multi to Single: application '{registration.Key}' " +
                            $"has {enabledTenantCount} enabled tenants.");
                    }
                }

                app.DisplayName = registration.DisplayName;
                app.Tenancy = registration.Tenancy;
                if (registration.ServicePrincipalId.HasValue)
                    app.ServicePrincipalId = registration.ServicePrincipalId;
                app.UpdatedUtc = DateTime.UtcNow;
            }

            // Sync roles
            var existingRoles = await db.Roles
                .Where(r => r.ApplicationId == app!.Id)
                .ToListAsync(ct);

            var requestedNames = registration.Roles.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var requestedRole in registration.Roles)
            {
                var existing = existingRoles.FirstOrDefault(r =>
                    string.Equals(r.Name, requestedRole.Name, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    db.Roles.Add(new Role
                    {
                        ApplicationId = app!.Id,
                        Name = requestedRole.Name,
                        DisplayName = requestedRole.DisplayName,
                        Description = requestedRole.Description,
                        IsDeprecated = false,
                    });
                    rolesAdded.Add(requestedRole.Name);
                }
                else if (existing.IsDeprecated)
                {
                    existing.IsDeprecated = false;
                    existing.DisplayName = requestedRole.DisplayName;
                    existing.Description = requestedRole.Description;
                    rolesReactivated.Add(requestedRole.Name);
                }
            }

            foreach (var existingRole in existingRoles)
            {
                if (!requestedNames.Contains(existingRole.Name) && !existingRole.IsDeprecated)
                {
                    existingRole.IsDeprecated = true;
                    rolesDeprecated.Add(existingRole.Name);
                }
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new RegistrationResult(
                app!.Id, created,
                rolesAdded, rolesDeprecated, rolesReactivated);
        });
    }

    /// <summary>
    /// Finds or creates the user by (oid, tid), ensures they are in management/Default/Default team,
    /// ensures they have the admin role assignment, re-enables them if disabled. Returns the user id.
    /// </summary>
    public async Task<Guid> EnsureAdminAsync(IdentityKey identity, string displayName, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Find or create user
            var user = await db.Users.FirstOrDefaultAsync(
                u => u.ObjectId == identity.ObjectId && u.IssuerTenantId == identity.IssuerTenantId, ct);

            if (user == null)
            {
                user = new User
                {
                    ObjectId = identity.ObjectId,
                    IssuerTenantId = identity.IssuerTenantId,
                    DisplayName = displayName,
                    CreatedUtc = DateTime.UtcNow,
                    BoundUtc = DateTime.UtcNow,
                };
                db.Users.Add(user);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                if (user.IsDisabled)
                {
                    user.IsDisabled = false;
                    Console.WriteLine($"Re-enabled user {user.Id}.");
                }
                if (user.BoundUtc == null)
                {
                    user.BoundUtc = DateTime.UtcNow;
                }
            }

            await db.SaveChangesAsync(ct); // persist re-enable / bind of an existing user

            // Resolve management app, Default tenant, Default team
            var app = await db.Applications.FirstOrDefaultAsync(a => a.Key == "management", ct)
                ?? throw new InvalidOperationException(
                    "The 'management' application does not exist. Run --migrate first.");

            var tenant = await db.Tenants.FirstOrDefaultAsync(
                t => t.ApplicationId == app.Id && t.Name == "Default", ct)
                ?? throw new InvalidOperationException("Default tenant not found for management application.");

            var team = await db.Teams.FirstOrDefaultAsync(
                t => t.TenantId == tenant.Id && t.Name == "Default", ct)
                ?? throw new InvalidOperationException("Default team not found for management/Default tenant.");

            var adminRole = await db.Roles.FirstOrDefaultAsync(
                r => r.ApplicationId == app.Id && r.Name == "admin", ct)
                ?? throw new InvalidOperationException("Admin role not found for management application.");

            // Ensure team membership
            var membership = await db.TeamMembers.FirstOrDefaultAsync(
                tm => tm.TeamId == team.Id && tm.UserId == user.Id, ct);

            if (membership == null)
            {
                membership = new TeamMember { TeamId = team.Id, UserId = user.Id };
                db.TeamMembers.Add(membership);
                await db.SaveChangesAsync(ct);
                Console.WriteLine($"Added user {user.Id} to Default team.");
            }

            // Ensure admin role assignment
            var assignment = await db.RoleAssignments.FirstOrDefaultAsync(
                ra => ra.TeamMemberId == membership.Id && ra.RoleId == adminRole.Id, ct);

            if (assignment == null)
            {
                db.RoleAssignments.Add(new RoleAssignment
                {
                    TeamMemberId = membership.Id,
                    RoleId = adminRole.Id,
                });
                await db.SaveChangesAsync(ct);
                Console.WriteLine($"Assigned admin role to user {user.Id}.");
            }

            await tx.CommitAsync(ct);
            return user.Id;
        });
    }
}
