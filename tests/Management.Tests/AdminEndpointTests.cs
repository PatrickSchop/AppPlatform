using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using PS.AppPlatform.Auth;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using PS.Management.Api;
using PS.Management.Registry;
using Xunit;

namespace PS.Management.Tests;

public class AdminEndpointTests
{
    [Fact]
    public async Task Pipeline_Test_6_NonAdmin_Gets_403_Admin_Gets_200()
    {
        using var harness = new Harness();

        // Non-admin should get 403
        var nonAdminResult = await harness.Invoke(typeof(AdminFunctions), "GetApplications", oid: "non-admin-oid", tokenRoles: null);
        Assert.Equal(403, nonAdminResult.Status);
        Assert.Equal("forbidden", nonAdminResult.Error);

        // Admin should get 200
        var adminResult = await harness.Invoke(typeof(AdminFunctions), "GetApplications", oid: "admin-oid", tokenRoles: new[] { "admin" });
        Assert.Equal(200, adminResult.Status);
        Assert.Null(adminResult.Error);
        Assert.True(adminResult.NextCalled);
    }

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _root;
        private readonly FunctionAuthorizationMiddleware _middleware = new();

        public Harness()
        {
            var values = new Dictionary<string, string?>
            {
                ["authentication:azureEntraId:tenantId"] = "entra-tenant",
                ["authentication:azureEntraId:clientId"] = "entra-client",
                ["authentication:requiredRole"] = "admin",
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

            var assemblies = new PlatformAssemblies();
            assemblies.Add(typeof(AdminEndpointTests).Assembly);

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);

            var environment = Substitute.For<IHostEnvironment>();
            environment.EnvironmentName.Returns(Environments.Development);
            services.AddSingleton(environment);
            services.AddSingleton(assemblies);
            services.AddLogging();
            services.AddPlatformAuth(config);

            // No tenancy needed for this test - we're only testing auth middleware

            // Set up in-memory database for registry
            services.AddSingleton<RegistryDbContext>(sp =>
            {
                var options = new DbContextOptionsBuilder<RegistryDbContext>()
                    .UseInMemoryDatabase($"admin-tests-{Guid.NewGuid()}")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                    .Options;
                return new RegistryDbContext(options, assemblies);
            });

            services.AddScoped<MembershipQuery>();
            services.AddScoped<RegistryService>();
            services.AddScoped<AdminEndpoints>();
            services.AddScoped<RegistryEndpoints>();
            services.AddSingleton<IAuthenticationService, HeaderAuthenticationService>();

            _root = services.BuildServiceProvider(validateScopes: true);
        }

        public async Task<InvokeResult> Invoke(
            Type shim, string method, string? oid = null, string? tid = "home-tid", string? tenantHeader = null, string[]? tokenRoles = null)
        {
            await using var scope = _root.CreateAsyncScope();

            var http = new DefaultHttpContext();
            http.Response.Body = new MemoryStream();
            if (oid != null) http.Request.Headers[HeaderAuthenticationService.OidHeader] = oid;
            if (tid != null) http.Request.Headers[HeaderAuthenticationService.TidHeader] = tid;
            if (tokenRoles != null && tokenRoles.Length > 0)
                http.Request.Headers[HeaderAuthenticationService.RolesHeader] = string.Join(',', tokenRoles);
            if (tenantHeader != null) http.Request.Headers["X-Tenant-Id"] = tenantHeader;

            var definition = Substitute.For<FunctionDefinition>();
            definition.Name.Returns(method);
            definition.EntryPoint.Returns($"{shim.FullName}.{method}");

            var context = Substitute.For<FunctionContext>();
            context.FunctionDefinition.Returns(definition);
            context.InstanceServices.Returns(scope.ServiceProvider);
            context.Items.Returns(new Dictionary<object, object> { ["HttpRequestContext"] = http });

            var nextCalled = false;
            await _middleware.Invoke(context, ctx =>
            {
                nextCalled = true;
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

            return new InvokeResult(http.Response.StatusCode, error, nextCalled);
        }

        public void Dispose() => _root.Dispose();
    }

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

    private sealed record InvokeResult(int Status, string? Error, bool NextCalled);
}
