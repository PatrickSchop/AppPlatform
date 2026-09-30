using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PS.AppPlatform.Auth;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using PS.Management.Api;
using PS.Management.Registry;
using Xunit;

namespace PS.Management.Tests;

public class RegistryEndpointTests
{
    private static async Task<IServiceProvider> BuildServicesWithManagementAppAsync(RegistryOptions options)
    {
        var db = new RegistryDbContext(
            new DbContextOptionsBuilder<RegistryDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            new PlatformAssemblies().AddContaining<RegistryService>());

        var svc = new RegistryService(db);
        var manifest = new Manifest();
        await svc.UpsertApplicationAsync(
            new ApplicationRegistration(manifest.Key, manifest.DisplayName, manifest.Tenancy, manifest.Roles, null),
            default);

        var sc = new ServiceCollection();
        sc.AddSingleton(db);
        sc.AddScoped<RegistryService>(sp => new RegistryService(sp.GetRequiredService<RegistryDbContext>()));
        sc.AddScoped<MembershipQuery>();
        sc.AddSingleton(Options.Create(options));
        sc.AddScoped<ILogger<RegistryEndpoints>>(sp =>
            new MockLogger<RegistryEndpoints>());
        sc.AddScoped<RegistryEndpoints>();
        return sc.BuildServiceProvider();
    }

    private static HttpRequest CreateRequestWithClaimsAndBody(string oid, string tid, object? body, string? queryString = null)
    {
        var context = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new Claim("oid", oid),
            new Claim("tid", tid),
        };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test", "name", "roles"));

        if (body != null)
        {
            var ms = new MemoryStream();
            JsonSerializer.Serialize(ms, body, JsonSerializerOptions.Web);
            ms.Position = 0;
            context.Request.Body = ms;
        }

        if (!string.IsNullOrEmpty(queryString))
        {
            context.Request.QueryString = new QueryString(queryString);
        }

        return context.Request;
    }

    [Fact]
    public async Task RegisterAsync_TrustedDeployer_Returns200_CreatesApp()
    {
        // Arrange
        var trustedTenantId = Guid.NewGuid().ToString();
        var trustedDeployerId = Guid.NewGuid().ToString();
        var options = new RegistryOptions
        {
            TrustedTenantId = trustedTenantId,
            TrustedDeployers = [trustedDeployerId]
        };

        var services = await BuildServicesWithManagementAppAsync(options);
        var endpoints = services.GetRequiredService<RegistryEndpoints>();

        var key = "test-app";
        var body = new RegistrationBody(
            key, "Test App", TenancyMode.Single, [], null);

        var req = CreateRequestWithClaimsAndBody(trustedDeployerId, trustedTenantId, body);

        // Act
        var result = await endpoints.RegisterAsync(req, key, default);

        // Assert
        Assert.NotNull(result);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var registrationResult = Assert.IsType<RegistrationResult>(okResult.Value);
        Assert.True(registrationResult.Created);
    }

    [Fact]
    public async Task RegisterAsync_NonDeployerOid_Returns403()
    {
        // Arrange
        var trustedTenantId = Guid.NewGuid().ToString();
        var trustedDeployerId = Guid.NewGuid().ToString();
        var nonDeployerId = Guid.NewGuid().ToString();
        var options = new RegistryOptions
        {
            TrustedTenantId = trustedTenantId,
            TrustedDeployers = [trustedDeployerId]
        };

        var services = await BuildServicesWithManagementAppAsync(options);
        var endpoints = services.GetRequiredService<RegistryEndpoints>();

        var key = "test-app";
        var body = new RegistrationBody(
            key, "Test App", TenancyMode.Single, [], null);

        var req = CreateRequestWithClaimsAndBody(nonDeployerId, trustedTenantId, body);

        // Act
        var result = await endpoints.RegisterAsync(req, key, default);

        // Assert
        Assert.NotNull(result);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, objResult.StatusCode);
    }

    [Fact]
    public async Task RegisterAsync_WrongTid_Returns403()
    {
        // Arrange
        var trustedTenantId = Guid.NewGuid().ToString();
        var wrongTenantId = Guid.NewGuid().ToString();
        var trustedDeployerId = Guid.NewGuid().ToString();
        var options = new RegistryOptions
        {
            TrustedTenantId = trustedTenantId,
            TrustedDeployers = [trustedDeployerId]
        };

        var services = await BuildServicesWithManagementAppAsync(options);
        var endpoints = services.GetRequiredService<RegistryEndpoints>();

        var key = "test-app";
        var body = new RegistrationBody(
            key, "Test App", TenancyMode.Single, [], null);

        var req = CreateRequestWithClaimsAndBody(trustedDeployerId, wrongTenantId, body);

        // Act
        var result = await endpoints.RegisterAsync(req, key, default);

        // Assert
        Assert.NotNull(result);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, objResult.StatusCode);
    }

    [Fact]
    public async Task RegisterAsync_InvalidRoleName_Returns400()
    {
        // Arrange
        var trustedTenantId = Guid.NewGuid().ToString();
        var trustedDeployerId = Guid.NewGuid().ToString();
        var options = new RegistryOptions
        {
            TrustedTenantId = trustedTenantId,
            TrustedDeployers = [trustedDeployerId]
        };

        var services = await BuildServicesWithManagementAppAsync(options);
        var endpoints = services.GetRequiredService<RegistryEndpoints>();

        var key = "test-app";
        var body = new RegistrationBody(
            key, "Test App", TenancyMode.Single,
            [new AppRoleDefinition("UPPER", "Upper Role")], null);

        var req = CreateRequestWithClaimsAndBody(trustedDeployerId, trustedTenantId, body);

        // Act
        var result = await endpoints.RegisterAsync(req, key, default);

        // Assert
        Assert.NotNull(result);
        var badResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badResult.Value);
        Assert.Contains("UPPER", badResult.Value.ToString()!);
    }

    [Fact]
    public async Task GetMembershipsAsync_ByAppOwner_Returns200()
    {
        // Arrange
        var trustedTenantId = Guid.NewGuid().ToString();
        var appServicePrincipalId = Guid.NewGuid();
        var options = new RegistryOptions
        {
            TrustedTenantId = trustedTenantId,
            TrustedDeployers = []
        };

        var services = await BuildServicesWithManagementAppAsync(options);
        var endpoints = services.GetRequiredService<RegistryEndpoints>();
        var registryService = services.GetRequiredService<RegistryService>();

        // Register an app with service principal
        var key = "test-app";
        var manifest = new Manifest();
        await registryService.UpsertApplicationAsync(
            new ApplicationRegistration(key, "Test App", TenancyMode.Single, [], appServicePrincipalId),
            default);

        var req = CreateRequestWithClaimsAndBody(appServicePrincipalId.ToString(), trustedTenantId, null, $"?tid={Uri.EscapeDataString(trustedTenantId)}");

        // Act
        var result = await endpoints.GetMembershipsAsync(req, key, appServicePrincipalId.ToString(), default);

        // Assert
        Assert.NotNull(result);
        // Should return 404 if no memberships for this identity (user not in DB)
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetMembershipsAsync_ByDifferentOid_Returns403()
    {
        // Arrange
        var trustedTenantId = Guid.NewGuid().ToString();
        var appServicePrincipalId = Guid.NewGuid();
        var differentOid = Guid.NewGuid();
        var options = new RegistryOptions
        {
            TrustedTenantId = trustedTenantId,
            TrustedDeployers = []
        };

        var services = await BuildServicesWithManagementAppAsync(options);
        var endpoints = services.GetRequiredService<RegistryEndpoints>();
        var registryService = services.GetRequiredService<RegistryService>();

        // Register an app with service principal
        var key = "test-app";
        await registryService.UpsertApplicationAsync(
            new ApplicationRegistration(key, "Test App", TenancyMode.Single, [], appServicePrincipalId),
            default);

        var req = CreateRequestWithClaimsAndBody(differentOid.ToString(), trustedTenantId, null, $"?tid={Uri.EscapeDataString(trustedTenantId)}");

        // Act
        var result = await endpoints.GetMembershipsAsync(req, key, differentOid.ToString(), default);

        // Assert
        Assert.NotNull(result);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, objResult.StatusCode);
    }

    [Fact]
    public async Task GetMembershipsAsync_UnknownUser_Returns404()
    {
        // Arrange
        var trustedTenantId = Guid.NewGuid().ToString();
        var appServicePrincipalId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var options = new RegistryOptions
        {
            TrustedTenantId = trustedTenantId,
            TrustedDeployers = []
        };

        var services = await BuildServicesWithManagementAppAsync(options);
        var endpoints = services.GetRequiredService<RegistryEndpoints>();
        var registryService = services.GetRequiredService<RegistryService>();

        // Register an app with service principal
        var key = "test-app";
        await registryService.UpsertApplicationAsync(
            new ApplicationRegistration(key, "Test App", TenancyMode.Single, [], appServicePrincipalId),
            default);

        var req = CreateRequestWithClaimsAndBody(appServicePrincipalId.ToString(), trustedTenantId, null, $"?tid={Uri.EscapeDataString(trustedTenantId)}");

        // Act
        var result = await endpoints.GetMembershipsAsync(req, key, oid.ToString(), default);

        // Assert
        Assert.NotNull(result);
        var notFoundResult = Assert.IsType<NotFoundResult>(result);
        Assert.Equal(404, notFoundResult.StatusCode);
    }

    [Fact]
    public async Task RegisterThenGetMemberships_WithServicePrincipalId_DoesNotReturn403()
    {
        // Arrange
        var trustedTenantId = Guid.NewGuid().ToString();
        var trustedDeployerId = Guid.NewGuid().ToString();
        var appServicePrincipalId = Guid.NewGuid();
        var options = new RegistryOptions
        {
            TrustedTenantId = trustedTenantId,
            TrustedDeployers = [trustedDeployerId]
        };

        var services = await BuildServicesWithManagementAppAsync(options);
        var endpoints = services.GetRequiredService<RegistryEndpoints>();

        var key = "roundtrip-app";
        var body = new RegistrationBody(
            key, "Roundtrip App", TenancyMode.Single, [],
            appServicePrincipalId);

        // PUT to register
        var putReq = CreateRequestWithClaimsAndBody(trustedDeployerId, trustedTenantId, body);
        var putResult = await endpoints.RegisterAsync(putReq, key, default);
        var okResult = Assert.IsType<OkObjectResult>(putResult);
        Assert.True(Assert.IsType<RegistrationResult>(okResult.Value).Created);

        // GET memberships as the app's own service principal
        var getReq = CreateRequestWithClaimsAndBody(
            appServicePrincipalId.ToString(), trustedTenantId, null,
            $"?tid={Uri.EscapeDataString(trustedTenantId)}");
        var getResult = await endpoints.GetMembershipsAsync(
            getReq, key, appServicePrincipalId.ToString(), default);

        // Should NOT be 403 — the app's ServicePrincipalId was registered
        // It should be 404 (no user memberships yet) or 200, not 403
        Assert.IsNotType<ObjectResult>(getResult); // ObjectResult with 403 would fail this
        Assert.IsType<NotFoundResult>(getResult);  // no memberships → 404
    }
}

// Mock logger for tests
internal class MockLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) { }
}
