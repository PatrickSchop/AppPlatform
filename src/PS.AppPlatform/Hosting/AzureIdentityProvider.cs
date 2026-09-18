using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PS.AppPlatform.Hosting;

public class AzureIdentityProvider : IAzureIdentityProvider
{
    private readonly IHostingEnvironment _hostingEnvironment;
    private readonly Azure.Core.TokenCredential _credential;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AzureIdentityProvider> _logger;

    public AzureIdentityProvider(IConfiguration configuration, IHostingEnvironment hostingEnvironment, ILogger<AzureIdentityProvider> logger)
    {
        _hostingEnvironment = hostingEnvironment;
        _configuration = configuration.GetSection("azureIdentity");
        _logger = logger;
        _credential = CreateCredential();
    }

    public Azure.Core.TokenCredential Credential => _credential;

    private Azure.Core.TokenCredential CreateCredential()
    {
        var credentialType = _configuration.GetValue<string?>("type") ?? "";
        switch (credentialType)
        {
            case "systemAssigned":
                _logger.LogInformation("Using Managed Identity Credential: System Assigned");
                return new ManagedIdentityCredential();
            case "userAssigned":
                _logger.LogInformation("Using Managed Identity Credential: User Assigned");
                return new ManagedIdentityCredential(_configuration.GetValue<string>("clientId"));
            case "azureCli":
                _logger.LogInformation("Using Azure CLI Credential");
                return new AzureCliCredential();
            case "":
                if (_hostingEnvironment.EnvironmentType == EnvironmentType.Development)
                {
                    _logger.LogInformation("Using Default Azure Credential for Development Environment");
                    return new DefaultAzureCredential();
                }
                break;
        }
        throw new InvalidOperationException($"Unsupported Azure identity type: {credentialType}");
    }
}

