using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PS.AppPlatform.Auth;
using PS.Management.Api.Contracts;
using PS.Management.Registry;

namespace PS.Management.Api;

public sealed class AdminEndpoints(
    RegistryService registryService,
    RegistryDbContext db,
    ILogger<AdminEndpoints> logger)
{
    // Applications
    public async Task<IActionResult> GetApplicationsAsync(HttpRequest req, CancellationToken ct)
    {
        var apps = await (from a in db.Applications
                          select new ApplicationSummary(
                              a.Id,
                              a.Key,
                              a.DisplayName,
                              a.Tenancy,
                              db.Tenants.Count(t => t.ApplicationId == a.Id),
                              db.TeamMembers
                                  .Join(db.Teams, tm => tm.TeamId, t => t.Id, (tm, t) => new { tm, t })
                                  .Where(x => db.Tenants.Any(t => t.Id == x.t.TenantId && t.ApplicationId == a.Id))
                                  .Select(x => x.tm.UserId).Distinct().Count(),
                              a.IsDisabled))
            .ToListAsync(ct);
        return new OkObjectResult(apps);
    }

    public async Task<IActionResult> GetApplicationAsync(HttpRequest req, string appId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var id))
            return new BadRequestObjectResult(new { error = "invalid_app_id" });

        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == id, cancellationToken: ct);
        if (app == null)
            return new NotFoundObjectResult(new { error = "not_found" });

        var roles = await (from r in db.Roles
                           where r.ApplicationId == id
                           let assignmentCount = db.RoleAssignments.Count(ra => ra.RoleId == r.Id)
                           select new RoleSummary(r.Id, r.Name, r.DisplayName, r.IsDeprecated, assignmentCount))
            .ToListAsync(ct);

        var tenants = await (from t in db.Tenants
                             where t.ApplicationId == id
                             select new TenantSummary(t.Id, t.Name, t.IsDisabled))
            .ToListAsync(ct);

        var detail = new ApplicationDetail(app.Id, app.Key, app.DisplayName, app.Tenancy, app.IsDisabled, roles, tenants);
        return new OkObjectResult(detail);
    }

    public async Task<IActionResult> PatchApplicationAsync(HttpRequest req, string appId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var id))
            return new BadRequestObjectResult(new { error = "invalid_app_id" });

        var oid = req.HttpContext.User.GetIdentityKey()?.ObjectId;
        if (oid == null)
            return new UnauthorizedResult();

        var body = await DeserializeAsync<PatchApplicationRequest>(req, ct);
        if (body == null)
            return new BadRequestObjectResult(new { error = "invalid_body" });

        try
        {
            await registryService.UpdateApplicationAsync(id, body.DisplayName, body.IsDisabled, ct);
            logger.LogInformation("Admin {Actor} patched application {AppId}", oid, id);
            return new OkResult();
        }
        catch (RegistryNotFoundException)
        {
            return new NotFoundObjectResult(new { error = "not_found" });
        }
    }

    public async Task<IActionResult> DeleteRoleAsync(HttpRequest req, string appId, string roleId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var appIdGuid) || !Guid.TryParse(roleId, out var roleIdGuid))
            return new BadRequestObjectResult(new { error = "invalid_ids" });

        var oid = req.HttpContext.User.GetIdentityKey()?.ObjectId;
        if (oid == null)
            return new UnauthorizedResult();

        try
        {
            await registryService.DeleteDeprecatedRoleAsync(roleIdGuid, ct);
            logger.LogInformation("Admin {Actor} deleted role {RoleId} from app {AppId}", oid, roleIdGuid, appIdGuid);
            return new OkResult();
        }
        catch (RegistryNotFoundException)
        {
            return new NotFoundObjectResult(new { error = "not_found" });
        }
        catch (RegistryConflictException ex)
        {
            return new ObjectResult(new { error = ex.Code }) { StatusCode = 409 };
        }
    }

    // Tenants
    public async Task<IActionResult> GetTenantsAsync(HttpRequest req, string appId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var id))
            return new BadRequestObjectResult(new { error = "invalid_app_id" });

        var tenants = await (from t in db.Tenants
                             where t.ApplicationId == id
                             select new TenantSummary(t.Id, t.Name, t.IsDisabled))
            .ToListAsync(ct);

        return new OkObjectResult(tenants);
    }

    public async Task<IActionResult> CreateTenantAsync(HttpRequest req, string appId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var id))
            return new BadRequestObjectResult(new { error = "invalid_app_id" });

        var oid = req.HttpContext.User.GetIdentityKey()?.ObjectId;
        if (oid == null)
            return new UnauthorizedResult();

        var body = await DeserializeAsync<CreateTenantRequest>(req, ct);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return new BadRequestObjectResult(new { error = "invalid_body" });

        try
        {
            await registryService.CreateTenantAsync(id, body.Name, ct);
            logger.LogInformation("Admin {Actor} created tenant {Name} in app {AppId}", oid, body.Name, id);
            return new CreatedResult("", null);
        }
        catch (RegistryConflictException ex)
        {
            return new ObjectResult(new { error = ex.Code }) { StatusCode = 409 };
        }
    }

    public async Task<IActionResult> PatchTenantAsync(HttpRequest req, string appId, string tenantId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var appIdGuid) || !Guid.TryParse(tenantId, out var tenantIdGuid))
            return new BadRequestObjectResult(new { error = "invalid_ids" });

        var oid = req.HttpContext.User.GetIdentityKey()?.ObjectId;
        if (oid == null)
            return new UnauthorizedResult();

        var body = await DeserializeAsync<PatchTenantRequest>(req, ct);
        if (body == null)
            return new BadRequestObjectResult(new { error = "invalid_body" });

        try
        {
            await registryService.UpdateTenantAsync(tenantIdGuid, body.Name, body.IsDisabled, ct);
            logger.LogInformation("Admin {Actor} patched tenant {TenantId} in app {AppId}", oid, tenantIdGuid, appIdGuid);
            return new OkResult();
        }
        catch (RegistryNotFoundException)
        {
            return new NotFoundObjectResult(new { error = "not_found" });
        }
        catch (RegistryConflictException ex)
        {
            return new ObjectResult(new { error = ex.Code }) { StatusCode = 409 };
        }
    }

    public async Task<IActionResult> DeleteTenantAsync(HttpRequest req, string appId, string tenantId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var appIdGuid) || !Guid.TryParse(tenantId, out var tenantIdGuid))
            return new BadRequestObjectResult(new { error = "invalid_ids" });

        var oid = req.HttpContext.User.GetIdentityKey()?.ObjectId;
        if (oid == null)
            return new UnauthorizedResult();

        try
        {
            await registryService.DeleteTenantAsync(tenantIdGuid, ct);
            logger.LogInformation("Admin {Actor} deleted tenant {TenantId} from app {AppId}", oid, tenantIdGuid, appIdGuid);
            return new OkResult();
        }
        catch (RegistryNotFoundException)
        {
            return new NotFoundObjectResult(new { error = "not_found" });
        }
        catch (RegistryConflictException ex)
        {
            return new ObjectResult(new { error = ex.Code }) { StatusCode = 409 };
        }
    }

    // Teams
    public async Task<IActionResult> GetTeamsAsync(HttpRequest req, string appId, string tenantId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var appIdGuid) || !Guid.TryParse(tenantId, out var tenantIdGuid))
            return new BadRequestObjectResult(new { error = "invalid_ids" });

        var teams = await (from t in db.Teams
                           where t.TenantId == tenantIdGuid
                           select new TeamSummary(t.Id, t.Name, t.IsDefault))
            .ToListAsync(ct);

        return new OkObjectResult(teams);
    }

    public async Task<IActionResult> CreateTeamAsync(HttpRequest req, string appId, string tenantId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var appIdGuid) || !Guid.TryParse(tenantId, out var tenantIdGuid))
            return new BadRequestObjectResult(new { error = "invalid_ids" });

        var oid = req.HttpContext.User.GetIdentityKey()?.ObjectId;
        if (oid == null)
            return new UnauthorizedResult();

        var body = await DeserializeAsync<CreateTeamRequest>(req, ct);
        if (body == null || string.IsNullOrEmpty(body.Name))
            return new BadRequestObjectResult(new { error = "invalid_body" });

        try
        {
            await registryService.CreateTeamAsync(tenantIdGuid, body.Name, ct);
            logger.LogInformation("Admin {Actor} created team {Name} in tenant {TenantId}", oid, body.Name, tenantIdGuid);
            return new CreatedResult("", null);
        }
        catch (RegistryNotFoundException)
        {
            return new NotFoundObjectResult(new { error = "not_found" });
        }
        catch (RegistryConflictException ex)
        {
            return new ObjectResult(new { error = ex.Code }) { StatusCode = 409 };
        }
    }

    public async Task<IActionResult> PatchTeamAsync(HttpRequest req, string appId, string tenantId, string teamId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var appIdGuid) || !Guid.TryParse(tenantId, out var tenantIdGuid) || !Guid.TryParse(teamId, out var teamIdGuid))
            return new BadRequestObjectResult(new { error = "invalid_ids" });

        var oid = req.HttpContext.User.GetIdentityKey()?.ObjectId;
        if (oid == null)
            return new UnauthorizedResult();

        var body = await DeserializeAsync<PatchTeamRequest>(req, ct);
        if (body == null)
            return new BadRequestObjectResult(new { error = "invalid_body" });

        try
        {
            await registryService.UpdateTeamAsync(teamIdGuid, body.Name, ct);
            logger.LogInformation("Admin {Actor} patched team {TeamId} in tenant {TenantId}", oid, teamIdGuid, tenantIdGuid);
            return new OkResult();
        }
        catch (RegistryNotFoundException)
        {
            return new NotFoundObjectResult(new { error = "not_found" });
        }
        catch (RegistryConflictException ex)
        {
            return new ObjectResult(new { error = ex.Code }) { StatusCode = 409 };
        }
    }

    public async Task<IActionResult> DeleteTeamAsync(HttpRequest req, string appId, string tenantId, string teamId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var appIdGuid) || !Guid.TryParse(tenantId, out var tenantIdGuid) || !Guid.TryParse(teamId, out var teamIdGuid))
            return new BadRequestObjectResult(new { error = "invalid_ids" });

        var oid = req.HttpContext.User.GetIdentityKey()?.ObjectId;
        if (oid == null)
            return new UnauthorizedResult();

        try
        {
            await registryService.DeleteTeamAsync(teamIdGuid, ct);
            logger.LogInformation("Admin {Actor} deleted team {TeamId} from tenant {TenantId}", oid, teamIdGuid, tenantIdGuid);
            return new OkResult();
        }
        catch (RegistryNotFoundException)
        {
            return new NotFoundObjectResult(new { error = "not_found" });
        }
        catch (RegistryConflictException ex)
        {
            return new ObjectResult(new { error = ex.Code }) { StatusCode = 409 };
        }
    }

    // Users
    public async Task<IActionResult> GetUsersAsync(HttpRequest req, string appId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var id))
            return new BadRequestObjectResult(new { error = "invalid_app_id" });

        var appTenantIds = await db.Tenants.Where(t => t.ApplicationId == id).Select(t => t.Id).ToListAsync(ct);

        var users = await (from u in db.Users
                           where db.TeamMembers.Any(tm => appTenantIds.Contains(
                               db.Teams.Where(t => t.Id == tm.TeamId).Select(t => t.TenantId).FirstOrDefault()) && tm.UserId == u.Id)
                           select new UserSummary(
                               u.Id,
                               u.DisplayName,
                               u.Email,
                               u.BoundUtc != null && !u.IsDisabled ? "active" : u.IsDisabled ? "disabled" : "invited",
                               u.CreatedUtc))
            .Distinct()
            .ToListAsync(ct);

        return new OkObjectResult(users);
    }

    public async Task<IActionResult> CreateUserAsync(HttpRequest req, string appId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var id))
            return new BadRequestObjectResult(new { error = "invalid_app_id" });

        var oid = req.HttpContext.User.GetIdentityKey()?.ObjectId;
        if (oid == null)
            return new UnauthorizedResult();

        var body = await DeserializeAsync<CreateUserRequest>(req, ct);
        if (body == null || string.IsNullOrEmpty(body.DisplayName))
            return new BadRequestObjectResult(new { error = "invalid_body" });

        try
        {
            await registryService.CreateUserAsync(body.DisplayName, body.Email, ct);
            logger.LogInformation("Admin {Actor} created user {DisplayName} in app {AppId}", oid, body.DisplayName, id);
            return new CreatedResult("", null);
        }
        catch (RegistryNotFoundException)
        {
            return new NotFoundObjectResult(new { error = "not_found" });
        }
    }

    public async Task<IActionResult> GetUserAsync(HttpRequest req, string appId, string userId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var appIdGuid) || !Guid.TryParse(userId, out var userIdGuid))
            return new BadRequestObjectResult(new { error = "invalid_ids" });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userIdGuid, cancellationToken: ct);
        if (user == null)
            return new NotFoundObjectResult(new { error = "not_found" });

        var appTenantIds = await db.Tenants.Where(t => t.ApplicationId == appIdGuid).Select(t => t.Id).ToListAsync(ct);

        var memberships = await (from tm in db.TeamMembers
                                 where tm.UserId == userIdGuid
                                 join t in db.Teams on tm.TeamId equals t.Id
                                 where appTenantIds.Contains(t.TenantId)
                                 join tenant in db.Tenants on t.TenantId equals tenant.Id
                                 join app in db.Applications on tenant.ApplicationId equals app.Id
                                 select new UserMembershipInfo(
                                     app.Key,
                                     tenant.Name,
                                     t.Name,
                                     tm.Id,
                                     db.RoleAssignments.Where(ra => ra.TeamMemberId == tm.Id)
                                         .Join(db.Roles, ra => ra.RoleId, r => r.Id, (ra, r) => r.Name)
                                         .ToList()))
            .ToListAsync(ct);

        var detail = new UserDetail(
            user.Id,
            user.DisplayName,
            user.Email,
            user.BoundUtc != null && !user.IsDisabled ? "active" : user.IsDisabled ? "disabled" : "invited",
            user.CreatedUtc,
            memberships);

        return new OkObjectResult(detail);
    }

    public async Task<IActionResult> PatchUserAsync(HttpRequest req, string appId, string userId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var appIdGuid) || !Guid.TryParse(userId, out var userIdGuid))
            return new BadRequestObjectResult(new { error = "invalid_ids" });

        var oid = req.HttpContext.User.GetIdentityKey()?.ObjectId;
        if (oid == null)
            return new UnauthorizedResult();

        var body = await DeserializeAsync<PatchUserRequest>(req, ct);
        if (body == null)
            return new BadRequestObjectResult(new { error = "invalid_body" });

        try
        {
            await registryService.UpdateUserAsync(userIdGuid, body.DisplayName, body.Email, body.IsDisabled, ct);
            logger.LogInformation("Admin {Actor} patched user {UserId} in app {AppId}", oid, userIdGuid, appIdGuid);
            return new OkResult();
        }
        catch (RegistryNotFoundException)
        {
            return new NotFoundObjectResult(new { error = "not_found" });
        }
    }

    // Members
    public async Task<IActionResult> GetMembersAsync(HttpRequest req, string appId, string tenantId, string teamId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var appIdGuid) || !Guid.TryParse(tenantId, out var tenantIdGuid) || !Guid.TryParse(teamId, out var teamIdGuid))
            return new BadRequestObjectResult(new { error = "invalid_ids" });

        var teamMembers = await (from tm in db.TeamMembers
                                 where tm.TeamId == teamIdGuid
                                 select tm.Id).ToListAsync(ct);

        var members = await (from tm in db.TeamMembers
                             where tm.TeamId == teamIdGuid
                             join u in db.Users on tm.UserId equals u.Id
                             join ra in db.RoleAssignments on tm.Id equals ra.TeamMemberId into ras
                             select new MemberSummary(
                                 tm.Id,
                                 tm.UserId,
                                 u.DisplayName,
                                 ras.Join(db.Roles, ra => ra.RoleId, r => r.Id, (ra, r) => r.Name).ToList()))
            .ToListAsync(ct);

        return new OkObjectResult(members);
    }

    public async Task<IActionResult> AddMemberAsync(HttpRequest req, string appId, string tenantId, string teamId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var appIdGuid) || !Guid.TryParse(tenantId, out var tenantIdGuid) || !Guid.TryParse(teamId, out var teamIdGuid))
            return new BadRequestObjectResult(new { error = "invalid_ids" });

        var oid = req.HttpContext.User.GetIdentityKey()?.ObjectId;
        if (oid == null)
            return new UnauthorizedResult();

        var body = await DeserializeAsync<AddMemberRequest>(req, ct);
        if (body == null)
            return new BadRequestObjectResult(new { error = "invalid_body" });

        try
        {
            await registryService.AddMemberAsync(teamIdGuid, body.UserId, body.RoleIds, ct);
            logger.LogInformation("Admin {Actor} added member {UserId} to team {TeamId}", oid, body.UserId, teamIdGuid);
            return new CreatedResult("", null);
        }
        catch (RegistryNotFoundException)
        {
            return new NotFoundObjectResult(new { error = "not_found" });
        }
        catch (RegistryValidationException ex)
        {
            var details = new Dictionary<string, string> { { ex.Field ?? "unknown", "invalid" } };
            return new BadRequestObjectResult(new { error = "validation", details });
        }
        catch (RegistryConflictException ex)
        {
            return new ObjectResult(new { error = ex.Code }) { StatusCode = 409 };
        }
    }

    public async Task<IActionResult> SetMemberRolesAsync(HttpRequest req, string appId, string tenantId, string teamId, string memberId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var appIdGuid) || !Guid.TryParse(tenantId, out var tenantIdGuid) ||
            !Guid.TryParse(teamId, out var teamIdGuid) || !Guid.TryParse(memberId, out var memberIdGuid))
            return new BadRequestObjectResult(new { error = "invalid_ids" });

        var oid = req.HttpContext.User.GetIdentityKey()?.ObjectId;
        if (oid == null)
            return new UnauthorizedResult();

        var body = await DeserializeAsync<SetRolesRequest>(req, ct);
        if (body == null)
            return new BadRequestObjectResult(new { error = "invalid_body" });

        try
        {
            await registryService.SetMemberRolesAsync(memberIdGuid, body.RoleIds, ct);
            logger.LogInformation("Admin {Actor} set roles for member {MemberId} in team {TeamId}", oid, memberIdGuid, teamIdGuid);
            return new OkResult();
        }
        catch (RegistryNotFoundException)
        {
            return new NotFoundObjectResult(new { error = "not_found" });
        }
        catch (RegistryValidationException ex)
        {
            var details = new Dictionary<string, string> { { ex.Field ?? "unknown", "invalid" } };
            return new BadRequestObjectResult(new { error = "validation", details });
        }
        catch (RegistryConflictException ex)
        {
            return new ObjectResult(new { error = ex.Code }) { StatusCode = 409 };
        }
    }

    public async Task<IActionResult> RemoveMemberAsync(HttpRequest req, string appId, string tenantId, string teamId, string memberId, CancellationToken ct)
    {
        if (!Guid.TryParse(appId, out var appIdGuid) || !Guid.TryParse(tenantId, out var tenantIdGuid) ||
            !Guid.TryParse(teamId, out var teamIdGuid) || !Guid.TryParse(memberId, out var memberIdGuid))
            return new BadRequestObjectResult(new { error = "invalid_ids" });

        var oid = req.HttpContext.User.GetIdentityKey()?.ObjectId;
        if (oid == null)
            return new UnauthorizedResult();

        try
        {
            await registryService.RemoveMemberAsync(memberIdGuid, ct);
            logger.LogInformation("Admin {Actor} removed member {MemberId} from team {TeamId}", oid, memberIdGuid, teamIdGuid);
            return new OkResult();
        }
        catch (RegistryNotFoundException)
        {
            return new NotFoundObjectResult(new { error = "not_found" });
        }
        catch (RegistryConflictException ex)
        {
            return new ObjectResult(new { error = ex.Code }) { StatusCode = 409 };
        }
    }

    private async Task<T?> DeserializeAsync<T>(HttpRequest req, CancellationToken ct)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(req.Body, JsonSerializerOptions.Web, ct);
        }
        catch
        {
            return default;
        }
    }
}
