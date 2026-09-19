using DmOrder.Domain.Common;

namespace DmOrder.Domain.Media;

/// <summary>
/// Metadata for one uploaded file. The bytes live in object storage behind
/// <c>IFileStorage</c>; PostgreSQL holds only this row.
///
/// One table serves store logos, covers, backgrounds, product images and customer reference uploads.
/// A separate StoreMedia table was considered and rejected — it would have been these same columns
/// under a different name.
/// </summary>
public sealed class MediaAsset : Entity, ITenantOwned
{
    private MediaAsset() { }

    public Guid StoreId { get; private set; }

    /// <summary>Provider-independent key. Meaningless without the configured storage provider.</summary>
    public string StorageKey { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    public int? Width { get; private set; }

    public int? Height { get; private set; }

    /// <summary>Kept for the seller's benefit only. Never used to build a path or serve the file.</summary>
    public string OriginalFileName { get; private set; } = null!;

    public Guid? UploadedByUserId { get; private set; }

    public MediaPurpose Purpose { get; private set; }

    public static MediaAsset Create(
        Guid storeId,
        string storageKey,
        string contentType,
        long sizeBytes,
        string originalFileName,
        MediaPurpose purpose,
        Guid? uploadedByUserId,
        int? width = null,
        int? height = null) =>
        new()
        {
            StoreId = storeId,
            StorageKey = storageKey,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            OriginalFileName = originalFileName,
            Purpose = purpose,
            UploadedByUserId = uploadedByUserId,
            Width = width,
            Height = height,
        };
}

/// <summary>
/// What an upload is for. Drives size and dimension expectations, and lets the seller's media library
/// be filtered sensibly later.
/// </summary>
public enum MediaPurpose
{
    StoreLogo = 0,
    StoreCover = 1,
    StoreBackground = 2,
    ProductImage = 3,
    OrderReference = 4,
}
