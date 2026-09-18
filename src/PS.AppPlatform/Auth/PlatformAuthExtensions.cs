using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;

namespace PS.AppPlatform.Auth;

public static class PlatformAuthExtensions
{
    public static IServiceCollection AddPlatformAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new PlatformAuthenticationOptions();
        configuration.GetSection(PlatformAuthenticationOptions.SectionName).Bind(options);
        services.Configure<PlatformAuthenticationOptions>(
            configuration.GetSection(PlatformAuthenticationOptions.SectionName));

        var enabled = options.Enabled ?? (options.AzureEntraId is not null);

        if (!enabled)
        {
            // Log at startup via IHostApplicationLifetime notification
            var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("PS.AppPlatform.Auth");
            logger.LogWarning(
                "Platform authentication is DISABLED. Every endpoint is publicly reachable. " +
                "Set authentication:azureEntraId to enable.");
            return services;
        }

        // Validate required configuration
        var entraId = options.AzureEntraId;
        if (string.IsNullOrEmpty(entraId?.TenantId))
        {
            throw new InvalidOperationException(
                "authentication:azureEntraId:tenantId is required when authentication is enabled. " +
                "See docs/auth-setup.md");
        }
        if (string.IsNullOrEmpty(entraId?.ClientId))
        {
            throw new InvalidOperationException(
                "authentication:azureEntraId:clientId is required when authentication is enabled. " +
                "See docs/auth-setup.md");
        }
        if (entraId.TenantId == entraId.ClientId)
        {
            throw new InvalidOperationException(
                $"authentication:azureEntraId:tenantId and authentication:azureEntraId:clientId must be different. " +
                $"tenantId={entraId.TenantId}, clientId={entraId.ClientId}. " +
                $"See docs/auth-setup.md");
        }

        // Configure JWT bearer authentication
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(
                jwtBearerOptions =>
                {
                    jwtBearerOptions.TokenValidationParameters.ValidateIssuer = true;
                    jwtBearerOptions.TokenValidationParameters.ValidateAudience = true;
                    jwtBearerOptions.TokenValidationParameters.ValidateLifetime = true;

                    // Add additional audiences (e.g., api://{clientId})
                    if (entraId.AdditionalAudiences.Length > 0)
                    {
                        var audiences = new List<string> { entraId.ClientId }
                            .Concat(entraId.AdditionalAudiences)
                            .ToList();
                        jwtBearerOptions.TokenValidationParameters.ValidAudiences = audiences;
                    }
                },
                microsoftIdentityOptions =>
                {
                    microsoftIdentityOptions.Instance = "https://login.microsoftonline.com/";
                    microsoftIdentityOptions.TenantId = entraId.TenantId;
                    microsoftIdentityOptions.ClientId = entraId.ClientId;
                });

        // Register authorization with platform default policy
        services.AddAuthorization(authOptions =>
        {
            var policy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser();

            if (!string.IsNullOrEmpty(options.RequiredRole))
            {
                policy.RequireAssertion(context =>
                {
                    var roles = context.User.FindAll("roles");
                    return roles.Any(c => c.Value.Equals(options.RequiredRole, StringComparison.OrdinalIgnoreCase));
                });
            }

            authOptions.FallbackPolicy = policy.Build();
            authOptions.DefaultPolicy = policy.Build();
        });

        return services;
    }
}

public static class PlatformPolicies
{
    public const string Default = "";
}

