using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wisdi.AppPlatform.Hosting;

namespace Wisdi.AppPlatform.Llm;

public static class LlmServiceBuilder
{
    /// <summary>
    /// Registers LLM services when the 'azureOpenAI' configuration section is present.
    /// If the section is absent, no services are registered (LLM is optional).
    /// </summary>
    public static void AddLlmServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // LLM is optional - only register if configuration section exists
        var azureOpenAIConfig = configuration.GetSection("azureOpenAI");
        if (!azureOpenAIConfig.Exists())
        {
            return;
        }

        // Register the Azure OpenAI client
        services.AddTransient(serviceProvider =>
        {
            var endpoint = azureOpenAIConfig.GetValue<string>("endpoint");
            if (string.IsNullOrEmpty(endpoint))
            {
                throw new InvalidOperationException("Missing 'endpoint' in 'azureOpenAI' configuration section.");
            }

            var authenticationConfig = azureOpenAIConfig.GetSection("authentication");
            var authenticationType = authenticationConfig.GetValue<string>("type");

            if (string.Equals(authenticationType, "apiKey", StringComparison.OrdinalIgnoreCase))
            {
                var logger = serviceProvider.GetRequiredService<ILogger<AzureOpenAIClient>>();
                logger.LogWarning(
                    "azureOpenAI is using apiKey authentication. Prefer managed identity; " +
                    "if a key is required, store it in Function App settings or a Key Vault reference, never in appsettings.json.");

                var apiKey = authenticationConfig.GetValue<string>("apiKey");
                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException(
                        "Missing 'apiKey' in 'azureOpenAI' configuration section for 'apiKey' authentication type.");
                }
                var credential = new AzureKeyCredential(apiKey);
                return new AzureOpenAIClient(new Uri(endpoint), credential);
            }
            else
            {
                var azureIdentityProvider = serviceProvider.GetRequiredService<IAzureIdentityProvider>();
                var client = new AzureOpenAIClient(new Uri(endpoint), azureIdentityProvider.Credential);
                return client;
            }
        });

        // Register the LLM parse client
        services.AddTransient<ILlmTextParseClient, LlmTextParseClient>();
    }
}
