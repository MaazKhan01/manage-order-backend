namespace DmOrder.Application.Common.Models;

/// <summary>
/// Builds SQL LIKE patterns from user-supplied search text.
///
/// Without escaping, a search for "50%" matches everything and "_" matches any character — not a
/// security hole, since EF still parameterises the value, but a silently wrong result. Callers must
/// pass the same escape character to <c>EF.Functions.Like</c>, which is why it is exposed here.
///
/// <c>EF.Functions.ILike</c> would be neater but is Npgsql-only, and Application does not reference a
/// database provider. Lower-casing both sides is the portable equivalent.
/// </summary>
public static class SearchPattern
{
    public const string EscapeCharacter = "\\";

    /// <summary>A case-insensitive "contains" pattern. Compare against a lower-cased column.</summary>
    public static string Contains(string term) => $"%{Escape(term.Trim().ToLowerInvariant())}%";

    /// <summary>
    /// A pattern that matches a stored E.164 number against a phone number typed any way at all.
    ///
    /// Numbers are stored canonically (+923002222222) but a seller searches for the number the way
    /// they know it - "0300 222 2222". Neither a prefix nor a contains match connects those two, so
    /// this compares the trailing digits instead: the leading zero is a national-dialling artefact
    /// and the country code is only present on one side.
    ///
    /// Returns null when the term is too short to be a phone number, in which case the caller should
    /// not attempt a phone match at all - two digits would match half the customer list.
    /// </summary>
    public static string? PhoneSuffix(string term)
    {
        var digits = new string([.. term.Where(char.IsAsciiDigit)]).TrimStart('0');

        // Six is long enough to be meaningful and short enough to allow "the last six digits", which
        // is how people actually half-remember a number.
        return digits.Length < 6 ? null : $"%{digits}";
    }

    private static string Escape(string term) =>
        term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
