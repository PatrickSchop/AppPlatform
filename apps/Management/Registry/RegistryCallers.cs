using System.Security.Claims;
using Microsoft.Extensions.Logging;
using PS.AppPlatform.Auth;

namespace PS.Management.Registry;

public sealed class RegistryOptions
{
    public const string SectionName = "registry";
    public string TrustedTenantId { get; set; } = "";
    public string[] TrustedDeployers { get; set; } = [];
}

public static class RegistryCallers
{
    /// <summary>Returns null if authorized, or an error string for 403.</summary>
    public static string? CheckRegister(ClaimsPrincipal user, RegistryOptions options, ILogger logger)
    {
        var identity = user.GetIdentityKey();
        if (identity == null)
            return "caller_not_allowed";

        // Check tid matches
        if (identity.IssuerTenantId != options.TrustedTenantId)
        {
            logger.LogWarning("Registry register denied: tid mismatch. oid={oid}", identity.ObjectId);
            return "caller_not_allowed";
        }

        // Check oid in TrustedDeployers
        if (!options.TrustedDeployers.Contains(identity.ObjectId))
        {
            logger.LogWarning("Registry register denied: oid not in trustedDeployers. oid={oid}", identity.ObjectId);
            return "caller_not_allowed";
        }

        return null;
    }

    /// <summary>Returns null if authorized (caller oid matches app's ServicePrincipalId), or error.</summary>
    public static string? CheckMembershipLookup(ClaimsPrincipal user, Guid? appServicePrincipalId, RegistryOptions options, ILogger logger)
    {
        var identity = user.GetIdentityKey();
        if (identity == null)
            return "caller_not_allowed";

        // Check tid matches
        if (identity.IssuerTenantId != options.TrustedTenantId)
        {
            logger.LogWarning("Registry membership lookup denied: tid mismatch. oid={oid}", identity.ObjectId);
            return "caller_not_allowed";
        }

        // Check oid matches appServicePrincipalId
        if (appServicePrincipalId == null || identity.ObjectId != appServicePrincipalId.ToString())
        {
            logger.LogWarning("Registry membership lookup denied: oid mismatch. oid={oid}", identity.ObjectId);
            return "caller_not_allowed";
        }

        return null;
    }
}
