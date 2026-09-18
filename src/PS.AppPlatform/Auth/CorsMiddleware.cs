using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;

namespace PS.AppPlatform.Auth;

public sealed class CorsMiddleware : IFunctionsWorkerMiddleware
{
    private readonly CorsConfig _config;

    public CorsMiddleware(IConfiguration configuration)
    {
        _config = new CorsConfig
        {
            AllowOrigin = configuration.GetSection("httpAccessControl").GetValue<string>("allowOrigin") ?? "",
            AllowHeaders = configuration.GetSection("httpAccessControl").GetValue<string>("allowHeaders") ?? "Content-Type, Authorization",
            EnableCors = !string.IsNullOrEmpty(configuration.GetSection("httpAccessControl").GetValue<string>("allowOrigin"))
        };
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        if (!_config.EnableCors)
        {
            await next(context);
            return;
        }

        try
        {
            var httpContext = context.GetHttpContext();
            if (httpContext == null)
            {
                await next(context);
                return;
            }

            var request = httpContext.Request;
            var response = httpContext.Response;

            // Determine the response origin
            var responseOrigin = GetResponseOrigin(request.Headers["Origin"].ToString(), _config.AllowOrigin);

            // Handle preflight OPTIONS requests
            if (request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                response.Headers.AccessControlAllowOrigin = responseOrigin;
                response.Headers.AccessControlAllowMethods = "GET, POST, PUT, DELETE, OPTIONS";
                response.Headers.AccessControlAllowHeaders = _config.AllowHeaders;
                response.Headers.Vary = "Origin";
                response.StatusCode = 204;
                return;
            }

            await next(context);

            // Set CORS headers on response
            if (!string.IsNullOrEmpty(responseOrigin))
            {
                response.Headers.AccessControlAllowOrigin = responseOrigin;
                response.Headers.Vary = "Origin";
            }
        }
        catch (ObjectDisposedException)
        {
            // Ignore disposed context. This can happen when the caller cancelled the request.
        }
    }

    private static string GetResponseOrigin(string requestOrigin, string allowOrigin)
    {
        if (string.IsNullOrEmpty(allowOrigin) || string.IsNullOrEmpty(requestOrigin))
            return allowOrigin;

        var allowedOrigins = allowOrigin.Split(',').Select(o => o.Trim()).ToArray();

        if (allowedOrigins.Length == 1)
            return allowedOrigins[0];

        return allowedOrigins.FirstOrDefault(o => o.Equals(requestOrigin, StringComparison.OrdinalIgnoreCase)) ?? allowedOrigins[0];
    }

    private sealed class CorsConfig
    {
        public bool EnableCors { get; set; }
        public string AllowOrigin { get; set; } = "";
        public string AllowHeaders { get; set; } = "";
    }
}

