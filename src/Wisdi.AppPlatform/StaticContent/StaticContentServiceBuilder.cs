using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wisdi.AppPlatform.Hosting;

namespace Wisdi.AppPlatform.StaticContent;

public sealed class StaticContentServiceBuilder : ServiceBuilder
{
    public override void BuildServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<LocalFilesProvider>();
        services.AddTransient<BlobProvider>();

        // Create the IFilesProvider implementation in a resolver function to postpone its creation until after function app startup.
        // Proper error handling during application startup is challenging.
        services.AddSingleton<IFilesProvider>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<StaticContentHandler>>();
            var config = serviceProvider.GetRequiredService<IConfiguration>();

            var staticContentConfig = config.GetSection("staticContent");
            if (!staticContentConfig.Exists())
            {
                throw new InvalidOperationException("Missing 'staticContent' configuration section.");
            }

            if (staticContentConfig.GetSection("files").Exists())
            {
                return serviceProvider.GetRequiredService<LocalFilesProvider>();
            }

            if (staticContentConfig.GetSection("blob").Exists())
            {
                return serviceProvider.GetRequiredService<BlobProvider>();
            }

            throw new InvalidOperationException("No valid static content provider configuration found. Expected either 'files' or 'blob' section.");
        });

        services.AddScoped<StaticContentHandler>();
    }
}
