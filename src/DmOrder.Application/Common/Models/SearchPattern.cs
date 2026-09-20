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

    private static string Escape(string term) =>
        term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
