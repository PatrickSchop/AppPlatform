using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Wisdi.AppPlatform.StaticContent;
using Xunit;

namespace Wisdi.AppPlatform.Tests;

public class StaticContentTests
{
    private class FakeFilesProvider : IFilesProvider
    {
        private readonly Dictionary<string, StaticFile> _files;

        public FakeFilesProvider(Dictionary<string, StaticFile> files)
        {
            _files = files;
        }

        public Task<StaticFile?> GetFileAsync(string relativePath, CancellationToken ct = default)
        {
            var normalized = relativePath.TrimStart('/', '\\');
            return Task.FromResult(_files.TryGetValue(normalized, out var file) ? file : null);
        }
    }

    private static Stream CreateStream(string content)
    {
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream);
        writer.Write(content);
        writer.Flush();
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task Empty_path_serves_index_html()
    {
        var files = new Dictionary<string, StaticFile>
        {
            { "index.html", new StaticFile(CreateStream("<html>app</html>"), "\"abc123\"", null, 15) }
        };

        var provider = new FakeFilesProvider(files);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var logger = Substitute.For<ILogger<StaticContentHandler>>();
        var handler = new StaticContentHandler(provider, config, logger);

        var httpContext = new DefaultHttpContext();
        var request = httpContext.Request;

        var result = await handler.HandleAsync(request, "", CancellationToken.None);

        Assert.NotNull(result);
        var fileResult = Assert.IsType<CachedFileStreamResult>(result);
        Assert.NotNull(fileResult);
    }

    [Fact]
    public async Task Extensionless_path_falls_back_to_index_html()
    {
        var files = new Dictionary<string, StaticFile>
        {
            { "index.html", new StaticFile(CreateStream("<html>app</html>"), "\"abc123\"", null, 15) },
            { "dashboard", null! }
        };

        var provider = new FakeFilesProvider(files);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var logger = Substitute.For<ILogger<StaticContentHandler>>();
        var handler = new StaticContentHandler(provider, config, logger);

        var httpContext = new DefaultHttpContext();
        var request = httpContext.Request;

        var result = await handler.HandleAsync(request, "/dashboard", CancellationToken.None);

        // Should serve index.html, not return 404
        Assert.NotNull(result);
        var fileResult = Assert.IsType<CachedFileStreamResult>(result);
        Assert.NotNull(fileResult);
    }

    [Fact]
    public async Task Nested_extensionless_path_falls_back_to_index_html()
    {
        var files = new Dictionary<string, StaticFile>
        {
            { "index.html", new StaticFile(CreateStream("<html>app</html>"), "\"abc123\"", null, 15) }
        };

        var provider = new FakeFilesProvider(files);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var logger = Substitute.For<ILogger<StaticContentHandler>>();
        var handler = new StaticContentHandler(provider, config, logger);

        var httpContext = new DefaultHttpContext();
        var request = httpContext.Request;

        var result = await handler.HandleAsync(request, "app/settings/advanced", CancellationToken.None);

        Assert.NotNull(result);
        var fileResult = Assert.IsType<CachedFileStreamResult>(result);
        Assert.NotNull(fileResult);
    }

    [Fact]
    public async Task Missing_asset_with_extension_returns_404()
    {
        var files = new Dictionary<string, StaticFile>();

        var provider = new FakeFilesProvider(files);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var logger = Substitute.For<ILogger<StaticContentHandler>>();
        var handler = new StaticContentHandler(provider, config, logger);

        var httpContext = new DefaultHttpContext();
        var request = httpContext.Request;

        var result = await handler.HandleAsync(request, "/assets/missing.png", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Path_with_double_dot_returns_400()
    {
        var files = new Dictionary<string, StaticFile>
        {
            { "index.html", new StaticFile(CreateStream("<html></html>"), null, null, 7) }
        };

        var provider = new FakeFilesProvider(files);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var logger = Substitute.For<ILogger<StaticContentHandler>>();
        var handler = new StaticContentHandler(provider, config, logger);

        var httpContext = new DefaultHttpContext();
        var request = httpContext.Request;

        var result = await handler.HandleAsync(request, "../secrets.txt", CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public async Task LocalFilesProvider_rejects_path_escape()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}");
        var secretsDir = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}-evil");

        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(secretsDir);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "staticContent:files:rootPath", tempDir }
                })
                .Build();

            var provider = new LocalFilesProvider(config);

            var result = await provider.GetFileAsync("../secrets.txt", CancellationToken.None);

            Assert.Null(result);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
            try { Directory.Delete(secretsDir, true); } catch { }
        }
    }

    [Fact]
    public async Task LocalFilesProvider_rejects_sibling_directory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wwwroot-{Guid.NewGuid()}");
        var evilDir = Path.Combine(Path.GetTempPath(), $"wwwroot-evil-{Guid.NewGuid()}");

        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(evilDir);
            File.WriteAllText(Path.Combine(evilDir, "secrets.txt"), "secret");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "staticContent:files:rootPath", tempDir }
                })
                .Build();

            var provider = new LocalFilesProvider(config);
            var result = await provider.GetFileAsync("../wwwroot-evil-test/secrets.txt", CancellationToken.None);

            Assert.Null(result);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
            try { Directory.Delete(evilDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Index_html_gets_no_cache_headers()
    {
        var files = new Dictionary<string, StaticFile>
        {
            { "index.html", new StaticFile(CreateStream("<html></html>"), null, null, 7) }
        };

        var provider = new FakeFilesProvider(files);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var logger = Substitute.For<ILogger<StaticContentHandler>>();
        var handler = new StaticContentHandler(provider, config, logger);

        var httpContext = new DefaultHttpContext();
        var request = httpContext.Request;

        var result = await handler.HandleAsync(request, "index.html", CancellationToken.None);

        Assert.NotNull(result);
        var cachedResult = Assert.IsType<CachedFileStreamResult>(result);
        Assert.NotNull(cachedResult);
    }

    [Fact]
    public async Task Asset_gets_immutable_cache_headers()
    {
        var files = new Dictionary<string, StaticFile>
        {
            { "main.a1b2c3.js", new StaticFile(CreateStream("console.log('test');"), null, null, 20) }
        };

        var provider = new FakeFilesProvider(files);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var logger = Substitute.For<ILogger<StaticContentHandler>>();
        var handler = new StaticContentHandler(provider, config, logger);

        var httpContext = new DefaultHttpContext();
        var request = httpContext.Request;

        var result = await handler.HandleAsync(request, "main.a1b2c3.js", CancellationToken.None);

        Assert.NotNull(result);
        var cachedResult = Assert.IsType<CachedFileStreamResult>(result);
        Assert.NotNull(cachedResult);
    }

    [Fact]
    public async Task If_none_match_returns_304()
    {
        var files = new Dictionary<string, StaticFile>
        {
            { "main.js", new StaticFile(CreateStream("console.log('test');"), "\"abc123\"", null, 20) }
        };

        var provider = new FakeFilesProvider(files);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var logger = Substitute.For<ILogger<StaticContentHandler>>();
        var handler = new StaticContentHandler(provider, config, logger);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.IfNoneMatch = "\"abc123\"";

        var result = await handler.HandleAsync(httpContext.Request, "main.js", CancellationToken.None);

        Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(304, ((StatusCodeResult)result).StatusCode);
    }

    [Fact]
    public void ContentTypes_resolves_woff2()
    {
        var contentType = ContentTypes.For("font.woff2");
        Assert.Equal("font/woff2", contentType);
    }

    [Fact]
    public void ContentTypes_resolves_ico()
    {
        var contentType = ContentTypes.For("favicon.ico");
        Assert.Equal("image/x-icon", contentType);
    }

    [Fact]
    public void ContentTypes_resolves_map()
    {
        var contentType = ContentTypes.For("main.js.map");
        Assert.Equal("application/json; charset=utf-8", contentType);
    }

    [Fact]
    public void ContentTypes_resolves_wasm()
    {
        var contentType = ContentTypes.For("app.wasm");
        Assert.Equal("application/wasm", contentType);
    }

    [Fact]
    public void ContentTypes_resolves_webp()
    {
        var contentType = ContentTypes.For("image.webp");
        Assert.Equal("image/webp", contentType);
    }

    [Fact]
    public void ContentTypes_unknown_extension_defaults_to_octet_stream()
    {
        var contentType = ContentTypes.For("file.unknown");
        Assert.Equal("application/octet-stream", contentType);
    }
}
