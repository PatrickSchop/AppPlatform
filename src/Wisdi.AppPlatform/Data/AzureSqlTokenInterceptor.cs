using Azure.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using PS.AppPlatform.Hosting;
using System.Data.Common;

namespace PS.AppPlatform.Data;

public class AzureSqlTokenInterceptor : DbConnectionInterceptor
{
    private readonly IAzureIdentityProvider _identityProvider;
    private readonly ILogger<AzureSqlTokenInterceptor> _logger;

    public AzureSqlTokenInterceptor(IAzureIdentityProvider identityProvider, ILogger<AzureSqlTokenInterceptor> logger)
    {
        _identityProvider = identityProvider;
        _logger = logger;
    }

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (connection is SqlConnection sqlConnection)
        {
            var tokenRequestContext = new TokenRequestContext(new[] { "https://database.windows.net/.default" });
            _logger.LogInformation($"Using {_identityProvider.Credential.GetType().Name} to authenticate to Azure SQL Database.");
            var token = await _identityProvider.Credential.GetTokenAsync(tokenRequestContext, cancellationToken);
            sqlConnection.AccessToken = token.Token;
        }

        return result;
    }

    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        if (connection is SqlConnection sqlConnection)
        {
            var tokenRequestContext = new TokenRequestContext(new[] { "https://database.windows.net/.default" });
            _logger.LogInformation($"Using {_identityProvider.Credential.GetType().Name} to authenticate to Azure SQL Database.");
            var token = _identityProvider.Credential.GetToken(tokenRequestContext, CancellationToken.None);
            sqlConnection.AccessToken = token.Token;
        }

        return result;
    }
}

