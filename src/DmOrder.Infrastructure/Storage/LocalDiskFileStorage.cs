using DmOrder.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace DmOrder.Infrastructure.Storage;

/// <summary>
/// Development-only storage: writes under a local folder served by the API.
///
/// Not suitable for production on an ephemeral host (Render, Railway, App Service) because the disk is
/// wiped on every deploy. Swap <c>FileStorage:Provider</c> to the S3-compatible implementation before
/// the first real deployment.
/// </summary>
public sealed class LocalDiskFileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    private readonly FileStorageOptions _options = options.Value;

    public async Task<StoredFile> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        string keyPrefix,
        CancellationToken cancellationToken)
    {
        var storageKey = BuildStorageKey(keyPrefix, fileName);
        var fullPath = ResolvePath(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var target = File.Create(fullPath);
        await content.CopyToAsync(target, cancellationToken);

        return new StoredFile(storageKey, contentType, target.Length, fileName);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        var fullPath = ResolvePath(storageKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        var fullPath = ResolvePath(storageKey);
        Stream? stream = File.Exists(fullPath) ? File.OpenRead(fullPath) : null;
        return Task.FromResult(stream);
    }

    public string GetPublicUrl(string storageKey) =>
        $"{_options.PublicBaseUrl.TrimEnd('/')}/{storageKey}";

    private static string BuildStorageKey(string keyPrefix, string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var safeExtension = string.IsNullOrWhiteSpace(extension) ? string.Empty : extension.ToLowerInvariant();
        var prefix = keyPrefix.Trim('/');

        // The stored name is generated, never taken from the upload, so a hostile filename cannot
        // influence the path or the served content type.
        return $"{prefix}/{Guid.CreateVersion7():n}{safeExtension}";
    }

    private string ResolvePath(string storageKey)
    {
        var root = Path.GetFullPath(_options.LocalRootPath);
        var candidate = Path.GetFullPath(Path.Combine(root, storageKey));

        // Defence against path traversal via a crafted storage key.
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved storage path escaped the storage root.");
        }

        return candidate;
    }
}
