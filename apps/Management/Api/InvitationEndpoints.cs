using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PS.AppPlatform.Auth;
using PS.AppPlatform.Tenancy;
using PS.Management.Api.Contracts;
using PS.Management.Registry;

namespace PS.Management.Api;

public sealed class InvitationEndpoints(
    RegistryService registryService,
    RegistryDbContext db,
    IOptions<RegistryOptions> options)
{
    // Admin endpoint: create invitation
    public async Task<IActionResult> CreateInvitationAsync(HttpRequest req, Guid userId, CancellationToken ct)
    {
        try
        {
            var actorIdentity = req.HttpContext.User.GetIdentityKey();
            if (actorIdentity == null)
                return new UnauthorizedResult();

            var actor = await db.Users.FindAsync(new object[] { new Guid(actorIdentity.ObjectId) }, cancellationToken: ct);
            if (actor == null)
                return new UnauthorizedResult();

            var (invitationId, token, expiresUtc) = await registryService.CreateInvitationAsync(
                userId, actor.Id, options.Value, ct);

            var url = $"{options.Value.PublicBaseUrl.TrimEnd('/')}/invite/{token}";
            return new OkObjectResult(new InvitationCreatedResponse(invitationId, url, expiresUtc));
        }
        catch (RegistryNotFoundException)
        {
            return new NotFoundResult();
        }
        catch (RegistryConflictException ex)
        {
            return new ConflictObjectResult(new { code = ex.Code });
        }
    }

    // Admin endpoint: list user invitations
    public async Task<IActionResult> GetInvitationsAsync(HttpRequest req, Guid userId, CancellationToken ct)
    {
        try
        {
            var invitations = await registryService.GetUserInvitationsAsync(userId, ct);

            var summaries = invitations.Select(i =>
            {
                var status = i.RevokedUtc.HasValue ? "revoked"
                    : i.AcceptedUtc.HasValue ? "accepted"
                    : DateTime.UtcNow > i.ExpiresUtc ? "expired"
                    : "open";

                return new InvitationSummary(
                    i.Id,
                    i.ExpiresUtc,
                    i.AcceptedUtc,
                    i.RevokedUtc,
                    status);
            }).ToList();

            return new OkObjectResult(summaries);
        }
        catch (RegistryNotFoundException)
        {
            return new NotFoundResult();
        }
    }

    // Admin endpoint: revoke invitation
    public async Task<IActionResult> RevokeInvitationAsync(HttpRequest req, Guid invitationId, CancellationToken ct)
    {
        try
        {
            await registryService.RevokeInvitationAsync(invitationId, ct);
            return new OkResult();
        }
        catch (RegistryNotFoundException)
        {
            return new NotFoundResult();
        }
    }

    // Public endpoint: preview invitation
    public async Task<IActionResult> PreviewInvitationAsync(HttpRequest req, string token, CancellationToken ct)
    {
        try
        {
            var (displayName, applications, expiresUtc) = await registryService.PreviewInvitationAsync(token, ct);
            return new OkObjectResult(new InvitationPreviewResponse(displayName, applications, expiresUtc));
        }
        catch (RegistryNotFoundException)
        {
            return new NotFoundResult();
        }
        catch (RegistryConflictException ex)
        {
            if (ex.Code == "invitation_expired" || ex.Code == "invitation_used")
                return new StatusCodeResult(StatusCodes.Status410Gone);
            return new ConflictObjectResult(new { code = ex.Code });
        }
    }

    // Public endpoint: accept invitation
    public async Task<IActionResult> AcceptInvitationAsync(HttpRequest req, string token, CancellationToken ct)
    {
        try
        {
            var callerIdentity = req.HttpContext.User.GetIdentityKey();
            if (callerIdentity == null)
                return new UnauthorizedResult();

            await registryService.AcceptInvitationAsync(token, callerIdentity, ct);

            // Try to evict from cache
            try
            {
                if (req.HttpContext.RequestServices.GetService(typeof(ITenantDirectory)) is CachingTenantDirectory cache)
                {
                    cache.Evict(callerIdentity);
                }
            }
            catch { }

            var user = await db.Users.FirstOrDefaultAsync(
                u => u.ObjectId == callerIdentity.ObjectId && u.IssuerTenantId == callerIdentity.IssuerTenantId, ct);

            return new OkObjectResult(new InvitationAcceptedResponse(user?.DisplayName ?? ""));
        }
        catch (RegistryNotFoundException)
        {
            return new NotFoundResult();
        }
        catch (RegistryConflictException ex)
        {
            if (ex.Code == "invitation_expired" || ex.Code == "invitation_used")
                return new StatusCodeResult(StatusCodes.Status410Gone);
            return new ConflictObjectResult(new { code = ex.Code });
        }
    }
}
