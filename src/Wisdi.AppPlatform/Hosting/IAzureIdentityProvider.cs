namespace Wisdi.AppPlatform.Hosting;

public interface IAzureIdentityProvider
{
    Azure.Core.TokenCredential Credential { get; }
}
