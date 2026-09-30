using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PS.AppPlatform.Auth;

namespace PS.AppPlatform.Tenancy;

public abstract record TenantResolution
{
    public sealed record Continue : TenantResolution;
    public sealed record Stop(int StatusCode, string Error) : TenantResolution;

    internal static readonly Continue Proceed = new();
}

public static class TenantErrors
{
    public const string NotRegistered = "not_registered";
    public const string TenantForbidden = "tenant_forbidden";
    public const string TenantRequired = "tenant_required";
    public const string RegistryUnavailable = "registry_unavailable";
}

/// <summary>
/// Runs between authentication and authorization: validates the user's tenant against the
/// registry, fills <see cref="TenantContext"/> and makes registry roles the principal's only roles.
/// </summary>
public sealed class TenantResolver(IOptions<TenancyOptions> options, ITenantDirectory directory, TenantContext context)
{
    public const string RoleClaimType = "roles";

    public async Task<TenantResolution> ResolveAsync(HttpContext http, bool tenantOptional, CancellationToken ct)
    {
        if (http.User.Identity?.IsAuthenticated != true)
        {
            return TenantResolution.Proceed;
        }

        // Token roles are dropped on every path past this point; the registry is the only authority.
        ReplaceRoles(http, []);

        var identity = http.User.GetIdentityKey();
        if (identity is null)
        {
            return new TenantResolution.Stop(StatusCodes.Status403Forbidden, TenantErrors.NotRegistered);
        }

        UserMemberships? memberships;
        try
        {
            memberships = await directory.GetMembershipsAsync(identity, ct);
        }
        catch (TenantDirectoryUnavailableException)
        {
            http.Response.Headers["Retry-After"] = "5";
            return new TenantResolution.Stop(StatusCodes.Status503ServiceUnavailable, TenantErrors.RegistryUnavailable);
        }
        if (memberships is null || memberships.Tenants.Count == 0)
        {
            if (!tenantOptional)
            {
                return new TenantResolution.Stop(StatusCodes.Status403Forbidden, TenantErrors.NotRegistered);
            }

            if (memberships is not null)
            {
                context.SetUser(memberships.UserId);
            }
            return TenantResolution.Proceed;
        }

        var hasHeader = http.Request.Headers.TryGetValue(options.Value.TenantHeader, out var headerValue)
            && !string.IsNullOrEmpty(headerValue.ToString());

        TenantMembership? selected;
        if (hasHeader)
        {
            // A forged header is never silently ignored, even on [TenantOptional] endpoints.
            selected = Guid.TryParse(headerValue.ToString(), out var requested)
                ? memberships.Tenants.FirstOrDefault(t => t.TenantId == requested)
                : null;

            if (selected is null)
            {
                return new TenantResolution.Stop(StatusCodes.Status403Forbidden, TenantErrors.TenantForbidden);
            }
        }
        else if (memberships.Tenants.Count == 1)
        {
            selected = memberships.Tenants[0];
        }
        else if (tenantOptional)
        {
            context.SetUser(memberships.UserId);
            return TenantResolution.Proceed;
        }
        else
        {
            return new TenantResolution.Stop(StatusCodes.Status409Conflict, TenantErrors.TenantRequired);
        }

        context.Set(memberships.UserId, selected.TenantId, selected.TeamIds, selected.Roles);
        ReplaceRoles(http, context.Roles);
        return TenantResolution.Proceed;
    }

    private static void ReplaceRoles(HttpContext http, IEnumerable<string> roles)
    {
        var source = http.User.Identity as ClaimsIdentity;
        var claims = http.User.Claims
            .Where(c => c.Type != RoleClaimType && c.Type != ClaimTypes.Role)
            .Select(c => new Claim(c.Type, c.Value, c.ValueType, c.Issuer, c.OriginalIssuer))
            .Concat(roles.Select(r => new Claim(RoleClaimType, r)));

        var identity = new ClaimsIdentity(
            claims,
            source?.AuthenticationType,
            source?.NameClaimType ?? ClaimsIdentity.DefaultNameClaimType,
            RoleClaimType);

        http.User = new ClaimsPrincipal(identity);
    }
}
