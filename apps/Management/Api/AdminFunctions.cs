using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace PS.Management.Api;

public class AdminFunctions(AdminEndpoints inner)
{
    [Function("GetApplications")]
    public Task<IActionResult> GetApplications(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/admin/applications")] HttpRequest req)
        => inner.GetApplicationsAsync(req, req.HttpContext.RequestAborted);

    [Function("GetApplication")]
    public Task<IActionResult> GetApplication(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/admin/applications/{appId}")] HttpRequest req, string appId)
        => inner.GetApplicationAsync(req, appId, req.HttpContext.RequestAborted);

    [Function("PatchApplication")]
    public Task<IActionResult> PatchApplication(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "api/admin/applications/{appId}")] HttpRequest req, string appId)
        => inner.PatchApplicationAsync(req, appId, req.HttpContext.RequestAborted);

    [Function("DeleteRole")]
    public Task<IActionResult> DeleteRole(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "api/admin/applications/{appId}/roles/{roleId}")] HttpRequest req, string appId, string roleId)
        => inner.DeleteRoleAsync(req, appId, roleId, req.HttpContext.RequestAborted);

    [Function("GetTenants")]
    public Task<IActionResult> GetTenants(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/admin/applications/{appId}/tenants")] HttpRequest req, string appId)
        => inner.GetTenantsAsync(req, appId, req.HttpContext.RequestAborted);

    [Function("CreateTenant")]
    public Task<IActionResult> CreateTenant(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/admin/applications/{appId}/tenants")] HttpRequest req, string appId)
        => inner.CreateTenantAsync(req, appId, req.HttpContext.RequestAborted);

    [Function("PatchTenant")]
    public Task<IActionResult> PatchTenant(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "api/admin/applications/{appId}/tenants/{tenantId}")] HttpRequest req, string appId, string tenantId)
        => inner.PatchTenantAsync(req, appId, tenantId, req.HttpContext.RequestAborted);

    [Function("DeleteTenant")]
    public Task<IActionResult> DeleteTenant(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "api/admin/applications/{appId}/tenants/{tenantId}")] HttpRequest req, string appId, string tenantId)
        => inner.DeleteTenantAsync(req, appId, tenantId, req.HttpContext.RequestAborted);

    [Function("GetTeams")]
    public Task<IActionResult> GetTeams(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/admin/applications/{appId}/tenants/{tenantId}/teams")] HttpRequest req, string appId, string tenantId)
        => inner.GetTeamsAsync(req, appId, tenantId, req.HttpContext.RequestAborted);

    [Function("CreateTeam")]
    public Task<IActionResult> CreateTeam(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/admin/applications/{appId}/tenants/{tenantId}/teams")] HttpRequest req, string appId, string tenantId)
        => inner.CreateTeamAsync(req, appId, tenantId, req.HttpContext.RequestAborted);

    [Function("PatchTeam")]
    public Task<IActionResult> PatchTeam(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "api/admin/applications/{appId}/tenants/{tenantId}/teams/{teamId}")] HttpRequest req, string appId, string tenantId, string teamId)
        => inner.PatchTeamAsync(req, appId, tenantId, teamId, req.HttpContext.RequestAborted);

    [Function("DeleteTeam")]
    public Task<IActionResult> DeleteTeam(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "api/admin/applications/{appId}/tenants/{tenantId}/teams/{teamId}")] HttpRequest req, string appId, string tenantId, string teamId)
        => inner.DeleteTeamAsync(req, appId, tenantId, teamId, req.HttpContext.RequestAborted);

    [Function("GetUsers")]
    public Task<IActionResult> GetUsers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/admin/applications/{appId}/users")] HttpRequest req, string appId)
        => inner.GetUsersAsync(req, appId, req.HttpContext.RequestAborted);

    [Function("CreateUser")]
    public Task<IActionResult> CreateUser(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/admin/applications/{appId}/users")] HttpRequest req, string appId)
        => inner.CreateUserAsync(req, appId, req.HttpContext.RequestAborted);

    [Function("GetUser")]
    public Task<IActionResult> GetUser(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/admin/applications/{appId}/users/{userId}")] HttpRequest req, string appId, string userId)
        => inner.GetUserAsync(req, appId, userId, req.HttpContext.RequestAborted);

    [Function("PatchUser")]
    public Task<IActionResult> PatchUser(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "api/admin/applications/{appId}/users/{userId}")] HttpRequest req, string appId, string userId)
        => inner.PatchUserAsync(req, appId, userId, req.HttpContext.RequestAborted);

    [Function("GetMembers")]
    public Task<IActionResult> GetMembers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/admin/applications/{appId}/tenants/{tenantId}/teams/{teamId}/members")] HttpRequest req, string appId, string tenantId, string teamId)
        => inner.GetMembersAsync(req, appId, tenantId, teamId, req.HttpContext.RequestAborted);

    [Function("AddMember")]
    public Task<IActionResult> AddMember(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/admin/applications/{appId}/tenants/{tenantId}/teams/{teamId}/members")] HttpRequest req, string appId, string tenantId, string teamId)
        => inner.AddMemberAsync(req, appId, tenantId, teamId, req.HttpContext.RequestAborted);

    [Function("SetMemberRoles")]
    public Task<IActionResult> SetMemberRoles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "api/admin/applications/{appId}/tenants/{tenantId}/teams/{teamId}/members/{memberId}/roles")] HttpRequest req, string appId, string tenantId, string teamId, string memberId)
        => inner.SetMemberRolesAsync(req, appId, tenantId, teamId, memberId, req.HttpContext.RequestAborted);

    [Function("RemoveMember")]
    public Task<IActionResult> RemoveMember(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "api/admin/applications/{appId}/tenants/{tenantId}/teams/{teamId}/members/{memberId}")] HttpRequest req, string appId, string tenantId, string teamId, string memberId)
        => inner.RemoveMemberAsync(req, appId, tenantId, teamId, memberId, req.HttpContext.RequestAborted);
}
