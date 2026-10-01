using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PS.AppPlatform.Auth;
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

public class RegistryConflictException : Exception
{
    public string Code { get; }
    public RegistryConflictException(string code) : base(code) => Code = code;
}

public class RegistryValidationException : Exception
{
    public string Field { get; }
    public RegistryValidationException(string field, string message) : base(message) => Field = field;
}

public class RegistryNotFoundException : Exception
{
    public RegistryNotFoundException() : base("not_found") { }
}

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

    public async Task<Application?> GetApplicationByKeyAsync(string key, CancellationToken ct)
    {
        return await db.Applications.FirstOrDefaultAsync(a => a.Key == key, ct);
    }

    // --- Applications ---
    public async Task<List<Application>> GetApplicationsAsync(CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var apps = await db.Applications.ToListAsync(ct);
            await tx.CommitAsync(ct);
            return apps;
        });
    }

    public async Task<object?> GetApplicationDetailAsync(Guid appId, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == appId, ct)
                ?? throw new RegistryNotFoundException();

            var roles = await db.Roles
                .Where(r => r.ApplicationId == appId)
                .ToListAsync(ct);

            var roleCounts = await db.RoleAssignments
                .Where(ra => roles.Select(r => r.Id).Contains(ra.RoleId))
                .GroupBy(ra => ra.RoleId)
                .Select(g => new { RoleId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RoleId, x => x.Count, ct);

            var rolesWithCounts = roles.Select(r => new
            {
                r.Id,
                r.Name,
                r.DisplayName,
                r.Description,
                r.IsDeprecated,
                AssignmentCount = roleCounts.ContainsKey(r.Id) ? roleCounts[r.Id] : 0
            }).ToList();

            var tenants = await db.Tenants
                .Where(t => t.ApplicationId == appId)
                .ToListAsync(ct);

            await tx.CommitAsync(ct);

            return new
            {
                app.Id,
                app.Key,
                app.DisplayName,
                app.Tenancy,
                app.IsDisabled,
                Roles = rolesWithCounts,
                Tenants = tenants
            };
        });
    }

    public async Task UpdateApplicationAsync(Guid appId, string? displayName, bool? isDisabled, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == appId, ct)
                ?? throw new RegistryNotFoundException();

            if (isDisabled.HasValue && app.Key == "management" && isDisabled.Value)
            {
                throw new RegistryConflictException("cannot_disable_management");
            }

            if (displayName != null)
                app.DisplayName = displayName;
            if (isDisabled.HasValue)
                app.IsDisabled = isDisabled.Value;

            app.UpdatedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return 0;
        });
    }

    public async Task DeleteDeprecatedRoleAsync(Guid roleId, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct)
                ?? throw new RegistryNotFoundException();

            if (!role.IsDeprecated)
                throw new RegistryConflictException("role_not_deprecated");

            var hasAssignments = await db.RoleAssignments
                .AnyAsync(ra => ra.RoleId == roleId, ct);

            if (hasAssignments)
                throw new RegistryConflictException("role_in_use");

            db.Roles.Remove(role);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return 0;
        });
    }

    // --- Tenants ---
    public async Task<Tenant> CreateTenantAsync(Guid appId, string name, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == appId, ct)
                ?? throw new RegistryNotFoundException();

            if (app.Tenancy == TenancyMode.Single)
            {
                var existingTenant = await db.Tenants
                    .FirstOrDefaultAsync(t => t.ApplicationId == appId, ct);
                if (existingTenant != null)
                    throw new RegistryConflictException("single_tenant_app");
            }

            var tenant = new Tenant
            {
                ApplicationId = appId,
                Name = name,
                CreatedUtc = DateTime.UtcNow,
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);

            var defaultTeam = new Team
            {
                TenantId = tenant.Id,
                Name = "Default",
                IsDefault = true,
            };
            db.Teams.Add(defaultTeam);
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
            return tenant;
        });
    }

    public async Task UpdateTenantAsync(Guid tenantId, string? name, bool? isDisabled, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
                ?? throw new RegistryNotFoundException();

            if (name != null)
                tenant.Name = name;
            if (isDisabled.HasValue)
                tenant.IsDisabled = isDisabled.Value;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return 0;
        });
    }

    public async Task DeleteTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
                ?? throw new RegistryNotFoundException();

            var appTenantCount = await db.Tenants
                .CountAsync(t => t.ApplicationId == tenant.ApplicationId && !t.IsDisabled, ct);

            if (appTenantCount <= 1)
                throw new RegistryConflictException("last_tenant");

            db.Tenants.Remove(tenant);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return 0;
        });
    }

    // --- Teams ---
    public async Task<Team> CreateTeamAsync(Guid tenantId, string name, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
                ?? throw new RegistryNotFoundException();

            var team = new Team
            {
                TenantId = tenantId,
                Name = name,
                IsDefault = false,
            };
            db.Teams.Add(team);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return team;
        });
    }

    public async Task UpdateTeamAsync(Guid teamId, string? name, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId, ct)
                ?? throw new RegistryNotFoundException();

            if (team.IsDefault)
                throw new RegistryConflictException("default_team");

            if (name != null)
                team.Name = name;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return 0;
        });
    }

    public async Task DeleteTeamAsync(Guid teamId, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId, ct)
                ?? throw new RegistryNotFoundException();

            if (team.IsDefault)
                throw new RegistryConflictException("default_team");

            db.Teams.Remove(team);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return 0;
        });
    }

    // --- Users ---
    public async Task<User> CreateUserAsync(string displayName, string? email, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var user = new User
            {
                DisplayName = displayName,
                Email = email,
                CreatedUtc = DateTime.UtcNow,
                BoundUtc = null,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return user;
        });
    }

    public async Task UpdateUserAsync(Guid userId, string? displayName, string? email, bool? isDisabled, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                ?? throw new RegistryNotFoundException();

            if (isDisabled.HasValue && isDisabled.Value && user.BoundUtc.HasValue)
            {
                await AssertNotLastAdminAsync(userId, ct);
            }

            if (displayName != null)
                user.DisplayName = displayName;
            if (email != null)
                user.Email = email;
            if (isDisabled.HasValue)
                user.IsDisabled = isDisabled.Value;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return 0;
        });
    }

    // --- Memberships ---
    public async Task<TeamMember> AddMemberAsync(Guid teamId, Guid userId, List<Guid> roleIds, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId, ct)
                ?? throw new RegistryNotFoundException();

            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == team.TenantId, ct)
                ?? throw new RegistryNotFoundException();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                ?? throw new RegistryNotFoundException();

            var roles = await db.Roles
                .Where(r => roleIds.Contains(r.Id))
                .ToListAsync(ct);

            if (roles.Any(r => r.IsDeprecated))
                throw new RegistryValidationException("roleIds", "deprecated_role");

            var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == tenant.ApplicationId, ct)
                ?? throw new RegistryNotFoundException();

            if (roles.Any(r => r.ApplicationId != app.Id))
                throw new RegistryValidationException("roleIds", "cross_app_role");

            var member = new TeamMember
            {
                TeamId = teamId,
                UserId = userId,
            };
            db.TeamMembers.Add(member);
            await db.SaveChangesAsync(ct);

            foreach (var roleId in roleIds)
            {
                db.RoleAssignments.Add(new RoleAssignment
                {
                    TeamMemberId = member.Id,
                    RoleId = roleId,
                });
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return member;
        });
    }

    public async Task SetMemberRolesAsync(Guid memberId, List<Guid> roleIds, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var member = await db.TeamMembers.FirstOrDefaultAsync(m => m.Id == memberId, ct)
                ?? throw new RegistryNotFoundException();

            var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == member.TeamId, ct)
                ?? throw new RegistryNotFoundException();

            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == team.TenantId, ct)
                ?? throw new RegistryNotFoundException();

            var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == tenant.ApplicationId, ct)
                ?? throw new RegistryNotFoundException();

            var roles = await db.Roles
                .Where(r => roleIds.Contains(r.Id))
                .ToListAsync(ct);

            if (roles.Any(r => r.IsDeprecated))
                throw new RegistryValidationException("roleIds", "deprecated_role");

            if (roles.Any(r => r.ApplicationId != app.Id))
                throw new RegistryValidationException("roleIds", "cross_app_role");

            var hasAdminBefore = await db.RoleAssignments
                .Join(db.Roles, ra => ra.RoleId, r => r.Id, (ra, r) => new { ra, r })
                .AnyAsync(x => x.ra.TeamMemberId == memberId && x.r.Name == "admin", ct);

            var willHaveAdminAfter = roles.Any(r => r.Name == "admin");

            if (hasAdminBefore && !willHaveAdminAfter && app.Key == "management")
            {
                await AssertNotLastAdminAsync(member.UserId, ct);
            }

            var existingAssignments = await db.RoleAssignments
                .Where(ra => ra.TeamMemberId == memberId)
                .ToListAsync(ct);

            db.RoleAssignments.RemoveRange(existingAssignments);

            foreach (var roleId in roleIds)
            {
                db.RoleAssignments.Add(new RoleAssignment
                {
                    TeamMemberId = memberId,
                    RoleId = roleId,
                });
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return 0;
        });
    }

    public async Task RemoveMemberAsync(Guid memberId, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var member = await db.TeamMembers
                .FirstOrDefaultAsync(m => m.Id == memberId, ct)
                ?? throw new RegistryNotFoundException();

            var hasAdmin = await db.RoleAssignments
                .Join(db.Roles, ra => ra.RoleId, r => r.Id, (ra, r) => new { ra, r })
                .AnyAsync(x => x.ra.TeamMemberId == memberId && x.r.Name == "admin", ct);

            if (hasAdmin)
            {
                var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == member.TeamId, ct)
                    ?? throw new RegistryNotFoundException();

                var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == team.TenantId, ct)
                    ?? throw new RegistryNotFoundException();

                var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == tenant.ApplicationId, ct)
                    ?? throw new RegistryNotFoundException();

                if (app.Key == "management")
                {
                    await AssertNotLastAdminAsync(member.UserId, ct);
                }
            }

            db.TeamMembers.Remove(member);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return 0;
        });
    }

    // --- Invitations ---
    public async Task<(Guid invitationId, string token, DateTime expiresUtc)> CreateInvitationAsync(
        Guid userId, Guid actorUserId, RegistryOptions options, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new RegistryNotFoundException();

        if (user.BoundUtc.HasValue)
        {
            throw new RegistryConflictException("already_bound");
        }

        // Revoke any existing open invitations
        var existingInvitations = await db.Invitations
            .Where(i => i.UserId == userId && i.RevokedUtc == null && i.AcceptedUtc == null)
            .ToListAsync(ct);

        foreach (var inv in existingInvitations)
        {
            inv.RevokedUtc = DateTime.UtcNow;
        }

        // Generate token
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        var token = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        // Hash token
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLower();

        // Create invitation
        var expiresUtc = DateTime.UtcNow.AddMinutes(options.InvitationLifetimeMinutes);
        var invitation = new Invitation
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresUtc = expiresUtc,
            CreatedByUserId = actorUserId
        };

        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);

        return (invitation.Id, token, expiresUtc);
    }

    public async Task<Invitation?> GetInvitationByTokenHashAsync(string tokenHash, CancellationToken ct)
    {
        return await db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == tokenHash, ct);
    }

    public async Task<(string DisplayName, List<string> Applications, DateTime ExpiresUtc)> PreviewInvitationAsync(string token, CancellationToken ct)
    {
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLower();
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == tokenHash, ct);

        if (invitation == null)
        {
            throw new RegistryNotFoundException();
        }

        // Constant-time compare for defence-in-depth
        var computedHashBytes = Encoding.UTF8.GetBytes(tokenHash);
        var storedHashBytes = Encoding.UTF8.GetBytes(invitation.TokenHash);
        if (!CryptographicOperations.FixedTimeEquals(storedHashBytes, computedHashBytes))
        {
            throw new RegistryNotFoundException();
        }

        if (invitation.RevokedUtc.HasValue)
        {
            throw new RegistryNotFoundException();
        }

        if (DateTime.UtcNow > invitation.ExpiresUtc)
        {
            throw new RegistryConflictException("invitation_expired");
        }

        if (invitation.AcceptedUtc.HasValue)
        {
            throw new RegistryConflictException("invitation_used");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == invitation.UserId, ct)
            ?? throw new RegistryNotFoundException();

        var applications = await db.TeamMembers
            .Where(tm => tm.UserId == user.Id)
            .Join(db.Teams, tm => tm.TeamId, t => t.Id, (tm, t) => new { tm, t })
            .Join(db.Tenants, x => x.t.TenantId, tn => tn.Id, (x, tn) => new { x.tm, x.t, tn })
            .Join(db.Applications, x => x.tn.ApplicationId, a => a.Id, (x, a) => a.DisplayName)
            .Distinct()
            .ToListAsync(ct);

        return (user.DisplayName, applications, invitation.ExpiresUtc);
    }

    public async Task AcceptInvitationAsync(string token, IdentityKey callerIdentity, CancellationToken ct)
    {
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLower();
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == tokenHash, ct);

        if (invitation == null)
        {
            throw new RegistryNotFoundException();
        }

        // Constant-time compare for defence-in-depth
        var computedHashBytes = Encoding.UTF8.GetBytes(tokenHash);
        var storedHashBytes = Encoding.UTF8.GetBytes(invitation.TokenHash);
        if (!CryptographicOperations.FixedTimeEquals(storedHashBytes, computedHashBytes))
        {
            throw new RegistryNotFoundException();
        }

        if (invitation.RevokedUtc.HasValue)
        {
            throw new RegistryNotFoundException();
        }

        if (DateTime.UtcNow > invitation.ExpiresUtc)
        {
            throw new RegistryConflictException("invitation_expired");
        }

        if (invitation.AcceptedUtc.HasValue)
        {
            throw new RegistryConflictException("invitation_used");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == invitation.UserId, ct)
            ?? throw new RegistryNotFoundException();

        if (user.BoundUtc.HasValue)
        {
            throw new RegistryConflictException("already_bound");
        }

        // Check if identity is already registered to another user
        var existingUser = await db.Users.FirstOrDefaultAsync(
            u => u.ObjectId == callerIdentity.ObjectId && u.IssuerTenantId == callerIdentity.IssuerTenantId, ct);

        if (existingUser != null && existingUser.Id != user.Id)
        {
            throw new RegistryConflictException("identity_already_registered");
        }

        try
        {
            user.ObjectId = callerIdentity.ObjectId;
            user.IssuerTenantId = callerIdentity.IssuerTenantId;
            user.BoundUtc = DateTime.UtcNow;
            invitation.AcceptedUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Users_ObjectId_IssuerTenantId") == true)
        {
            throw new RegistryConflictException("identity_already_registered");
        }
    }

    public async Task RevokeInvitationAsync(Guid invitationId, CancellationToken ct)
    {
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Id == invitationId, ct)
            ?? throw new RegistryNotFoundException();

        invitation.RevokedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<Invitation>> GetUserInvitationsAsync(Guid userId, CancellationToken ct)
    {
        return await db.Invitations
            .Where(i => i.UserId == userId)
            .ToListAsync(ct);
    }

    // --- Helpers ---
    private async Task AssertNotLastAdminAsync(Guid userId, CancellationToken ct)
    {
        var managementApp = await db.Applications.FirstOrDefaultAsync(a => a.Key == "management", ct)
            ?? throw new RegistryNotFoundException();

        var defaultTenant = await db.Tenants.FirstOrDefaultAsync(
            t => t.ApplicationId == managementApp.Id && t.Name == "Default", ct)
            ?? throw new RegistryNotFoundException();

        var defaultTeam = await db.Teams.FirstOrDefaultAsync(
            t => t.TenantId == defaultTenant.Id && t.IsDefault, ct)
            ?? throw new RegistryNotFoundException();

        var adminRole = await db.Roles.FirstOrDefaultAsync(
            r => r.ApplicationId == managementApp.Id && r.Name == "admin", ct)
            ?? throw new RegistryNotFoundException();

        var enabledAdminCount = await db.RoleAssignments
            .Join(db.TeamMembers, ra => ra.TeamMemberId, tm => tm.Id, (ra, tm) => new { ra, tm })
            .Join(db.Users, x => x.tm.UserId, u => u.Id, (x, u) => new { x.ra, x.tm, u })
            .Where(x =>
                x.tm.TeamId == defaultTeam.Id &&
                x.ra.RoleId == adminRole.Id &&
                x.u.BoundUtc.HasValue &&
                !x.u.IsDisabled)
            .CountAsync(ct);

        if (enabledAdminCount <= 1)
        {
            throw new RegistryConflictException("last_admin");
        }
    }
}
