using Amazon.S3;
using Amazon.S3.Model;
using DmOrder.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace DmOrder.Infrastructure.Storage;

/// <summary>
/// Object storage for any S3-compatible bucket: Cloudflare R2, MinIO, Backblaze B2, S3 itself.
///
/// This is the production provider. The local-disk one is wiped on every deploy of an ephemeral
/// host, which would quietly delete every seller's logo and product photography.
///
/// **No ACL is set on upload, deliberately.** R2 does not implement per-object ACLs at all and
/// rejects the request, and modern S3 buckets default to Object Ownership = bucket owner enforced,
/// where ACLs are ignored. On both, public read is a property of the bucket - R2's public URL or a
/// custom domain, a bucket policy on S3 - not of the object. Setting one here would break R2 and
/// mislead on S3.
///
/// The bucket must be configured for public read, because <see cref="GetPublicUrl"/> hands the
/// browser a direct URL. Nothing private is ever stored here: these are images a seller publishes on
/// a storefront that anyone can open.
/// </summary>
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly FileStorageOptions _options;

    public S3FileStorage(IOptions<FileStorageOptions> options)
    {
        _options = options.Value;
        _client = CreateClient(_options);
    }

    internal static IAmazonS3 CreateClient(FileStorageOptions options)
    {
        var config = new AmazonS3Config
        {
            // R2 and MinIO are reached by endpoint, not by AWS region. ServiceURL is what makes the
            // SDK talk to them instead of to Amazon.
            ServiceURL = options.S3ServiceUrl,

            // Required for R2 and MinIO. Virtual-host style would resolve
            // bucket.<account>.r2.cloudflarestorage.com, which does not exist.
            ForcePathStyle = true,

            // R2 ignores the region but SigV4 still needs one to sign with; "auto" is what
            // Cloudflare documents.
            AuthenticationRegion = string.IsNullOrWhiteSpace(options.S3Region) ? "auto" : options.S3Region,
        };

        return new AmazonS3Client(options.S3AccessKeyId, options.S3SecretAccessKey, config);
    }

    public async Task<StoredFile> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        string keyPrefix,
        CancellationToken cancellationToken)
    {
        var storageKey = StorageKeys.Build(keyPrefix, fileName);

        // Callers buffer the upload before validating its magic bytes, so this is seekable and its
        // length is known. Guarding anyway: a non-seekable stream would make the SDK buffer the whole
        // body itself with no size ceiling.
        var length = content.CanSeek ? content.Length : throw new InvalidOperationException(
            "S3FileStorage needs a seekable stream so the content length is known before upload.");

        if (content.Position != 0 && content.CanSeek)
        {
            content.Position = 0;
        }

        await _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _options.S3Bucket,
                Key = storageKey,
                InputStream = content,
                ContentType = contentType,

                /*
                 * The SDK closes InputStream when it is done, which would be this provider reaching
                 * back and disposing a buffer its caller still owns. The local provider does not do
                 * that, so a handler that works on disk would break the moment storage was switched
                 * - which is exactly what happened the first time this ran against a real bucket.
                 */
                AutoCloseStream = false,

                DisablePayloadSigning = _options.S3DisablePayloadSigning,
                Headers =
                {
                    // Keys are generated GUIDs, so a URL's content never changes - it can be cached
                    // forever.
                    CacheControl = "public, max-age=31536000, immutable",
                    ContentDisposition = "inline",
                },
            },
            cancellationToken);

        return new StoredFile(storageKey, contentType, length, fileName);
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        StorageKeys.Validate(storageKey);

        await _client.DeleteObjectAsync(
            new DeleteObjectRequest { BucketName = _options.S3Bucket, Key = storageKey },
            cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        StorageKeys.Validate(storageKey);

        try
        {
            var response = await _client.GetObjectAsync(
                new GetObjectRequest { BucketName = _options.S3Bucket, Key = storageKey },
                cancellationToken);

            return response.ResponseStream;
        }
        catch (AmazonS3Exception error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Matches the local provider: a missing object is null, not an exception. A media row
            // whose object has gone is a gap to render around, not a server error.
            return null;
        }
    }

    /// <summary>
    /// The bucket's own public URL, or whatever custom domain is in front of it.
    ///
    /// The API is not in this path at all - the browser fetches straight from the bucket, which is
    /// the whole point of using object storage rather than serving bytes from the application.
    /// </summary>
    public string GetPublicUrl(string storageKey) =>
        $"{_options.PublicBaseUrl.TrimEnd('/')}/{storageKey}";

    public void Dispose() => _client.Dispose();
}
