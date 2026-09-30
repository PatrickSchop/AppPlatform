using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Options;
using PS.AppPlatform.Hosting;

namespace PS.AppPlatform.Tenancy;

public sealed class ManagementDirectoryOptions
{
    public const string SectionName = "tenancy:management";
    public string Url { get; set; } = "";
    public string Audience { get; set; } = "";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);
}

public sealed class ManagementApiTenantDirectory : ITenantDirectory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppManifest _manifest;
    private readonly ManagementDirectoryOptions _options;
    private readonly TokenCredential _credential;
    private readonly string _tokenScope;
    private AccessToken _cachedToken;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public ManagementApiTenantDirectory(
        IHttpClientFactory httpClientFactory,
        AppManifest manifest,
        IOptions<ManagementDirectoryOptions> options,
        IAzureIdentityProvider azureIdentityProvider)
    {
        _httpClientFactory = httpClientFactory;
        _manifest = manifest;
        _options = options.Value;
        _credential = azureIdentityProvider.Credential;
        _tokenScope = $"{_options.Audience}/.default";
    }

    public async Task<UserMemberships?> GetMembershipsAsync(IdentityKey identity, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        var url = $"{_options.Url}/api/registry/v1/applications/{_manifest.Key}/users/{identity.ObjectId}/memberships?tid={identity.IssuerTenantId}";

        var client = _httpClientFactory.CreateClient("ps-registry");
        client.Timeout = _options.Timeout;

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        try
        {
            var response = await client.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new TenantDirectoryUnavailableException(
                    $"Management directory API returned {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<UserMemberships>(
                json,
                JsonSerializerOptions.Web);
            return result;
        }
        catch (TaskCanceledException ex)
        {
            throw new TenantDirectoryUnavailableException("Directory request timeout", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new TenantDirectoryUnavailableException("Directory request failed", ex);
        }
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            // Check if cached token is still valid (refresh 5 min before expiry)
            if (_cachedToken.Token != null &&
                DateTimeOffset.UtcNow < _cachedToken.ExpiresOn.AddMinutes(-5))
            {
                return _cachedToken.Token;
            }

            // Get new token
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { _tokenScope }),
                ct);

            _cachedToken = token;
            return token.Token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
