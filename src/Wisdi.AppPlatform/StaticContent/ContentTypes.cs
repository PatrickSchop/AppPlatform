namespace Wisdi.AppPlatform.StaticContent;

public static class ContentTypes
{
    private static readonly Dictionary<string, string> TypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".html", "text/html; charset=utf-8" },
        { ".css", "text/css; charset=utf-8" },
        { ".js", "text/javascript; charset=utf-8" },
        { ".mjs", "text/javascript; charset=utf-8" },
        { ".json", "application/json; charset=utf-8" },
        { ".map", "application/json; charset=utf-8" },
        { ".png", "image/png" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".gif", "image/gif" },
        { ".svg", "image/svg+xml; charset=utf-8" },
        { ".ico", "image/x-icon" },
        { ".webp", "image/webp" },
        { ".woff", "font/woff" },
        { ".woff2", "font/woff2" },
        { ".ttf", "font/ttf" },
        { ".txt", "text/plain; charset=utf-8" },
        { ".wasm", "application/wasm" },
        { ".xml", "application/xml; charset=utf-8" }
    };

    public static string For(string path)
    {
        string extension = Path.GetExtension(path);
        return TypeMap.TryGetValue(extension, out var contentType)
            ? contentType
            : "application/octet-stream";
    }
}
