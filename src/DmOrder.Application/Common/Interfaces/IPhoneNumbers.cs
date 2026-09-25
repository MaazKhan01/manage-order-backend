namespace DmOrder.Application.Common.Interfaces;

/// <summary>
/// The result of trying to make sense of a phone number someone typed.
/// </summary>
/// <param name="IsValid">Whether it is a real, dialable number for its region.</param>
/// <param name="E164">
/// The canonical form, e.g. <c>+923001234567</c>. Null when the number could not be parsed.
/// </param>
/// <param name="RegionCode">The ISO 3166-1 alpha-2 region it belongs to, when known.</param>
public readonly record struct PhoneParseResult(bool IsValid, string? E164, string? RegionCode);

/// <summary>
/// Parses and normalises phone numbers.
///
/// Phone numbers cannot be validated by counting digits. "03001234567" is eleven digits and valid in
/// Pakistan; eleven digits is not a valid mobile number in most of Europe. A length check either
/// rejects real customers or accepts nonsense, and usually both.
///
/// So this wraps Google's libphonenumber, which knows the actual numbering plans. It lives in
/// Infrastructure because Domain references nothing and this needs a data set that changes several
/// times a year.
///
/// **Everything is stored in E.164.** One customer with one number must not become two records
/// because they typed it differently the second time.
/// </summary>
public interface IPhoneNumbers
{
    /// <summary>
    /// Parses a number, optionally with a default region for numbers written locally
    /// (e.g. "0300 1234567" needs to know it is Pakistani; "+923001234567" does not).
    /// </summary>
    PhoneParseResult Parse(string? input, string? defaultRegion = null);

    /// <summary>
    /// The E.164 form, or the trimmed input when it could not be parsed.
    ///
    /// Used where a number has already been validated and the caller needs the canonical string.
    /// Never silently drops data: an unparseable number is kept as typed rather than becoming null,
    /// because a seller's existing customer records must survive a library update that changes its
    /// mind about a numbering plan.
    /// </summary>
    string Normalise(string input, string? defaultRegion = null);
}
