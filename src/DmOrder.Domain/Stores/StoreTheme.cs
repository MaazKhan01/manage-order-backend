using System.Text.RegularExpressions;
using DmOrder.Domain.Exceptions;

namespace DmOrder.Domain.Stores;

/// <summary>
/// A store's appearance, as data.
///
/// The public renderer reads these values; there is no per-seller frontend code and no page builder.
/// Colours are validated as strict hex because they are injected into CSS custom properties — an
/// unvalidated value there is a stylesheet injection.
/// </summary>
public sealed partial class StoreTheme
{
    private StoreTheme() { }

    public Guid StoreId { get; private set; }

    public string PrimaryColor { get; private set; } = DefaultPrimary;

    public string AccentColor { get; private set; } = DefaultAccent;

    public string BackgroundColor { get; private set; } = DefaultBackground;

    public StoreFontChoice FontChoice { get; private set; } = StoreFontChoice.Sans;

    public StoreButtonStyle ButtonStyle { get; private set; } = StoreButtonStyle.Rounded;

    public StoreLayoutVariant LayoutVariant { get; private set; } = StoreLayoutVariant.Grid;

    public Guid? BackgroundMediaId { get; private set; }

    // A restrained, neutral default that suits clothing, cakes, flowers, jewellery and gifts equally.
    // Nothing here assumes a product category.
    public const string DefaultPrimary = "#111827";
    public const string DefaultAccent = "#0F766E";
    public const string DefaultBackground = "#FFFFFF";

    public static StoreTheme CreateDefault(Guid storeId) => new() { StoreId = storeId };

    public void Update(
        string primaryColor,
        string accentColor,
        string backgroundColor,
        StoreFontChoice fontChoice,
        StoreButtonStyle buttonStyle,
        StoreLayoutVariant layoutVariant)
    {
        PrimaryColor = RequireHexColor(primaryColor, nameof(primaryColor));
        AccentColor = RequireHexColor(accentColor, nameof(accentColor));
        BackgroundColor = RequireHexColor(backgroundColor, nameof(backgroundColor));

        // Enum.IsDefined guards against a cast from an arbitrary integer in a request body.
        FontChoice = Require(fontChoice);
        ButtonStyle = Require(buttonStyle);
        LayoutVariant = Require(layoutVariant);
    }

    public void SetBackgroundMedia(Guid? mediaId) => BackgroundMediaId = mediaId;

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();

    private static string RequireHexColor(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || !HexColor().IsMatch(value.Trim()))
        {
            throw new BusinessRuleException($"{field} must be a colour like #1A2B3C.");
        }

        return value.Trim().ToUpperInvariant();
    }

    private static TEnum Require<TEnum>(TEnum value) where TEnum : struct, Enum =>
        Enum.IsDefined(value)
            ? value
            : throw new BusinessRuleException($"'{value}' is not a valid {typeof(TEnum).Name}.");
}
