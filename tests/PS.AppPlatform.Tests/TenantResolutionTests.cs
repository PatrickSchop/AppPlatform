using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using PS.AppPlatform.Auth;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using Xunit;

namespace PS.AppPlatform.Tests;

public class TenantResolutionTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid TenantC = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid UserU = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private const string Tid = "home-tid";

    // U: A (editor), B (viewer). V: A (viewer). X: A (editor, member).
    private static Dictionary<string, string?> DirectoryConfig() => new()
    {
        ["tenancy:mode"] = "multi",
        ["tenancy:directory"] = "config",
        ["tenancy:devDirectory:tenants:0:id"] = TenantA.ToString(),
        ["tenancy:devDirectory:tenants:0:name"] = "A",
        ["tenancy:devDirectory:tenants:1:id"] = TenantB.ToString(),
        ["tenancy:devDirectory:tenants:1:name"] = "B",
        ["tenancy:devDirectory:tenants:2:id"] = TenantC.ToString(),
        ["tenancy:devDirectory:tenants:2:name"] = "C",

        ["tenancy:devDirectory:users:0:id"] = UserU.ToString(),
        ["tenancy:devDirectory:users:0:oid"] = "u-oid",
        ["tenancy:devDirectory:users:0:tid"] = Tid,
        ["tenancy:devDirectory:users:0:displayName"] = "U",
        ["tenancy:devDirectory:users:0:memberships:0:tenantId"] = TenantA.ToString(),
        ["tenancy:devDirectory:users:0:memberships:0:roles:0"] = "editor",
        ["tenancy:devDirectory:users:0:memberships:1:tenantId"] = TenantB.ToString(),
        ["tenancy:devDirectory:users:0:memberships:1:roles:0"] = "viewer",

        ["tenancy:devDirectory:users:1:id"] = Guid.NewGuid().ToString(),
        ["tenancy:devDirectory:users:1:oid"] = "v-oid",
        ["tenancy:devDirectory:users:1:tid"] = Tid,
        ["tenancy:devDirectory:users:1:displayName"] = "V",
        ["tenancy:devDirectory:users:1:memberships:0:tenantId"] = TenantA.ToString(),
        ["tenancy:devDirectory:users:1:memberships:0:roles:0"] = "viewer",

        ["tenancy:devDirectory:users:2:id"] = Guid.NewGuid().ToString(),
        ["tenancy:devDirectory:users:2:oid"] = "x-oid",
        ["tenancy:devDirectory:users:2:tid"] = Tid,
        ["tenancy:devDirectory:users:2:displayName"] = "X",
        ["tenancy:devDirectory:users:2:memberships:0:tenantId"] = TenantA.ToString(),
        ["tenancy:devDirectory:users:2:memberships:0:roles:0"] = "editor",
        ["tenancy:devDirectory:users:2:memberships:0:roles:1"] = "member",
    };

    // 1. Mode None

    [Fact]
    public async Task Mode_none_behaves_as_v1_and_never_resolves_tenants()
    {
        using var host = new Harness(tenancy: false);

        var noToken = await host.Invoke(nameof(TenantShimEndpoints.Plain));
        Assert.Equal(401, noToken.Status);
        Assert.Equal("unauthorized", noToken.Error);

        var withToken = await host.Invoke(nameof(TenantShimEndpoints.Plain), oid: "not-in-directory");
        Assert.Equal(200, withToken.Status);
        Assert.Equal(0, host.Directory.Calls);
    }

    // 2–4. Tenant selection

    [Fact]
    public async Task Single_membership_without_header_resolves_that_tenant()
    {
        using var host = new Harness();

        var result = await host.Invoke(nameof(TenantShimEndpoints.Plain), oid: "v-oid");

        Assert.Equal(200, result.Status);
        Assert.Equal(TenantA, result.Tenant!.TenantId);
        Assert.Contains("viewer", result.Tenant.Roles);
    }

    [Fact]
    public async Task Several_memberships_without_header_is_tenant_required()
    {
        using var host = new Harness();

        var result = await host.Invoke(nameof(TenantShimEndpoints.Plain), oid: "u-oid");

        Assert.Equal(409, result.Status);
        Assert.Equal("tenant_required", result.Error);
        Assert.False(result.NextCalled);
    }

    [Fact]
    public async Task Header_selects_a_member_tenant()
    {
        using var host = new Harness();

        var result = await host.Invoke(nameof(TenantShimEndpoints.Plain), oid: "u-oid", tenantHeader: TenantB.ToString());

        Assert.Equal(200, result.Status);
        Assert.Equal(TenantB, result.Tenant!.TenantId);
        Assert.Equal(UserU, result.Tenant.UserId);
    }

    [Theory]
    [InlineData("cccccccc-0000-0000-0000-000000000001")]
    [InlineData("garbage")]
    public async Task Header_for_a_non_member_or_invalid_tenant_is_tenant_forbidden(string header)
    {
        using var host = new Harness();

        var result = await host.Invoke(nameof(TenantShimEndpoints.Plain), oid: "u-oid", tenantHeader: header);

        Assert.Equal(403, result.Status);
        Assert.Equal("tenant_forbidden", result.Error);
        Assert.False(result.NextCalled);
    }

    // 5. Unregistered

    [Theory]
    [InlineData("unknown-oid", Tid)]
    [InlineData("u-oid", "other-tid")]
    [InlineData("u-oid", null)]
    public async Task Unregistered_identity_is_not_registered(string oid, string? tid)
    {
        using var host = new Harness();

        var result = await host.Invoke(nameof(TenantShimEndpoints.Plain), oid: oid, tid: tid);

        Assert.Equal(403, result.Status);
        Assert.Equal("not_registered", result.Error);
    }

    // 6. [TenantOptional]

    [Fact]
    public async Task Tenant_optional_with_several_tenants_sets_user_only()
    {
        using var host = new Harness();

        var result = await host.Invoke(nameof(TenantShimEndpoints.Optional), oid: "u-oid");

        Assert.Equal(200, result.Status);
        Assert.True(result.Tenant!.IsResolved);
        Assert.Equal(UserU, result.Tenant.UserId);
        Assert.Null(result.Tenant.TenantId);
    }

    [Fact]
    public async Task Tenant_optional_lets_an_unregistered_user_through_unresolved()
    {
        using var host = new Harness();

        var result = await host.Invoke(nameof(TenantShimEndpoints.Optional), oid: "unknown-oid");

        Assert.Equal(200, result.Status);
        Assert.False(result.Tenant!.IsResolved);
    }

    [Fact]
    public async Task Tenant_optional_still_rejects_a_bad_header()
    {
        using var host = new Harness();

        var result = await host.Invoke(nameof(TenantShimEndpoints.Optional), oid: "u-oid", tenantHeader: TenantC.ToString());

        Assert.Equal(403, result.Status);
        Assert.Equal("tenant_forbidden", result.Error);
    }

    [Fact]
    public async Task Tenant_optional_class_attribute_applies_to_its_methods()
    {
        using var host = new Harness();

        var result = await host.Invoke(typeof(TenantOptionalShimEndpoints), nameof(TenantOptionalShimEndpoints.Get), oid: "u-oid");

        Assert.Equal(200, result.Status);
    }

    // 7–8a. Roles

    [Fact]
    public async Task Authorize_roles_uses_the_roles_of_the_selected_tenant()
    {
        using var host = new Harness();

        var editorInA = await host.Invoke(nameof(TenantShimEndpoints.EditorOnly), oid: "u-oid", tenantHeader: TenantA.ToString());
        Assert.Equal(200, editorInA.Status);

        var viewerInB = await host.Invoke(nameof(TenantShimEndpoints.EditorOnly), oid: "u-oid", tenantHeader: TenantB.ToString());
        Assert.Equal(403, viewerInB.Status);
        Assert.Equal("forbidden", viewerInB.Error);
    }

    [Fact]
    public async Task Authorize_roles_does_not_bypass_required_role()
    {
        using var host = new Harness(requiredRole: "member");

        var editorWithoutMember = await host.Invoke(nameof(TenantShimEndpoints.EditorOnly), oid: "u-oid", tenantHeader: TenantA.ToString());
        Assert.Equal(403, editorWithoutMember.Status);

        var editorAndMember = await host.Invoke(nameof(TenantShimEndpoints.EditorOnly), oid: "x-oid");
        Assert.Equal(200, editorAndMember.Status);
    }

    [Fact]
    public async Task Token_roles_are_dropped_in_favour_of_registry_roles()
    {
        using var host = new Harness();

        var result = await host.Invoke(nameof(TenantShimEndpoints.EditorOnly), oid: "v-oid", tokenRoles: ["editor"]);

        Assert.Equal(403, result.Status);
    }

    [Fact]
    public async Task Token_roles_cannot_satisfy_required_role()
    {
        using var host = new Harness(requiredRole: "member");

        var result = await host.Invoke(nameof(TenantShimEndpoints.Plain), oid: "v-oid", tokenRoles: ["member"]);

        Assert.Equal(403, result.Status);
    }

    [Fact]
    public async Task Authenticated_only_policy_ignores_required_role()
    {
        using var host = new Harness(requiredRole: "member");

        var withoutRole = await host.Invoke(nameof(TenantShimEndpoints.AuthenticatedOnly), oid: "v-oid");
        Assert.Equal(200, withoutRole.Status);

        var noToken = await host.Invoke(nameof(TenantShimEndpoints.AuthenticatedOnly));
        Assert.Equal(401, noToken.Status);
    }

    [Fact]
    public async Task Authorize_roles_works_without_tenancy()
    {
        using var host = new Harness(tenancy: false);

        var withRole = await host.Invoke(nameof(TenantShimEndpoints.EditorOnly), oid: "any", tokenRoles: ["editor"]);
        Assert.Equal(200, withRole.Status);

        var withoutRole = await host.Invoke(nameof(TenantShimEndpoints.EditorOnly), oid: "any");
        Assert.Equal(403, withoutRole.Status);
    }

    // 9. Anonymous

    [Fact]
    public async Task Anonymous_function_skips_authentication_and_the_directory()
    {
        using var host = new Harness();

        var result = await host.Invoke(nameof(TenantShimEndpoints.Anonymous));

        Assert.Equal(200, result.Status);
        Assert.Equal(0, host.Directory.Calls);
    }

    [Fact]
    public async Task No_token_is_401_before_any_directory_call()
    {
        using var host = new Harness();

        var result = await host.Invoke(nameof(TenantShimEndpoints.Plain));

        Assert.Equal(401, result.Status);
        Assert.Equal(0, host.Directory.Calls);
    }

    // Supporting pieces

    [Fact]
    public void GetIdentityKey_reads_mapped_claim_types()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", "o"),
            new Claim("http://schemas.microsoft.com/identity/claims/tenantid", "t"),
        ], "test"));

        Assert.Equal(new IdentityKey("o", "t"), principal.GetIdentityKey());
    }

    [Fact]
    public void GetTargetFunctionMethod_is_cached_per_entry_point()
    {
        var definition = Substitute.For<FunctionDefinition>();
        definition.EntryPoint.Returns($"{typeof(TenantShimEndpoints).FullName}.{nameof(TenantShimEndpoints.Plain)}");
        var context = Substitute.For<FunctionContext>();
        context.FunctionDefinition.Returns(definition);

        var first = context.GetTargetFunctionMethod();
        var second = context.GetTargetFunctionMethod();

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    private sealed record InvokeResult(int Status, string? Error, bool NextCalled, ITenantContext? Tenant);

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _root;
        private readonly FunctionAuthorizationMiddleware _middleware = new();

        public CountingTenantDirectory Directory { get; }

        public Harness(bool tenancy = true, string? requiredRole = null)
        {
            var values = DirectoryConfig();
            values["authentication:azureEntraId:tenantId"] = "entra-tenant";
            values["authentication:azureEntraId:clientId"] = "entra-client";
            values["authentication:requiredRole"] = requiredRole;
            var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

            var assemblies = new PlatformAssemblies();
            assemblies.Add(typeof(TenantResolutionTests).Assembly);

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            var environment = Substitute.For<IHostEnvironment>();
            environment.EnvironmentName.Returns(Environments.Development);
            services.AddSingleton(environment);
            services.AddSingleton(assemblies);
            services.AddLogging();
            services.AddPlatformAuth(config);

            if (tenancy)
            {
                services.AddPlatformTenancy(config);
            }
            else
            {
                // Registered but never reachable: proves mode None never calls the directory.
                services.AddScoped<TenantContext>();
                services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
            }

            services.AddSingleton(sp => new CountingTenantDirectory(new ConfigTenantDirectory(
                sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHostEnvironment>())));
            services.AddSingleton<ITenantDirectory>(sp => sp.GetRequiredService<CountingTenantDirectory>());
            services.AddSingleton<IAuthenticationService, HeaderAuthenticationService>();

            _root = services.BuildServiceProvider(validateScopes: true);
            Directory = _root.GetRequiredService<CountingTenantDirectory>();
        }

        public Task<InvokeResult> Invoke(
            string method, string? oid = null, string? tid = Tid, string? tenantHeader = null, string[]? tokenRoles = null) =>
            Invoke(typeof(TenantShimEndpoints), method, oid, tid, tenantHeader, tokenRoles);

        public async Task<InvokeResult> Invoke(
            Type shim, string method, string? oid = null, string? tid = Tid, string? tenantHeader = null, string[]? tokenRoles = null)
        {
            await using var scope = _root.CreateAsyncScope();

            var http = new DefaultHttpContext();
            http.Response.Body = new MemoryStream();
            if (oid != null) http.Request.Headers[HeaderAuthenticationService.OidHeader] = oid;
            if (tid != null) http.Request.Headers[HeaderAuthenticationService.TidHeader] = tid;
            if (tokenRoles != null) http.Request.Headers[HeaderAuthenticationService.RolesHeader] = string.Join(',', tokenRoles);
            if (tenantHeader != null) http.Request.Headers["X-Tenant-Id"] = tenantHeader;

            var definition = Substitute.For<FunctionDefinition>();
            definition.Name.Returns(method);
            definition.EntryPoint.Returns($"{shim.FullName}.{method}");

            var context = Substitute.For<FunctionContext>();
            context.FunctionDefinition.Returns(definition);
            context.InstanceServices.Returns(scope.ServiceProvider);
            context.Items.Returns(new Dictionary<object, object> { ["HttpRequestContext"] = http });

            var nextCalled = false;
            ITenantContext? tenant = null;
            await _middleware.Invoke(context, ctx =>
            {
                nextCalled = true;
                tenant = ctx.InstanceServices.GetService<ITenantContext>();
                http.Response.StatusCode = 200;
                return Task.CompletedTask;
            });

            string? error = null;
            if (http.Response.Body.Length > 0)
            {
                http.Response.Body.Position = 0;
                using var doc = await JsonDocument.ParseAsync(http.Response.Body);
                error = doc.RootElement.GetProperty("error").GetString();
            }

            return new InvokeResult(http.Response.StatusCode, error, nextCalled, tenant);
        }

        public void Dispose() => _root.Dispose();
    }

    private sealed class CountingTenantDirectory(ITenantDirectory inner) : ITenantDirectory
    {
        private int _calls;
        public int Calls => _calls;

        public Task<UserMemberships?> GetMembershipsAsync(IdentityKey identity, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return inner.GetMembershipsAsync(identity, ct);
        }
    }

    /// <summary>Authenticates from test headers so no JWT is needed.</summary>
    private sealed class HeaderAuthenticationService : IAuthenticationService
    {
        public const string OidHeader = "X-Test-Oid";
        public const string TidHeader = "X-Test-Tid";
        public const string RolesHeader = "X-Test-Roles";

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            var oid = context.Request.Headers[OidHeader].ToString();
            if (string.IsNullOrEmpty(oid))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new("oid", oid) };
            var tid = context.Request.Headers[TidHeader].ToString();
            if (!string.IsNullOrEmpty(tid)) claims.Add(new Claim("tid", tid));
            foreach (var role in context.Request.Headers[RolesHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                claims.Add(new Claim("roles", role));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test", "name", "roles"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, scheme ?? "Test")));
        }

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }
}

/// <summary>Shaped like a real [Function] shim: instance methods on a public class.</summary>
public class TenantShimEndpoints
{
    public void Plain() { }

    [TenantOptional]
    public void Optional() { }

    [Authorize(Roles = "editor")]
    public void EditorOnly() { }

    [Authorize(Policy = PlatformPolicies.AuthenticatedOnly)]
    public void AuthenticatedOnly() { }

    [AllowAnonymous]
    public void Anonymous() { }
}

[TenantOptional]
public class TenantOptionalShimEndpoints
{
    public void Get() { }
}
