using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PS.AppPlatform.StaticContent;

public class StaticContentHandler
{
    private readonly IFilesProvider _filesProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StaticContentHandler> _logger;
    private readonly string _immutableMaxAge;

    public StaticContentHandler(
        IFilesProvider filesProvider,
        IConfiguration configuration,
        ILogger<StaticContentHandler> logger)
    {
        _filesProvider = filesProvider;
        _configuration = configuration;
        _logger = logger;
        _immutableMaxAge = configuration.GetValue("staticContent:cacheControl:immutableMaxAge", "public, max-age=31536000, immutable");
    }

    public async Task<IActionResult> HandleAsync(HttpRequest request, string path, CancellationToken ct = default)
    {
        // Normalize: trim leading /; if empty, use index.html
        path = path?.TrimStart('/', '\\') ?? "";
        if (string.IsNullOrEmpty(path))
        {
            path = "index.html";
        }

        // Reject any path containing ..
        if (path.Contains(".."))
        {
            return new BadRequestResult();
        }

        // Try to get the file
        var file = await _filesProvider.GetFileAsync(path, ct);

        // If not found, apply SPA fallback: serve index.html for extensionless paths
        if (file is null && !Path.HasExtension(path))
        {
            file = await _filesProvider.GetFileAsync("index.html", ct);
        }

        // Still null â†’ NotFound
        if (file is null)
        {
            return new NotFoundResult();
        }

        // If-None-Match: return 304 if ETag matches
        if (file.ETag is not null &&
            request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch) &&
            ifNoneMatch.Contains(file.ETag))
        {
            file.Content.Dispose();
            return new StatusCodeResult(StatusCodes.Status304NotModified);
        }

        // Set cache headers based on whether this is index.html or a fallback-served file
        string cacheControl = (path == "index.html" || path.Contains("index.html"))
            ? "no-cache, must-revalidate"
            : _immutableMaxAge;

        var result = new FileStreamResult(file.Content, ContentTypes.For(path));

        result.EnableRangeProcessing = true;

        // Set cache control
        result.FileDownloadName = null; // Don't force download

        // Need to set headers manually since FileStreamResult doesn't expose them directly
        // We'll do this through the response after the action executes

        // Set Last-Modified if available
        if (file.LastModified.HasValue)
        {
            result.LastModified = file.LastModified.Value;
        }

        // Set ETag if available
        if (file.ETag is not null)
        {
            result.EntityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue(file.ETag);
        }

        // We need to return the result and let the response write the cache headers
        // Create a custom response wrapper
        return new CachedFileStreamResult(result, cacheControl);
    }
}

// Helper class to wrap FileStreamResult with cache control headers
public class CachedFileStreamResult : IActionResult
{
    private readonly FileStreamResult _innerResult;
    private readonly string _cacheControl;

    public CachedFileStreamResult(FileStreamResult innerResult, string cacheControl)
    {
        _innerResult = innerResult;
        _cacheControl = cacheControl;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        context.HttpContext.Response.Headers.CacheControl = _cacheControl;
        await _innerResult.ExecuteResultAsync(context);
    }
}

