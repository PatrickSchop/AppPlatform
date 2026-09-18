using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using PS.AppPlatform.Hosting;

namespace PS.AppPlatform.StaticContent;

public class BlobProvider : IFilesProvider
{
    private readonly BlobContainerClient _blobContainerClient;

    public BlobProvider(IConfiguration configuration, IAzureIdentityProvider identityProvider)
    {
        var blobsSection = configuration
            .GetRequiredSection("staticContent")
            .GetRequiredSection("blob");

        string uri = blobsSection.GetValue<string>("uri") ??
            throw new InvalidOperationException("Blob uri is required");

        _blobContainerClient = new BlobContainerClient(new Uri(uri), identityProvider.Credential);
    }

    public async Task<StaticFile?> GetFileAsync(string relativePath, CancellationToken ct = default)
    {
        string normalizedPath = relativePath.TrimStart('/', '\\');
        var blobClient = _blobContainerClient.GetBlobClient(normalizedPath);

        try
        {
            var response = await blobClient.DownloadStreamingAsync(cancellationToken: ct);
            var details = response.Value.Details;

            return new StaticFile(
                response.Value.Content,
                ETag: details.ETag.ToString(),
                LastModified: details.LastModified,
                Length: details.ContentLength
            );
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}

