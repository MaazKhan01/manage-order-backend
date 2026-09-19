using System.Text.RegularExpressions;

namespace DmOrder.Domain.Catalogue;

/// <summary>
/// Slug rules for categories and products.
///
/// Unlike <see cref="Stores.StoreSlug"/> there is no reserved list: these live under a store's own
/// path (<c>/{storeSlug}/{productSlug}</c>) and so cannot shadow a platform route.
/// </summary>
public static partial class CatalogueSlug
{
    public const int MinLength = 1;
    public const int MaxLength = 80;

    public const string Requirements =
        "Use lowercase letters, numbers and single hyphens between words.";

    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static string Normalise(string? slug) =>
        (slug ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValid(string? slug) =>
        !string.IsNullOrWhiteSpace(slug)
        && slug.Length >= MinLength
        && slug.Length <= MaxLength
        && Pattern().IsMatch(slug);

    /// <summary>
    /// Derives a slug from a name. Returns an empty string when nothing usable remains — callers must
    /// still validate, rather than assume this produced something.
    /// </summary>
    public static string Suggest(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length);

        foreach (var character in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var trimmed = builder.ToString().Trim('-');

        // Truncate first, then trim again: cutting at MaxLength can leave a trailing hyphen, which is
        // not a valid slug.
        return trimmed.Length <= MaxLength
            ? trimmed
            : trimmed[..MaxLength].TrimEnd('-');
    }
}
