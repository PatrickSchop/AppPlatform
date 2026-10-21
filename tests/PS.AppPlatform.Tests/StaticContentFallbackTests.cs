using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PS.AppPlatform.StaticContent;
using Xunit;

namespace PS.AppPlatform.Tests;

public class StaticContentFallbackTests
{
    private sealed class Files(Dictionary<string, string> content) : IFilesProvider
    {
        public Task<StaticFile?> GetFileAsync(string relativePath, CancellationToken ct = default)
        {
            var key = relativePath.TrimStart('/');
            if (!content.TryGetValue(key, out var text)) return Task.FromResult<StaticFile?>(null);
            return Task.FromResult<StaticFile?>(new StaticFile(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)), null, null, text.Length));
        }
    }

    private static async Task<HttpResponse> Get(string path)
    {
        var provider = new Files(new() { ["index.html"] = "<html>app</html>", ["main-ABC123.js"] = "console.log(1)" });
        var config = new ConfigurationBuilder().Build();
        var handler = new StaticContentHandler(provider, config, Substitute.For<ILogger<StaticContentHandler>>());

        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().AddMvcCore().Services.BuildServiceProvider() };
        http.Response.Body = new MemoryStream();
        var result = await handler.HandleAsync(http.Request, path, CancellationToken.None);
        await result.ExecuteResultAsync(new ActionContext(http, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
        return http.Response;
    }

    [Theory]
    [InlineData("no-access")]
    [InlineData("applications")]
    [InlineData("invite/abc-123")]
    public async Task SpaRoute_IsServedAsHtml_NotAsADownload(string route)
    {
        var response = await Get(route);

        Assert.StartsWith("text/html", response.ContentType);
    }

    [Fact]
    public async Task SpaRoute_IsNotCachedAsImmutable()
    {
        // The shell changes with every deploy; a year-long immutable cache pins a stale bundle.
        var response = await Get("no-access");

        Assert.Equal("no-cache, must-revalidate", response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task HashedAsset_StaysImmutable()
    {
        var response = await Get("main-ABC123.js");

        Assert.StartsWith("text/javascript", response.ContentType);
        Assert.Contains("immutable", response.Headers.CacheControl.ToString());
    }
}
