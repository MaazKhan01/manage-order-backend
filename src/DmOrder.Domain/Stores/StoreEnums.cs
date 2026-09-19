namespace DmOrder.Domain.Stores;

/// <summary>
/// Theme choices are closed sets, not free text.
///
/// These values end up in CSS custom properties and class names on a public page. A closed enum means
/// a seller cannot inject anything into a stylesheet, and it keeps the storefront looking designed
/// rather than assembled.
/// </summary>
public enum StoreFontChoice
{
    /// <summary>Neutral sans — the default. Reads well for any category.</summary>
    Sans = 0,

    /// <summary>Serif — suits jewellery, tailoring, formal wear, patisserie.</summary>
    Serif = 1,

    /// <summary>Rounded sans — suits bakeries, florists, gifts, handmade goods.</summary>
    Rounded = 2,
}

public enum StoreButtonStyle
{
    Rounded = 0,
    Pill = 1,
    Square = 2,
}

/// <summary>How products are arranged on the storefront. Not a page builder — a small set of layouts.</summary>
public enum StoreLayoutVariant
{
    /// <summary>Two-column card grid. The safe default for most catalogues.</summary>
    Grid = 0,

    /// <summary>Single-column rows with a thumbnail. Better when descriptions matter more than photos.</summary>
    List = 1,

    /// <summary>Large edge-to-edge imagery. For sellers whose product photography is the pitch.</summary>
    Showcase = 2,
}

public static class SupportedCurrencies
{
    /// <summary>
    /// A curated list rather than every ISO 4217 code. It drives a dropdown and is validated on the
    /// server, so an unfamiliar code is a deliberate addition rather than a typo that reaches a
    /// storefront. Extend as real sellers need it.
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "PKR", "INR", "BDT", "LKR", "NPR", "AED", "SAR", "QAR", "KWD", "OMR", "BHD",
        "USD", "EUR", "GBP", "CAD", "AUD", "NZD", "SGD", "MYR", "IDR", "PHP", "THB",
        "TRY", "EGP", "NGN", "KES", "ZAR", "GHS", "MAD",
    };

    public const string Default = "PKR";

    public static bool IsSupported(string? code) => code is not null && All.Contains(code);
}
