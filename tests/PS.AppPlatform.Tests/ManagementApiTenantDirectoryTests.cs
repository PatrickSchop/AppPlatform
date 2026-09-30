using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using Xunit;

namespace PS.AppPlatform.Tests;

public class ManagementApiTenantDirectoryTests
{
    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            var token = "eyJhbGciOiJIUzI1NiJ9.eyJvaWQiOiIxMjM0NTY3OS1hYmNkLWVmZ2gtaWprbCJ9.test";
            return new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1));
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? Handler { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Handler != null)
                return await Handler(request);

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        }
    }

    private sealed class FakeAzureIdentityProvider : IAzureIdentityProvider
    {
        public TokenCredential Credential => new FakeTokenCredential();
    }

    private ManagementApiTenantDirectory CreateDirectory(
        FakeHttpMessageHandler handler,
        string url = "https://registry.example.com",
        string audience = "api://registry")
    {
        var options = Options.Create(new ManagementDirectoryOptions
        {
            Url = url,
            Audience = audience,
            Timeout = TimeSpan.FromSeconds(5)
        });

        var manifest = new TestManifest();
        var credentialProvider = new FakeAzureIdentityProvider();

        // Create HttpClientFactory that returns a client with our fake handler
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddHttpClient("ps-registry")
            .ConfigureHttpClient(c => c.BaseAddress = new Uri(url))
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        return new ManagementApiTenantDirectory(factory, manifest, options, credentialProvider);
    }

    [Fact]
    public async Task GetMembershipsAsync_Returns200_DeserializesUserMemberships()
    {
        var handler = new FakeHttpMessageHandler();
        var identity = new IdentityKey("user-oid", "tenant-id");

        var expected = new UserMemberships(
            Guid.NewGuid(),
            "Test User",
            new List<TenantMembership>
            {
                new(Guid.NewGuid(), "Tenant 1", new List<Guid> { Guid.NewGuid() }, new List<string> { "admin" })
            });

        handler.Handler = async (req) =>
        {
            var json = JsonSerializer.Serialize(expected, JsonSerializerOptions.Web);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content };
        };

        var directory = CreateDirectory(handler);
        var result = await directory.GetMembershipsAsync(identity, default);

        Assert.NotNull(result);
        Assert.Equal(expected.UserId, result.UserId);
        Assert.Equal(expected.DisplayName, result.DisplayName);
    }

    [Fact]
    public async Task GetMembershipsAsync_Returns404_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler();
        var identity = new IdentityKey("user-oid", "tenant-id");

        handler.Handler = async (req) =>
            new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);

        var directory = CreateDirectory(handler);
        var result = await directory.GetMembershipsAsync(identity, default);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetMembershipsAsync_Returns500_ThrowsUnavailableException()
    {
        var handler = new FakeHttpMessageHandler();
        var identity = new IdentityKey("user-oid", "tenant-id");

        handler.Handler = async (req) =>
            new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);

        var directory = CreateDirectory(handler);

        var ex = await Assert.ThrowsAsync<TenantDirectoryUnavailableException>(
            () => directory.GetMembershipsAsync(identity, default));

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task GetMembershipsAsync_TimesOut_ThrowsUnavailableException()
    {
        var handler = new FakeHttpMessageHandler();
        var identity = new IdentityKey("user-oid", "tenant-id");

        handler.Handler = async (req) =>
        {
            // Simulate timeout
            await Task.Delay(10000);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        };

        var options = Options.Create(new ManagementDirectoryOptions
        {
            Url = "https://registry.example.com",
            Audience = "api://registry",
            Timeout = TimeSpan.FromMilliseconds(100)
        });

        var manifest = new TestManifest();
        var credentialProvider = new FakeAzureIdentityProvider();

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddHttpClient("ps-registry")
            .ConfigureHttpClient(c => c.BaseAddress = new Uri(options.Value.Url))
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var directory = new ManagementApiTenantDirectory(factory, manifest, options, credentialProvider);

        var ex = await Assert.ThrowsAsync<TenantDirectoryUnavailableException>(
            () => directory.GetMembershipsAsync(identity, default));

        Assert.NotNull(ex);
    }
}
