using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PS.AppPlatform.Tenancy;
using System.Reflection;
using System.Text.Json;

namespace PS.AppPlatform.Auth;

public sealed class FunctionAuthorizationMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        // Get HTTP context - non-HTTP triggers like timer are allowed through
        var httpContext = context.GetHttpContext();
        if (httpContext == null)
        {
            await next(context);
            return;
        }

        // Resolve services from per-invocation scope, not root provider
        var options = context.InstanceServices.GetService(typeof(IOptions<PlatformAuthenticationOptions>)) as IOptions<PlatformAuthenticationOptions>;
        var authzService = context.InstanceServices.GetService(typeof(IAuthorizationService)) as IAuthorizationService;
        var logger = context.InstanceServices.GetService(typeof(ILogger<FunctionAuthorizationMiddleware>)) as ILogger<FunctionAuthorizationMiddleware>;

        if (options?.Value.Enabled == false || authzService == null)
        {
            if (logger != null && options?.Value.Enabled == false)
            {
                logger.LogWarning("Authorization skipped: authentication is disabled");
            }
            await next(context);
            return;
        }

        var method = context.GetTargetFunctionMethod();
        var functionName = context.FunctionDefinition.Name;

        // Check opt-out attributes
        var isAnonymous = IsAnonymousFunction(method, functionName, options!.Value);
        if (isAnonymous)
        {
            await next(context);
            return;
        }

        // Authenticate the request
        var authService = context.InstanceServices.GetService(typeof(IAuthenticationService)) as IAuthenticationService;
        var authResult = authService != null
            ? await authService.AuthenticateAsync(httpContext, JwtBearerDefaults.AuthenticationScheme)
            : null;
        httpContext.User = authResult?.Principal ?? new();

        var tenancyMode = context.InstanceServices.GetService(typeof(TenancyMode)) as TenancyMode? ?? TenancyMode.None;
        if (tenancyMode != TenancyMode.None)
        {
            var resolver = (TenantResolver)context.InstanceServices.GetRequiredService(typeof(TenantResolver));
            var resolution = await resolver.ResolveAsync(httpContext, IsTenantOptional(method), context.CancellationToken);
            if (resolution is TenantResolution.Stop stop)
            {
                httpContext.Response.StatusCode = stop.StatusCode;
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new { error = stop.Error }));
                logger?.LogInformation(
                    "Tenant resolution stopped {FunctionName} for user {Oid}: {Error}",
                    functionName, httpContext.User.GetIdentityKey()?.ObjectId ?? "unknown", stop.Error);
                return;
            }
        }

        var policyProvider = (IAuthorizationPolicyProvider)context.InstanceServices.GetRequiredService(typeof(IAuthorizationPolicyProvider));
        var policy = await GetAuthorizationPolicyAsync(method, policyProvider);

        // Authorize the request
        var authzResult = await authzService.AuthorizeAsync(httpContext.User, resource: null, policy);

        if (!authzResult.Succeeded)
        {
            var isAuthenticated = httpContext.User?.Identity?.IsAuthenticated ?? false;
            if (!isAuthenticated)
            {
                // 401 Unauthorized
                httpContext.Response.StatusCode = 401;
                httpContext.Response.Headers.WWWAuthenticate = "Bearer";
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new { error = "unauthorized" }));
                logger?.LogInformation("Unauthorized access to {FunctionName}", functionName);
            }
            else
            {
                // 403 Forbidden
                var oid = httpContext.User?.FindFirst("oid")?.Value ?? "unknown";
                httpContext.Response.StatusCode = 403;
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new { error = "forbidden" }));
                logger?.LogInformation("Forbidden access to {FunctionName} by user {Oid}", functionName, oid);
            }
            return;
        }

        await next(context);
    }

    private static bool IsAnonymousFunction(MethodInfo? method, string functionName, PlatformAuthenticationOptions options)
    {
        if (method == null)
            return false;

        // Check [AllowAnonymous] on method
        if (method.GetCustomAttribute<AllowAnonymousAttribute>() != null)
            return true;

        // Check [AllowAnonymous] on declaring type
        if (method.DeclaringType?.GetCustomAttribute<AllowAnonymousAttribute>() != null)
            return true;

        // Check AnonymousFunctions configuration
        if (options.AnonymousFunctions.Contains(functionName, StringComparer.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool IsTenantOptional(MethodInfo? method) =>
        method != null &&
        (method.GetCustomAttribute<TenantOptionalAttribute>() != null ||
         method.DeclaringType?.GetCustomAttribute<TenantOptionalAttribute>() != null);

    /// <summary>
    /// Combines [Authorize] on method and class as ASP.NET Core does. A named Policy replaces the
    /// default policy; Roles alone are added on top of it, so they never bypass requiredRole.
    /// </summary>
    private static async Task<AuthorizationPolicy> GetAuthorizationPolicyAsync(
        MethodInfo? method, IAuthorizationPolicyProvider policyProvider)
    {
        var defaultPolicy = await policyProvider.GetDefaultPolicyAsync();
        if (method == null)
            return defaultPolicy;

        var authorizeData = method.GetCustomAttributes<AuthorizeAttribute>()
            .Concat(method.DeclaringType?.GetCustomAttributes<AuthorizeAttribute>() ?? [])
            .ToList();

        var combined = await AuthorizationPolicy.CombineAsync(policyProvider, authorizeData);
        if (combined == null)
            return defaultPolicy;

        return authorizeData.Any(a => !string.IsNullOrEmpty(a.Policy))
            ? combined
            : AuthorizationPolicy.Combine(defaultPolicy, combined);
    }
}

