namespace Wisdi.AppPlatform.StaticContent;

/// <summary>
/// Provides static files with metadata for caching and content negotiation.
/// </summary>
public interface IFilesProvider
{
    /// <summary>
    /// Returns null when the file does not exist. Never throws for a missing file.
    /// </summary>
    Task<StaticFile?> GetFileAsync(string relativePath, CancellationToken ct = default);
}

public sealed record StaticFile(Stream Content, string? ETag, DateTimeOffset? LastModified, long? Length);
