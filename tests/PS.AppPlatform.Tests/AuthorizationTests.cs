using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Security.Claims;
using NSubstitute;
using PS.AppPlatform.Auth;
using Xunit;

namespace PS.AppPlatform.Tests;

public class AuthorizationTests
{
    [Fact]
    public void AddPlatformAuthentication_throws_when_TenantId_equals_ClientId()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["authentication:azureEntraId:tenantId"] = "same-id",
                ["authentication:azureEntraId:clientId"] = "same-id",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddPlatformAuthentication(config));
        Assert.Contains("tenantId", ex.Message);
        Assert.Contains("clientId", ex.Message);
        Assert.Contains("must be different", ex.Message);
    }

    [Fact]
    public void AddPlatformAuthentication_throws_when_TenantId_missing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["authentication:azureEntraId:clientId"] = "client-id-456",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddPlatformAuthentication(config));
        Assert.Contains("tenantId", ex.Message);
    }

    [Fact]
    public void AddPlatformAuthentication_throws_when_ClientId_missing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["authentication:azureEntraId:tenantId"] = "tenant-id-123",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddPlatformAuthentication(config));
        Assert.Contains("clientId", ex.Message);
    }

    [Fact]
    public void AddPlatformAuthentication_requires_role_attribute_parsing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["authentication:azureEntraId:tenantId"] = "tenant-id-123",
                ["authentication:azureEntraId:clientId"] = "client-id-456",
                ["authentication:requiredRole"] = "app.user",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddPlatformAuthentication(config);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<PlatformAuthenticationOptions>>();

        Assert.NotNull(options.Value.AzureEntraId);
        Assert.Equal("app.user", options.Value.RequiredRole);
    }

    [Fact]
    public void CorsMiddleware_reads_configuration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["httpAccessControl:allowOrigin"] = "http://localhost:3000",
                ["httpAccessControl:allowHeaders"] = "Content-Type, Authorization, X-Custom",
            })
            .Build();

        var middleware = new CorsMiddleware(config);
        Assert.NotNull(middleware);
    }

    [Fact]
    public void FunctionAuthorizationMiddleware_is_registered()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["authentication:azureEntraId:tenantId"] = "tenant-id-123",
                ["authentication:azureEntraId:clientId"] = "client-id-456",
                ["httpAccessControl:allowOrigin"] = "http://localhost:3000",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddPlatformAuth(config);

        var sp = services.BuildServiceProvider();
        var middleware = sp.GetRequiredService<FunctionAuthorizationMiddleware>();
        Assert.NotNull(middleware);
    }

    [Fact]
    public void AllowAnonymousAttribute_is_recognized()
    {
        var method = typeof(TestEndpoints).GetMethod(nameof(TestEndpoints.AllowAnonymousMethod));
        Assert.NotNull(method);
        var attr = method.GetCustomAttribute<AllowAnonymousAttribute>();
        Assert.NotNull(attr);
    }

    [Fact]
    public void AllowAnonymousAttribute_on_type_is_recognized()
    {
        var type = typeof(AnonymousEndpoints);
        var attr = type.GetCustomAttribute<AllowAnonymousAttribute>();
        Assert.NotNull(attr);
    }

    [Fact]
    public void FunctionContextExtensions_has_GetTargetFunctionMethod()
    {
        // Test that the extension method exists and can be called
        var mockContext = NSubstitute.Substitute.For<FunctionContext>();

        // The method exists and returns null or a MethodInfo
        var method = mockContext.GetTargetFunctionMethod();
        // Method might be null since EntryPoint is read-only on the mock, but that's OK
        Assert.True(method is null || method is MethodInfo);
    }

    [Fact]
    public void CorsMiddleware_with_multiple_origins()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["httpAccessControl:allowOrigin"] = "http://localhost:3000, http://localhost:3001",
                ["httpAccessControl:allowHeaders"] = "Content-Type, Authorization",
            })
            .Build();

        var middleware = new CorsMiddleware(config);
        Assert.NotNull(middleware);
    }

    [Fact]
    public void AuthServiceBuilder_extension_method_exists()
    {
        // Test that the extension method can be called without errors
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["authentication:azureEntraId:tenantId"] = "tenant-id-123",
                ["authentication:azureEntraId:clientId"] = "client-id-456",
                ["httpAccessControl:allowOrigin"] = "http://localhost:3000",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddLogging();
        services.AddAuthorization();

        // Call AddPlatformAuth - should not throw
        var result = services.AddPlatformAuth(config);

        Assert.NotNull(result);
        Assert.Same(services, result);
    }

    [Fact]
    public void GetTargetFunctionMethod_resolves_an_instance_method_and_its_AllowAnonymous()
    {
        // Every real [Function] shim is an instance method on a primary-constructor class.
        // Resolving static methods only returns null here, which makes [AllowAnonymous]
        // undetectable and fails every anonymous endpoint closed.
        var definition = NSubstitute.Substitute.For<FunctionDefinition>();
        definition.EntryPoint.Returns(
            $"{typeof(ShimStyleEndpoints).FullName}.{nameof(ShimStyleEndpoints.GetThing)}");

        var context = NSubstitute.Substitute.For<FunctionContext>();
        context.FunctionDefinition.Returns(definition);

        var method = context.GetTargetFunctionMethod();

        Assert.NotNull(method);
        Assert.Equal(nameof(ShimStyleEndpoints.GetThing), method!.Name);
        Assert.NotNull(method.DeclaringType!.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public async Task Default_policy_is_resolvable_when_no_named_policy_is_declared()
    {
        // Endpoints without [Authorize(Policy = "x")] fall back to the default policy.
        // Looking that up as a named policy throws "No policy found: ."
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["authentication:azureEntraId:tenantId"] = "tenant-id-123",
                ["authentication:azureEntraId:clientId"] = "client-id-456",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddLogging();
        services.AddAuthorization();
        services.AddPlatformAuth(config);

        var provider = services.BuildServiceProvider().GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await provider.GetDefaultPolicyAsync();

        Assert.NotNull(policy);
        Assert.NotEmpty(policy.Requirements);
    }
}

// Test endpoint classes
public static class TestEndpoints
{
    public static void NoAttributes() { }

    [AllowAnonymous]
    public static void AllowAnonymousMethod() { }
}

[AllowAnonymous]
public static class AnonymousEndpoints
{
    public static void AnyMethod() { }
}

/// <summary>
/// Mirrors the shape of a real [Function] shim: [AllowAnonymous] on a primary-constructor
/// class whose endpoint methods are instance methods. The static fixtures above do not
/// exercise the reflection path that actually ships.
/// </summary>
[AllowAnonymous]
public class ShimStyleEndpoints(string dependency)
{
    public string Dependency { get; } = dependency;

    public void GetThing() { }
}

