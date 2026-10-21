using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using PS.AppPlatform.Auth;
using PS.AppPlatform.Generated;
using Xunit;

namespace PS.Management.Tests;

public class TenancyFunctionsPolicyTests
{
    [Fact]
    public void GetMyTenants_IgnoresRequiredRole()
    {
        // A user who holds no role must still learn that they are not registered. With the default
        // policy, requiredRole turns that answer into a 403 and the login flow cannot start.
        var method = typeof(TenancyFunctions).GetMethod(nameof(TenancyFunctions.GetMyTenants))!;

        var authorize = Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(PlatformPolicies.AuthenticatedOnly, authorize.Policy);
    }
}
