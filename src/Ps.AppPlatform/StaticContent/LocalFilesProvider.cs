using Microsoft.Extensions.Configuration;

namespace PS.AppPlatform.StaticContent;

public class LocalFilesProvider : IFilesProvider
{
    private readonly string _rootPath;

    public LocalFilesProvider(IConfiguration configuration)
    {
        string rootPath = configuration
            .GetRequiredSection("staticContent")
            .GetRequiredSection("files")
            .GetValue<string>("rootPath") ?? "";
        _rootPath = Path.GetFullPath(rootPath);

        if (!_rootPath.EndsWith(Path.DirectorySeparatorChar))
        {
            _rootPath = _rootPath + Path.DirectorySeparatorChar;
        }
    }

    public async Task<StaticFile?> GetFileAsync(string relativePath, CancellationToken ct = default)
    {
        string fullPath = Path.GetFullPath(Path.Combine(_rootPath, relativePath.TrimStart('/', '\\')));

        if (!fullPath.StartsWith(_rootPath, StringComparison.Ordinal) || !File.Exists(fullPath))
        {
            return null;
        }

        var fileInfo = new FileInfo(fullPath);
        var eTag = $"\"{fileInfo.LastWriteTimeUtc.Ticks:x}-{fileInfo.Length:x}\"";

        var stream = await Task.FromResult(File.OpenRead(fullPath));
        return new StaticFile(
            stream,
            ETag: eTag,
            LastModified: fileInfo.LastWriteTimeUtc,
            Length: fileInfo.Length
        );
    }
}

