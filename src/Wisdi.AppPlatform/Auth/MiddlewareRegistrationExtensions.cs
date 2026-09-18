using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;

namespace PS.AppPlatform.Auth;

/// <summary>
/// Middleware pipeline activator that chains CORS and Authorization middleware.
/// This wraps the platform middleware in the correct order for execution.
/// </summary>
internal sealed class PlatformMiddlewareChain : IFunctionsWorkerMiddleware
{
    private readonly CorsMiddleware _corsMiddleware;
    private readonly FunctionAuthorizationMiddleware _authzMiddleware;

    public PlatformMiddlewareChain(CorsMiddleware corsMiddleware, FunctionAuthorizationMiddleware authzMiddleware)
    {
        _corsMiddleware = corsMiddleware;
        _authzMiddleware = authzMiddleware;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        // Chain: CORS â†’ Authorization â†’ next
        await _corsMiddleware.Invoke(context, async ctx =>
        {
            await _authzMiddleware.Invoke(ctx, next);
        });
    }
}


