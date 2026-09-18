using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Text.Json;

namespace Wisdi.AppPlatform.Auth;

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

        // Determine the policy to evaluate
        var policyName = GetAuthorizationPolicy(method) ?? PlatformPolicies.Default;

        // Authorize the request
        var authzResult = await authzService.AuthorizeAsync(httpContext.User, resource: null, policyName);

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

    private static string? GetAuthorizationPolicy(MethodInfo? method)
    {
        if (method == null)
            return null;

        // Check [Authorize(Policy = "X")] on method
        var methodAuth = method.GetCustomAttribute<AuthorizeAttribute>();
        if (methodAuth?.Policy != null)
            return methodAuth.Policy;

        // Check [Authorize(Policy = "X")] on declaring type
        var typeAuth = method.DeclaringType?.GetCustomAttribute<AuthorizeAttribute>();
        if (typeAuth?.Policy != null)
            return typeAuth.Policy;

        // No explicit policy - use default (which means default-deny)
        return null;
    }
}
