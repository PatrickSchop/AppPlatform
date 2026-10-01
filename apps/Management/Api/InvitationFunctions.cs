using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using PS.AppPlatform.Auth;
using PS.AppPlatform.Tenancy;

namespace PS.Management.Api;

public class InvitationFunctions(InvitationEndpoints inner)
{
    // Admin endpoints (default policy = admin role)
    [Function("CreateInvitation")]
    public Task<IActionResult> CreateInvitation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/admin/users/{userId}/invitations")] HttpRequest req, string userId)
        => inner.CreateInvitationAsync(req, Guid.Parse(userId), req.HttpContext.RequestAborted);

    [Function("GetUserInvitations")]
    public Task<IActionResult> GetUserInvitations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/admin/users/{userId}/invitations")] HttpRequest req, string userId)
        => inner.GetInvitationsAsync(req, Guid.Parse(userId), req.HttpContext.RequestAborted);

    [Function("RevokeInvitation")]
    public Task<IActionResult> RevokeInvitation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "api/admin/invitations/{invitationId}")] HttpRequest req, string invitationId)
        => inner.RevokeInvitationAsync(req, Guid.Parse(invitationId), req.HttpContext.RequestAborted);

    // Public endpoints — accessible to any authenticated user
    [Function("PreviewInvitation")]
    [Authorize(Policy = PlatformPolicies.AuthenticatedOnly)]
    [TenantOptional]
    // v1 §4.1a: invitee is authenticated but not yet registered
    public Task<IActionResult> PreviewInvitation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/invitations/{token}")] HttpRequest req, string token)
        => inner.PreviewInvitationAsync(req, token, req.HttpContext.RequestAborted);

    [Function("AcceptInvitation")]
    [Authorize(Policy = PlatformPolicies.AuthenticatedOnly)]
    [TenantOptional]
    // v1 §4.1a: invitee is authenticated but not yet registered
    public Task<IActionResult> AcceptInvitation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/invitations/{token}/accept")] HttpRequest req, string token)
        => inner.AcceptInvitationAsync(req, token, req.HttpContext.RequestAborted);
}
