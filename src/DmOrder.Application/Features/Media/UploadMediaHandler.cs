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
            throw new BusinessRuleException("Upload a JPEG, PNG, WebP or AVIF image.");
        }

        var stored = await storage.SaveAsync(
            buffer,
            // The stored name is generated from the *detected* type, never from the uploaded filename.
            $"upload{inspection.Extension}",
            inspection.ContentType!,
            $"stores/{store.Id}/{purpose.ToString().ToLowerInvariant()}",
            cancellationToken);

        var asset = MediaAsset.Create(
            store.Id,
            stored.StorageKey,
            inspection.ContentType!,
            buffer.Length,
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
