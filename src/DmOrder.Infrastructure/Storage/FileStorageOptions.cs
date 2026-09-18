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
}
