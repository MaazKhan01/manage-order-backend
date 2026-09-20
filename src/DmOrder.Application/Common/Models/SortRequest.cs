namespace DmOrder.Application.Common.Models;

/// <summary>
/// Sorting input for a list endpoint, as <c>field</c> or <c>-field</c> for descending.
///
/// Sorting has to happen in the database, not in the client. A table that sorts the page it was
/// handed is sorting twenty arbitrary rows and calling it "sorted by newest", which is worse than
/// offering no sorting at all.
///
/// The field is checked against the endpoint's whitelist during parsing, so a handler can never
/// receive a column it does not know how to sort by.
/// </summary>
public readonly record struct SortRequest(string Field, bool Descending)
{
    /// <summary>
    /// Reads a sort parameter, falling back to the endpoint's default when the field is absent or not
    /// one this endpoint sorts by.
    ///
    /// The fallback replaces the *whole* sort, direction included. Keeping the caller's direction and
    /// only replacing the field produces a list that is ordered by something the caller never asked
    /// for, in a direction they did — which reads as a bug to everyone who sees it.
    /// </summary>
    public static SortRequest Parse(
        string? sort,
        IReadOnlyCollection<string> allowedFields,
        string defaultField,
        bool defaultDescending = true)
    {
        var fallback = new SortRequest(defaultField, defaultDescending);

        if (string.IsNullOrWhiteSpace(sort))
        {
            return fallback;
        }

        var trimmed = sort.Trim();
        var descending = trimmed.StartsWith('-');
        var field = descending ? trimmed[1..] : trimmed;

        var match = allowedFields.FirstOrDefault(f => string.Equals(f, field, StringComparison.OrdinalIgnoreCase));

        return match is null ? fallback : new SortRequest(match, descending);
    }
}
