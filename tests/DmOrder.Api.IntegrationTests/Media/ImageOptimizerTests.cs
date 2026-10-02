using System.Text;
using DmOrder.Infrastructure.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using SkiaSharp;

namespace DmOrder.Api.IntegrationTests.Media;

/// <summary>
/// Image processing, which every upload now passes through.
///
/// The metadata test is the one that matters most. Everything else here is about size; that one is
/// about a seller publishing their home coordinates without ever being asked.
/// </summary>
public sealed class ImageOptimizerTests
{
    private static SkiaImageOptimizer Optimizer(int maxDimension = 2000, int maxMegapixels = 50) =>
        new(
            Options.Create(new ImageOptimizerOptions
            {
                MaxDimension = maxDimension,
                MaxMegapixels = maxMegapixels,
                Quality = 80,
            }),
            NullLogger<SkiaImageOptimizer>.Instance);

    /// <summary>A JPEG with a gradient, so compression comparisons mean something.</summary>
    private static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor((byte)(x % 256), (byte)(y % 256), (byte)((x + y) % 256)));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        return data.ToArray();
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Splices an EXIF block into a JPEG.
    ///
    /// Skia can read metadata but not write it, so the fixture is assembled by hand: an APP1 segment
    /// carrying the standard "Exif\0\0" header plus a recognisable payload, inserted straight after
    /// the SOI marker where a camera would put it. That is enough to prove the real property - that
    /// what arrives in the metadata does not come out the other side.
    /// </summary>
    private static byte[] WithExif(byte[] jpeg, string payload)
    {
        var body = Encoding.ASCII.GetBytes("Exif\0\0" + payload);
        var length = body.Length + 2;

        var output = new List<byte>(jpeg.Length + length + 4);
        output.AddRange(jpeg[..2]); // SOI: FF D8
        output.AddRange([0xFF, 0xE1, (byte)(length >> 8), (byte)(length & 0xFF)]);
        output.AddRange(body);
        output.AddRange(jpeg[2..]);

        return [.. output];
    }

    [Fact]
    public async Task Metadata_does_not_survive_re_encoding()
    {
        // The whole reason this component exists. A phone photograph taken at home carries the
        // location of the home in exactly this kind of block.
        const string secret = "GPSLatitude31.5204GPSLongitude74.3587";
        var source = WithExif(Jpeg(800, 600), secret);

        // The fixture really does carry it, or the assertion below would pass for the wrong reason.
        Encoding.ASCII.GetString(source).ShouldContain(secret);

        using var input = new MemoryStream(source);
        using var result = await Optimizer().OptimiseAsync(input, CancellationToken.None);

        result.ShouldNotBeNull();
        var stored = ((MemoryStream)result.Content).ToArray();
        Encoding.ASCII.GetString(stored).ShouldNotContain(secret);
        Encoding.ASCII.GetString(stored).ShouldNotContain("Exif");
    }

    [Fact]
    public async Task Everything_comes_out_as_webp()
    {
        using var input = new MemoryStream(Jpeg(800, 600));

        using var result = await Optimizer().OptimiseAsync(input, CancellationToken.None);

        result.ShouldNotBeNull();
        result.ContentType.ShouldBe("image/webp");
        result.Extension.ShouldBe(".webp");
    }

    [Fact]
    public async Task An_oversized_photograph_is_scaled_to_the_ceiling()
    {
        using var input = new MemoryStream(Jpeg(4000, 3000));

        using var result = await Optimizer(maxDimension: 2000).OptimiseAsync(input, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Width.ShouldBe(2000);
        // Aspect ratio preserved rather than squashed to a square.
        result.Height.ShouldBe(1500);
    }

    [Fact]
    public async Task A_small_image_is_never_enlarged()
    {
        // A 120px logo blown up to 2000px would be both bigger and blurrier than the original.
        using var input = new MemoryStream(Jpeg(120, 120));

        using var result = await Optimizer().OptimiseAsync(input, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Width.ShouldBe(120);
        result.Height.ShouldBe(120);
    }

    [Fact]
    public async Task A_decompression_bomb_is_refused_before_it_is_decoded()
    {
        // A flat PNG compresses to almost nothing while describing an enormous canvas. Decoding one
        // allocates width x height x 4 bytes - an upload well inside the 5MB limit that is still an
        // outage.
        using var input = new MemoryStream(Png(4000, 4000));

        // 16MP against a 10MP ceiling.
        var result = await Optimizer(maxMegapixels: 10).OptimiseAsync(input, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Bytes_that_are_not_an_image_are_refused_rather_than_thrown_over()
    {
        using var input = new MemoryStream("this is not an image"u8.ToArray());

        var result = await Optimizer().OptimiseAsync(input, CancellationToken.None);

        // Null, not an exception: the caller turns this into a readable message for the seller.
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Re_encoding_actually_makes_a_photograph_smaller()
    {
        var source = Jpeg(3000, 2000);
        using var input = new MemoryStream(source);

        using var result = await Optimizer().OptimiseAsync(input, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Content.Length.ShouldBeLessThan(source.Length);
    }
}
