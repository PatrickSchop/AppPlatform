using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using Xunit;

namespace PS.AppPlatform.Tests;

public class RegisterCommandTests
{
    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            // JWT with oid claim for OID extraction test
            var header = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"none\"}"));
            var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"oid\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\"}"));
            var token = $"{header}.{payload}.sig";
            return new ValueTask<AccessToken>(new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1)));
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

    private IServiceProvider BuildServices(
        FakeHttpMessageHandler handler,
        string url = "https://registry.example.com",
        string audience = "api://registry")
    {
        var services = new ServiceCollection();
        services.Configure<ManagementDirectoryOptions>(opts =>
        {
            opts.Url = url;
            opts.Audience = audience;
        });
        services.AddSingleton<AppManifest>(new TestManifest());          // ← was AddSingleton(manifest)
        services.AddSingleton<TokenCredential>(new FakeTokenCredential()); // ← new: testability seam
        services.AddSingleton<HttpMessageHandler>(handler);                // ← new: testability seam
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RunAsync_MissingPrincipalId_ReturnsExit2()
    {
        var handler = new FakeHttpMessageHandler();
        var services = BuildServices(handler);
        var args = new PlatformCommandArgs(new[] { "--some-arg", "value" });
        var command = new RegisterCommand();

        var exitCode = await command.RunAsync(args, services, default);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task RunAsync_InvalidPrincipalIdFormat_ReturnsExit2()
    {
        var handler = new FakeHttpMessageHandler();
        var services = BuildServices(handler);

        var args = new PlatformCommandArgs(new[] { "--principal-id", "not-a-guid" });
        var command = new RegisterCommand();

        var exitCode = await command.RunAsync(args, services, default);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task RunAsync_NoManifest_ReturnsExit2()
    {
        var handler = new FakeHttpMessageHandler();
        var services = new ServiceCollection()
            .Configure<ManagementDirectoryOptions>(opts =>
            {
                opts.Url = "https://registry.example.com";
                opts.Audience = "api://registry";
            })
            .BuildServiceProvider();

        var args = new PlatformCommandArgs(new[] { "--principal-id", "12345678-1234-1234-1234-123456789012" });
        var command = new RegisterCommand();

        var exitCode = await command.RunAsync(args, services, default);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task RunAsync_MissingOptions_ReturnsExit2()
    {
        var services = new ServiceCollection()
            .AddSingleton<AppManifest>(new TestManifest())
            .BuildServiceProvider();

        var args = new PlatformCommandArgs(new[] { "--principal-id", "12345678-1234-1234-1234-123456789012" });
        var command = new RegisterCommand();

        var exitCode = await command.RunAsync(args, services, default);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task RunAsync_MissingUrlInOptions_ReturnsExit2()
    {
        var services = new ServiceCollection()
            .AddSingleton<AppManifest>(new TestManifest())
            .Configure<ManagementDirectoryOptions>(opts =>
            {
                opts.Url = "";
                opts.Audience = "api://registry";
            })
            .BuildServiceProvider();

        var args = new PlatformCommandArgs(new[] { "--principal-id", "12345678-1234-1234-1234-123456789012" });
        var command = new RegisterCommand();

        var exitCode = await command.RunAsync(args, services, default);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task RunAsync_SuccessfulRegistration_ReturnsExit0()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler();
        handler.Handler = async (req) =>
        {
            captured = req;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { applicationId = Guid.NewGuid(), displayName = "Test" }),
                    Encoding.UTF8, "application/json")
            };
        };

        var services = BuildServices(handler);
        var args = new PlatformCommandArgs(new[] { "--principal-id", "12345678-1234-1234-1234-123456789012" });
        var command = new RegisterCommand();

        var exitCode = await command.RunAsync(args, services, default);

        Assert.Equal(0, exitCode);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Put, captured!.Method);
        Assert.Contains("/api/registry/v1/applications/testapp", captured.RequestUri!.ToString());
    }

    [Fact]
    public async Task RunAsync_Forbidden403_ReturnsExit1WithRoleInfo()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Handler = async (req) =>
            new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden);

        var services = BuildServices(handler);
        var args = new PlatformCommandArgs(new[] { "--principal-id", "12345678-1234-1234-1234-123456789012" });
        var command = new RegisterCommand();

        var originalErr = Console.Error;
        using var sw = new System.IO.StringWriter();
        Console.SetError(sw);
        try
        {
            var exitCode = await command.RunAsync(args, services, default);
            Assert.Equal(1, exitCode);
            Assert.Contains("trustedDeployers", sw.ToString());
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }
}
