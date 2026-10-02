using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Features.Stores;
using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Media;
using Microsoft.Extensions.Logging;

namespace DmOrder.Application.Features.Media;

public sealed record UploadMediaResult(Guid Id, string Url, string ContentType, long SizeBytes);

public sealed class UploadMediaHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage,
    IImageOptimizer images,
    ILogger<UploadMediaHandler> logger)
{
    /// <summary>Hard ceiling regardless of what the storage provider allows.</summary>
    public const long MaxSizeBytes = 5 * 1024 * 1024;

    public async Task<UploadMediaResult> HandleAsync(
        Stream content,
        string fileName,
        long declaredLength,
        MediaPurpose purpose,
        CancellationToken cancellationToken)
    {
        var store = await StoreLoader.RequireOwnStoreAsync(db, currentUser, cancellationToken);

        if (declaredLength <= 0)
        {
            throw new BusinessRuleException("The file is empty.");
        }

        if (declaredLength > MaxSizeBytes)
        {
            throw new BusinessRuleException($"Images must be {MaxSizeBytes / (1024 * 1024)}MB or smaller.");
        }

        // Buffer the upload so the leading bytes can be inspected before anything is written to
        // storage, and so the real length is known rather than trusted from the request.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        if (buffer.Length > MaxSizeBytes)
        {
            throw new BusinessRuleException($"Images must be {MaxSizeBytes / (1024 * 1024)}MB or smaller.");
        }

        if (buffer.Length == 0)
        {
            throw new BusinessRuleException("The file is empty.");
        }

        buffer.Position = 0;
        var header = new byte[Math.Min(ImageValidation.HeaderLength, buffer.Length)];
        _ = await buffer.ReadAsync(header, cancellationToken);
        buffer.Position = 0;

        var inspection = ImageValidation.Inspect(header);
        if (!inspection.IsImage)
        {
            // The declared content type is ignored entirely — only the bytes decide.
            logger.LogWarning("Rejected upload to store {StoreId}: not a supported image", store.Id);
            throw new BusinessRuleException("Upload a JPEG, PNG or WebP image.");
        }

        /*
         * Re-encoded before it is stored, never after.
         *
         * Nothing reaches the bucket in the shape it arrived: the image is resized, re-encoded as
         * WebP and stripped of its metadata. The metadata is the part that matters - a phone
         * photograph carries GPS coordinates, and a seller shooting a product at home would
         * otherwise publish where they live.
         *
         * A null here means the bytes passed the magic-number check but could not actually be
         * decoded - a truncated file, or a header glued to something else. It is refused rather
         * than stored as-is, because storing it would be storing exactly the thing we cannot
         * inspect.
         */
        using var optimised = await images.OptimiseAsync(buffer, cancellationToken)
            ?? throw new BusinessRuleException(
                "That image could not be read. Try saving it again as a JPEG or PNG.");

        var stored = await storage.SaveAsync(
            optimised.Content,
            // The stored name is generated, never taken from the upload.
            $"upload{optimised.Extension}",
            optimised.ContentType,
            $"stores/{store.Id}/{purpose.ToString().ToLowerInvariant()}",
            cancellationToken);

        var asset = MediaAsset.Create(
            store.Id,
            stored.StorageKey,
            optimised.ContentType,
            stored.SizeBytes,
            SanitiseFileName(fileName),
            purpose,
            currentUser.UserId);

        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);

        return new UploadMediaResult(
            asset.Id,
            storage.GetPublicUrl(asset.StorageKey),
            asset.ContentType,
            asset.SizeBytes);
    }

    /// <summary>
    /// The original name is kept only to show the seller which file they picked. It never reaches a
    /// path or a header, but it is still trimmed and stripped of separators so it cannot be mistaken
    /// for one later.
    /// </summary>
    private static string SanitiseFileName(string fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            return "image";
        }

        var cleaned = new string([.. name.Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != '/' && c != '\\')]);

        return cleaned.Length == 0 ? "image" : cleaned[..Math.Min(cleaned.Length, 255)];
    }
}
