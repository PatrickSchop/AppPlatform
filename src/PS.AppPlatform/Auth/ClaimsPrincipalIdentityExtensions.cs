using System.Security.Claims;
using PS.AppPlatform.Tenancy;

namespace PS.AppPlatform.Auth;

public static class ClaimsPrincipalIdentityExtensions
{
    private const string ObjectIdLongType = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string TenantIdLongType = "http://schemas.microsoft.com/identity/claims/tenantid";

    /// <summary>The (oid, tid) pair, checking both short and mapped claim types. Null if either is missing.</summary>
    public static IdentityKey? GetIdentityKey(this ClaimsPrincipal user)
    {
        var oid = user.FindFirst("oid")?.Value ?? user.FindFirst(ObjectIdLongType)?.Value;
        var tid = user.FindFirst("tid")?.Value ?? user.FindFirst(TenantIdLongType)?.Value;

        return string.IsNullOrEmpty(oid) || string.IsNullOrEmpty(tid) ? null : new IdentityKey(oid, tid);
    }
}
