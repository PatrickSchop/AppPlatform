using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.Core;
using Microsoft.Extensions.Options;
using PS.AppPlatform.Hosting;

namespace PS.AppPlatform.Tenancy;

public sealed class RegisterCommand : IPlatformCommand
{
    public string Name => "register";

    public async Task<int> RunAsync(PlatformCommandArgs args, IServiceProvider services, CancellationToken ct)
    {
        var principalId = args.Get("principal-id");
        if (string.IsNullOrWhiteSpace(principalId))
        {
            Console.Error.WriteLine("Error: --principal-id is required.");
            return 2;
        }

        if (!Guid.TryParse(principalId, out var pid))
        {
            Console.Error.WriteLine($"Error: --principal-id '{principalId}' is not a valid GUID.");
            return 2;
        }

        var manifest = (AppManifest?)services.GetService(typeof(AppManifest));
        if (manifest == null)
        {
            Console.Error.WriteLine("Error: No AppManifest found. This command requires an app manifest.");
            return 2;
        }

        var optionsProvider = (IOptions<ManagementDirectoryOptions>?)services.GetService(typeof(IOptions<ManagementDirectoryOptions>));
        if (optionsProvider == null)
        {
            Console.Error.WriteLine("Error: ManagementDirectoryOptions not configured.");
            return 2;
        }

        var options = optionsProvider.Value;
        if (string.IsNullOrWhiteSpace(options.Url) || string.IsNullOrWhiteSpace(options.Audience))
        {
            Console.Error.WriteLine("Error: tenancy:management:url and tenancy:management:audience are required.");
            return 2;
        }

        // Get token using AzureCliCredential (or injected credential for testing)
        TokenCredential credential = (TokenCredential?)services.GetService(typeof(TokenCredential)) ?? new AzureCliCredential();
        AccessToken token;
        try
        {
            token = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { $"{options.Audience}/.default" }),
                ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Failed to get authentication token. {ex.Message}");
            return 1;
        }

        // Build registration body
        var registrationBody = new
        {
            displayName = manifest.DisplayName,
            tenancy = manifest.Tenancy.ToString(),
            roles = manifest.Roles,
            servicePrincipalId = pid.ToString()
        };

        var jsonBody = JsonSerializer.Serialize(registrationBody, JsonSerializerOptions.Web);
        var url = $"{options.Url}/api/registry/v1/applications/{manifest.Key}";

        // Send registration request
        var testHandler = (HttpMessageHandler?)services.GetService(typeof(HttpMessageHandler));
        using var client = testHandler != null
            ? new HttpClient(testHandler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) }
            : new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

        try
        {
            var response = await client.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                Console.WriteLine("Registration successful.");
                return 0;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // Extract caller OID from token JWT (middle segment)
                var callerOid = ExtractOidFromToken(token.Token);
                Console.Error.WriteLine(
                    $"Error: Access denied (403). Your caller identity ({callerOid}) is not in the registry:trustedDeployers role.");
                return 1;
            }

            var responseText = await response.Content.ReadAsStringAsync(ct);
            Console.Error.WriteLine($"Error: Registration failed with status {response.StatusCode}. {responseText}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Registration request failed. {ex.Message}");
            return 1;
        }
    }

    private static string ExtractOidFromToken(string jwtToken)
    {
        try
        {
            var parts = jwtToken.Split('.');
            if (parts.Length != 3)
                return "<unknown>";

            var payload = parts[1];
            // Add padding if needed
            var padded = payload + new string('=', (4 - payload.Length % 4) % 4);
            var decoded = Convert.FromBase64String(padded);
            var json = Encoding.UTF8.GetString(decoded);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("oid", out var oid))
                return oid.GetString() ?? "<unknown>";
            return "<unknown>";
        }
        catch
        {
            return "<unknown>";
        }
    }
}
