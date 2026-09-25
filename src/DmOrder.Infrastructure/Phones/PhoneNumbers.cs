using DmOrder.Application.Common.Interfaces;
using PhoneNumbers;

namespace DmOrder.Infrastructure.Phones;

/// <summary>
/// Google's libphonenumber, behind the Application interface.
///
/// The library knows every numbering plan in the world and is updated several times a year, which is
/// the entire reason not to hand-roll this. Sellers on this platform are international by design —
/// the same rule must accept a Karachi mobile, a London landline and a Lagos number without the
/// product having an opinion about which country matters.
/// </summary>
public sealed class LibPhoneNumbers : IPhoneNumbers
{
    // Thread-safe and expensive to build, so it is shared. This is the library's own guidance.
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    public PhoneParseResult Parse(string? input, string? defaultRegion = null)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new PhoneParseResult(false, null, null);
        }

        try
        {
            // A null region is fine for numbers written in international form; it is only needed to
            // interpret a leading 0.
            var parsed = Util.Parse(input.Trim(), NormaliseRegion(defaultRegion));

            // IsValidNumber checks the number against the plan for its region — not just its shape.
            var isValid = Util.IsValidNumber(parsed);

            return new PhoneParseResult(
                isValid,
                Util.Format(parsed, PhoneNumberFormat.E164),
                Util.GetRegionCodeForNumber(parsed));
        }
        catch (NumberParseException)
        {
            // Not a number we can make sense of. The caller turns this into a message for a person;
            // the library's own exception text is developer-facing and not worth surfacing.
            return new PhoneParseResult(false, null, null);
        }
    }

    public string Normalise(string input, string? defaultRegion = null)
    {
        var result = Parse(input, defaultRegion);

        // Deliberately falls back to the input rather than null. A stored number that this library
        // later stops recognising must not be erased by a save that happened to touch the row.
        return result.E164 ?? input.Trim();
    }

    /// <summary>
    /// libphonenumber expects an upper-case two-letter region, and treats anything else as absent.
    /// </summary>
    private static string? NormaliseRegion(string? region) =>
        string.IsNullOrWhiteSpace(region) || region.Trim().Length != 2
            ? null
            : region.Trim().ToUpperInvariant();
}
