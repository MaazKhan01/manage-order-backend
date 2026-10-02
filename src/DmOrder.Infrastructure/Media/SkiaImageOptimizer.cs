using System.ComponentModel.DataAnnotations;
using DmOrder.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace DmOrder.Infrastructure.Media;

public sealed class ImageOptimizerOptions
{
    public const string SectionName = "ImageOptimizer";

    /// <summary>
    /// Longest edge, in pixels. A phone camera produces 4000px or more; a storefront shows these at
    /// a few hundred CSS pixels, and next/image resizes again on the way out. Anything beyond this
    /// is bandwidth and storage nobody ever sees.
    /// </summary>
    [Range(320, 8000)]
    public int MaxDimension { get; set; } = 2000;

    /// <summary>WebP quality. 80 is the usual sweet spot for photographs.</summary>
    [Range(1, 100)]
    public int Quality { get; set; } = 80;

    /// <summary>
    /// Refuses an image whose decoded size would be absurd, before a single pixel is allocated.
    ///
    /// This is decompression-bomb protection, not a quality setting. A 5 MB PNG of one flat colour
    /// can describe a 30,000 x 30,000 canvas, which is 3.6 GB of RGBA once decoded - an upload well
    /// inside the size limit that still takes the whole API down. Fifty megapixels is far above any
    /// real camera and far below anything dangerous.
    /// </summary>
    [Range(1, 500)]
    public int MaxMegapixels { get; set; } = 50;
}

/// <summary>
/// Image processing with SkiaSharp.
///
/// **Everything is re-encoded to WebP.** It is meaningfully smaller than JPEG at the same perceived
/// quality, every browser in use supports it, and one output format means one content type and one
/// extension rather than a matrix of them.
///
/// SkiaSharp rather than ImageSharp, which was the first choice and had to be abandoned: ImageSharp
/// 4 refuses to build in Release without a paid Six Labors licence key. It compiles happily in
/// Debug, so that would have surfaced as a failed production build and nowhere earlier. Skia is MIT,
/// needs no key, and has no revenue threshold to outgrow.
///
/// Metadata is stripped by construction rather than by effort: decoding to a bitmap keeps pixels and
/// nothing else, so EXIF - including the GPS coordinates a phone writes into every photograph -
/// cannot survive. Orientation is the one tag that must be honoured before it is lost, which is what
/// the origin handling below is for.
/// </summary>
public sealed class SkiaImageOptimizer(
    IOptions<ImageOptimizerOptions> options,
    ILogger<SkiaImageOptimizer> logger) : IImageOptimizer
{
    private readonly ImageOptimizerOptions _options = options.Value;

    public Task<OptimisedImage?> OptimiseAsync(Stream source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // SKManagedStream wraps without owning: the caller still owns `source`.
        using var codec = SKCodec.Create(new SKManagedStream(source));

        if (codec is null)
        {
            // No filename and no bytes in the log - this is a seller's or a customer's image.
            logger.LogWarning("Rejected an upload that could not be decoded as a supported image.");
            return Task.FromResult<OptimisedImage?>(null);
        }

        // The header alone gives the dimensions, so the bomb check happens before any pixel buffer
        // is allocated.
        var megapixels = (long)codec.Info.Width * codec.Info.Height / 1_000_000d;
        if (megapixels > _options.MaxMegapixels)
        {
            logger.LogWarning(
                "Rejected an upload of {Megapixels:F1}MP, over the {Limit}MP ceiling.",
                megapixels,
                _options.MaxMegapixels);
            return Task.FromResult<OptimisedImage?>(null);
        }

        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            logger.LogWarning("An upload identified as an image could not be decoded.");
            return Task.FromResult<OptimisedImage?>(null);
        }

        // Orientation first: the EXIF flag has to become real pixels before the metadata is dropped,
        // or a photo taken sideways on a phone is stored sideways.
        using var upright = ApplyOrientation(decoded, codec.EncodedOrigin);
        using var sized = Resize(upright);

        using var image = SKImage.FromBitmap(sized);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, _options.Quality);

        if (encoded is null)
        {
            logger.LogWarning("Could not re-encode an upload as WebP.");
            return Task.FromResult<OptimisedImage?>(null);
        }

        // Not a using: ownership passes to the caller through OptimisedImage.
        var output = new MemoryStream();
        encoded.SaveTo(output);
        output.Position = 0;

        return Task.FromResult<OptimisedImage?>(
            new OptimisedImage(output, "image/webp", ".webp", sized.Width, sized.Height));
    }

    /// <summary>
    /// Scales down to the ceiling, never up.
    ///
    /// Enlarging makes a file both bigger and blurrier - a 120px logo stretched to 2000px is a worse
    /// image than the one it replaced, and costs more to serve.
    /// </summary>
    private SKBitmap Resize(SKBitmap source)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= _options.MaxDimension) return source.Copy();

        var scale = (double)_options.MaxDimension / longest;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        var target = new SKBitmap(new SKImageInfo(width, height, source.ColorType, source.AlphaType));

        // Mitchell cubic: the usual choice for downscaling photographs, and visibly better than
        // nearest or bilinear at the ratios a phone photo needs.
        source.ScalePixels(target, new SKSamplingOptions(SKCubicResampler.Mitchell));

        return target;
    }

    /// <summary>
    /// Rotates and flips so the stored pixels are the right way up.
    ///
    /// A phone does not rewrite pixels when you turn it; it writes an orientation tag and leaves
    /// them as the sensor saw them. Honouring that tag here is what lets the tag itself be
    /// discarded with the rest of the metadata.
    /// </summary>
    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return source.Copy();

        // The four quarter-turn origins swap width and height; the mirrored ones do not.
        var swapsAxes = origin
            is SKEncodedOrigin.LeftTop
            or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom
            or SKEncodedOrigin.LeftBottom;

        var width = swapsAxes ? source.Height : source.Width;
        var height = swapsAxes ? source.Width : source.Height;

        var target = new SKBitmap(new SKImageInfo(width, height, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(target);

        // Each case maps one EXIF origin onto the transform that undoes it.
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Translate(width, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.Translate(width, height);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Translate(0, height);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(width, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(width, 0);
                canvas.RotateDegrees(90);
                canvas.Translate(height, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, height);
                canvas.RotateDegrees(270);
                break;
        }

        // The sampling overload; the plain one is obsolete and warns as an error here.
        canvas.DrawBitmap(source, 0, 0, SKSamplingOptions.Default);
        canvas.Flush();

        return target;
    }
}
