namespace DmOrder.Application.Common.Interfaces;

/// <summary>
/// Re-encodes an uploaded image smaller, and strips everything that is not pixels.
///
/// Size is the obvious reason and the lesser one. The real reason is metadata: a photograph taken on
/// a phone carries an EXIF block, and that block routinely contains GPS coordinates. A seller
/// photographing a product on their kitchen table and uploading it would publish their home address
/// to anyone who opened the image - silently, with no part of the product ever having asked them.
///
/// So this is not an optimisation that can be skipped when it is inconvenient. An image that cannot
/// be decoded and re-encoded is refused rather than stored as it arrived.
/// </summary>
public interface IImageOptimizer
{
    /// <summary>
    /// Returns a re-encoded image, or null when the bytes cannot be decoded as a supported format.
    ///
    /// The returned stream is owned by the caller and must be disposed. The source is left open and
    /// is not rewound.
    /// </summary>
    Task<OptimisedImage?> OptimiseAsync(Stream source, CancellationToken cancellationToken);
}

/// <param name="Content">Rewound and ready to store.</param>
public sealed record OptimisedImage(
    Stream Content,
    string ContentType,
    string Extension,
    int Width,
    int Height) : IDisposable
{
    public void Dispose() => Content.Dispose();
}
