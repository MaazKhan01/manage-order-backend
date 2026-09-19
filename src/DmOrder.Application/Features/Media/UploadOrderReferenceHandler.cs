using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DmOrder.Application.Features.Media;

/// <summary>
/// A customer's reference photo, uploaded anonymously before the order is submitted.
///
/// "Send me a picture of what you want" is how these sellers already work, so this has to exist. It
/// is also the only endpoint in the product where an unauthenticated caller writes a file, which is
/// why it is separate from the seller upload rather than sharing a route with a flag.
///
/// Defences: rate limiting at the endpoint, a smaller size cap than the seller's, magic-byte
/// validation, and the resulting media is bound to one store so it cannot be pointed anywhere else.
/// An upload that never becomes an order is an orphan; sweeping those is a background job, not a
/// request-path concern.
/// </summary>
public sealed class UploadOrderReferenceHandler(
    IAppDbContext db,
    IFileStorage storage,
    ILogger<UploadOrderReferenceHandler> logger)
{
    /// <summary>Smaller than the seller's limit — a phone photo is plenty, and this path is anonymous.</summary>
    public const long MaxSizeBytes = 3 * 1024 * 1024;

    public async Task<UploadMediaResult> HandleAsync(
        string storeSlug,
        Stream content,
        long declaredLength,
        CancellationToken cancellationToken)
    {
        var slug = storeSlug.Trim().ToLowerInvariant();

        // Only a live storefront accepts uploads. Without this, an unpublished store is still a
        // writable file endpoint.
        var storeId = await db.Stores
            .Where(s => s.Slug == slug && s.IsPublished && s.IsActive)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Store", slug);

        if (declaredLength <= 0)
        {
            throw new BusinessRuleException("The file is empty.");
        }

        if (declaredLength > MaxSizeBytes)
        {
            throw new BusinessRuleException($"Photos must be {MaxSizeBytes / (1024 * 1024)}MB or smaller.");
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        // Re-checked against the real length: the declared one is the client's claim.
        if (buffer.Length is 0 or > MaxSizeBytes)
        {
            throw new BusinessRuleException($"Photos must be between 1 byte and {MaxSizeBytes / (1024 * 1024)}MB.");
        }

        buffer.Position = 0;
        var header = new byte[Math.Min(ImageValidation.HeaderLength, buffer.Length)];
        _ = await buffer.ReadAsync(header, cancellationToken);
        buffer.Position = 0;

        var inspection = ImageValidation.Inspect(header);
        if (!inspection.IsImage)
        {
            logger.LogWarning("Rejected an anonymous upload to store {StoreId}: not a supported image", storeId);
            throw new BusinessRuleException("Upload a JPEG, PNG, WebP or AVIF photo.");
        }

        var stored = await storage.SaveAsync(
            buffer,
            $"reference{inspection.Extension}",
            inspection.ContentType!,
            $"stores/{storeId}/orderreference",
            cancellationToken);

        var asset = MediaAsset.Create(
            storeId,
            stored.StorageKey,
            inspection.ContentType!,
            buffer.Length,
            // The customer's filename is never kept: it is personal data we have no use for, and it
            // would end up shown to the seller for no reason.
            "reference",
            MediaPurpose.OrderReference,
            uploadedByUserId: null);

        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);

        return new UploadMediaResult(
            asset.Id, storage.GetPublicUrl(asset.StorageKey), asset.ContentType, asset.SizeBytes);
    }
}
