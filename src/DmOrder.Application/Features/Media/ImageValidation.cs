namespace DmOrder.Application.Features.Media;

/// <summary>
/// Validates uploaded images by their actual bytes.
///
/// A declared content type and a file extension are both attacker-controlled. Checking the leading
/// bytes is what stops an executable, a script, or an SVG full of JavaScript being stored and later
/// served from our own origin.
///
/// SVG is deliberately not supported: it is a document format that can carry script, and serving one
/// from the storefront's origin would be a stored XSS.
/// </summary>
public static class ImageValidation
{
    public static readonly IReadOnlyDictionary<string, string> AllowedContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp",
            ["image/avif"] = ".avif",
        };

    public static ImageInspection Inspect(ReadOnlySpan<byte> header)
    {
        // JPEG: FF D8 FF
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return new ImageInspection(true, "image/jpeg", ".jpg");
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (header.Length >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return new ImageInspection(true, "image/png", ".png");
        }

        // RIFF container: "RIFF" ....  "WEBP"
        if (header.Length >= 12
            && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
            && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P')
        {
            return new ImageInspection(true, "image/webp", ".webp");
        }

        // ISO-BMFF container with an AVIF brand: bytes 4..8 are "ftyp", then the major brand.
        if (header.Length >= 12
            && header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p'
            && header[8] == 'a' && header[9] == 'v' && header[10] == 'i'
            && (header[11] == 'f' || header[11] == 's'))
        {
            return new ImageInspection(true, "image/avif", ".avif");
        }

        return new ImageInspection(false, null, null);
    }

    /// <summary>Number of leading bytes <see cref="Inspect"/> needs.</summary>
    public const int HeaderLength = 16;
}

public readonly record struct ImageInspection(bool IsImage, string? ContentType, string? Extension);
