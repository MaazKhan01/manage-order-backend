using System.Text.RegularExpressions;

namespace DmOrder.Domain.Stores;

/// <summary>
/// Rules for the slug in <c>yourapp.com/{slug}</c>.
///
/// Storefronts sit at the URL root, so a slug competes with every platform route. The reserved list
/// below is the authoritative defence — see docs/ADR/0007-storefront-slug-at-url-root.md.
/// </summary>
public static partial class StoreSlug
{
    public const int MinLength = 3;
    public const int MaxLength = 40;

    /// <summary>
    /// Every top-level route the platform owns or intends to own, plus the usual impersonation
    /// targets. Deliberately generous: a name kept back today costs nothing, while a name given away
    /// can never be reclaimed without breaking a seller's shared links.
    ///
    /// **Adding a top-level route to the frontend means adding it here.** A test asserts the two agree.
    /// </summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Platform routes, current and planned
        "api", "admin", "dashboard", "login", "logout", "register", "signup", "signin", "sign-in",
        "sign-up", "setup", "onboarding", "account", "settings", "profile", "search", "explore",
        "pricing", "plans", "billing", "checkout", "orders", "store", "stores", "shop", "shops",
        "help", "support", "contact", "about", "terms", "privacy", "legal", "docs", "blog", "status",
        "health", "home", "index", "new", "create", "edit", "delete",

        // Framework and infrastructure paths
        "_next", "static", "assets", "public", "media", "images", "img", "css", "js", "fonts",
        "favicon.ico", "robots.txt", "sitemap.xml", "well-known", ".well-known", "sw.js", "manifest",

        // Common subdomain-style names people expect to belong to the platform
        "www", "mail", "email", "ftp", "cdn", "app", "web", "server", "root", "system",

        // Impersonation targets
        "official", "verified", "security", "team", "staff", "moderator", "administrator",
    };

    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    public static SlugValidationResult Validate(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return SlugValidationResult.Invalid("A store address is required.");
        }

        if (slug.Length < MinLength)
        {
            return SlugValidationResult.Invalid($"Use at least {MinLength} characters.");
        }

        if (slug.Length > MaxLength)
        {
            return SlugValidationResult.Invalid($"Use at most {MaxLength} characters.");
        }

        // The pattern also rules out leading, trailing and doubled hyphens.
        if (!SlugPattern().IsMatch(slug))
        {
            return SlugValidationResult.Invalid(
                "Use lowercase letters, numbers and single hyphens between words.");
        }

        if (Reserved.Contains(slug))
        {
            return SlugValidationResult.Invalid("That address is not available.");
        }

        return SlugValidationResult.Valid();
    }

    public static bool IsReserved(string slug) => Reserved.Contains(slug);

    /// <summary>
    /// Best-effort suggestion from a store name, for pre-filling the field. The result is still
    /// validated like any other input — this is a convenience, not a trusted path.
    /// </summary>
    public static string Suggest(string storeName)
    {
        var lowered = storeName.Trim().ToLowerInvariant();
        var builder = new System.Text.StringBuilder(lowered.Length);

        foreach (var character in lowered)
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

        return builder.ToString().Trim('-');
    }
}

public readonly record struct SlugValidationResult(bool IsValid, string? Error)
{
    public static SlugValidationResult Valid() => new(true, null);

    public static SlugValidationResult Invalid(string error) => new(false, error);
}
