using System.ComponentModel.DataAnnotations;

namespace DmOrder.Infrastructure.Storage;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>"LocalDisk" for development, "S3" for any S3-compatible bucket (R2, MinIO, S3).</summary>
    [Required]
    public string Provider { get; set; } = "LocalDisk";

    /// <summary>Absolute or relative path used by the LocalDisk provider.</summary>
    public string LocalRootPath { get; set; } = "App_Data/uploads";

    /// <summary>Absolute base URL files are served from, e.g. https://api.example.com/media.</summary>
    [Required]
    public string PublicBaseUrl { get; set; } = "http://localhost:5080/media";

    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;

    public string[] AllowedContentTypes { get; set; } =
        ["image/jpeg", "image/png", "image/webp", "image/avif"];

    // --- S3-compatible provider (Cloudflare R2, MinIO, Backblaze B2, S3) --------------------

    /// <summary>
    /// The bucket's API endpoint. For R2 this is
    /// <c>https://{account-id}.r2.cloudflarestorage.com</c> - the S3 API host, not the public one
    /// that serves files, which is <see cref="PublicBaseUrl"/>. Mixing the two up is the usual way
    /// this is misconfigured, and it fails on the first upload rather than at startup.
    /// </summary>
    public string? S3ServiceUrl { get; set; }

    public string? S3Bucket { get; set; }

    public string? S3AccessKeyId { get; set; }

    /// <summary>Secret. Environment variable only - never a config file, never source control.</summary>
    public string? S3SecretAccessKey { get; set; }

    /// <summary>R2 ignores this but SigV4 must sign with something; "auto" is what Cloudflare uses.</summary>
    public string S3Region { get; set; } = "auto";

    /// <summary>
    /// Sends the body without a streaming SHA-256 payload signature.
    ///
    /// **On by default, because R2 requires it.** The AWS SDK v4 signs uploads with
    /// <c>STREAMING-AWS4-HMAC-SHA256-PAYLOAD-TRAILER</c>, which Cloudflare has not implemented -
    /// every upload fails with exactly that message. Verified against a real bucket.
    ///
    /// The request is still signed with SigV4 and still travels over TLS; what is skipped is the
    /// per-chunk body hash, which protects against an in-transit body swap that TLS already
    /// prevents. Safe on real S3 too, so one default serves both.
    /// </summary>
    public bool S3DisablePayloadSigning { get; set; } = true;

    public bool UsesS3 => string.Equals(Provider, "S3", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Fails a half-configured bucket at startup instead of on a seller's first upload.
    ///
    /// A missing secret here does not degrade gracefully the way a missing AI key does: there is no
    /// sensible "storage is switched off" state for a product whose sellers upload photographs, so
    /// refusing to start is the honest response.
    /// </summary>
    public IEnumerable<ValidationResult> ValidateS3()
    {
        if (!UsesS3) yield break;

        foreach (var (value, name) in new[]
        {
            (S3ServiceUrl, nameof(S3ServiceUrl)),
            (S3Bucket, nameof(S3Bucket)),
            (S3AccessKeyId, nameof(S3AccessKeyId)),
            (S3SecretAccessKey, nameof(S3SecretAccessKey)),
        })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                yield return new ValidationResult(
                    $"FileStorage:{name} is required when FileStorage:Provider is \"S3\".");
            }
        }

        if (PublicBaseUrl.Contains("r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase))
        {
            // The API endpoint needs credentials on every request, so images served from it would
            // all 401. Worth naming precisely, because the symptom is every image silently broken.
            yield return new ValidationResult(
                "FileStorage:PublicBaseUrl is the R2 *API* endpoint. Use the bucket's public r2.dev "
                + "URL or your custom domain - the API endpoint cannot serve files to a browser.");
        }
    }
}
