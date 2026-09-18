namespace DmOrder.Application.Common.Interfaces;

/// <summary>
/// Binary storage for images. Implementations: local disk in development, S3-compatible object
/// storage (Cloudflare R2 / MinIO / S3) everywhere else. Image bytes never go into PostgreSQL —
/// only the metadata row that points at <see cref="StoredFile.StorageKey"/>.
/// </summary>
public interface IFileStorage
{
    /// <param name="keyPrefix">
    /// Logical folder, e.g. <c>stores/{storeId}/products</c>. Implementations must treat this as
    /// untrusted and sanitise it; callers must never build it from raw user input.
    /// </param>
    Task<StoredFile> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        string keyPrefix,
        CancellationToken cancellationToken);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);

    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>Absolute URL a browser can use to fetch the file.</summary>
    string GetPublicUrl(string storageKey);
}

/// <param name="StorageKey">Provider-independent key persisted in the database.</param>
public sealed record StoredFile(
    string StorageKey,
    string ContentType,
    long SizeBytes,
    string OriginalFileName);
