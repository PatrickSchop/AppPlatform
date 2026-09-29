using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PS.AppPlatform.Endpoints;
using PS.AppPlatform.Tenancy;
using Xunit;

namespace PS.AppPlatform.Tests;

public class TenancyEndpointsTests
{
    private static readonly Guid TenantAcme = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantZeta = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("11111111-0000-0000-0000-000000000001");

    [Fact]
    public async Task Registered_user_gets_tenants_sorted_by_name_with_roles()
    {
        var directory = Substitute.For<ITenantDirectory>();
        directory.GetMembershipsAsync(new IdentityKey("u-oid", "u-tid"), Arg.Any<CancellationToken>())
            .Returns(new UserMemberships(UserId, "Patrick", [
                new TenantMembership(TenantZeta, "Zeta", [Guid.NewGuid()], ["viewer"]),
                new TenantMembership(TenantAcme, "Acme", [Guid.NewGuid()], ["editor", "admin"]),
            ]));

        var endpoint = new TenancyEndpoints(directory, NullLogger<TenancyEndpoints>.Instance);
        var request = Request("u-oid", "u-tid");

        var result = Assert.IsType<OkObjectResult>(await endpoint.GetMyTenantsAsync(request));
        var response = Assert.IsType<MyTenantsResponse>(result.Value);

        Assert.True(response.registered);
        Assert.Equal("Patrick", response.displayName);
        Assert.Collection(response.tenants,
            t => { Assert.Equal(TenantAcme, t.tenantId); Assert.Equal("Acme", t.name); Assert.Equal(["editor", "admin"], t.roles); },
            t => { Assert.Equal(TenantZeta, t.tenantId); Assert.Equal("Zeta", t.name); });
    }

    [Fact]
    public async Task Unregistered_user_gets_registered_false_with_no_tenants()
    {
        var directory = Substitute.For<ITenantDirectory>();
        directory.GetMembershipsAsync(Arg.Any<IdentityKey>(), Arg.Any<CancellationToken>())
            .Returns((UserMemberships?)null);

        var endpoint = new TenancyEndpoints(directory, NullLogger<TenancyEndpoints>.Instance);
        var request = Request("unknown-oid", "u-tid");

        var result = Assert.IsType<OkObjectResult>(await endpoint.GetMyTenantsAsync(request));
        var response = Assert.IsType<MyTenantsResponse>(result.Value);

        Assert.False(response.registered);
        Assert.Null(response.displayName);
        Assert.Empty(response.tenants);
    }

    [Fact]
    public async Task Missing_identity_claims_returns_unauthorized()
    {
        var directory = Substitute.For<ITenantDirectory>();
        var endpoint = new TenancyEndpoints(directory, NullLogger<TenancyEndpoints>.Instance);

        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.IsType<UnauthorizedResult>(await endpoint.GetMyTenantsAsync(context.Request));
        await directory.DidNotReceive().GetMembershipsAsync(Arg.Any<IdentityKey>(), Arg.Any<CancellationToken>());
    }

    private static HttpRequest Request(string oid, string tid)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("oid", oid),
            new Claim("tid", tid),
        ], "test"));
        return context.Request;
    }
}
