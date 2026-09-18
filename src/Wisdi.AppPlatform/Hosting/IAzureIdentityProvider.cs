namespace PS.AppPlatform.Hosting;

public interface IAzureIdentityProvider
{
    Azure.Core.TokenCredential Credential { get; }
}

