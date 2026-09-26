using System.Globalization;

namespace DmOrder.Application.Common;

/// <summary>
/// Turns a store's free-text country into an ISO 3166-1 alpha-2 region code.
///
/// A store's country is whatever the seller typed - "Pakistan", "pakistan", "PK". libphonenumber
/// needs "PK", and only uses it to read a number written locally; a number already in E.164 does
/// not need it at all. So an unresolvable country is not an error here, it just means local numbers
/// cannot be interpreted and will be reported as missing rather than guessed at.
/// </summary>
public static class CountryRegions
{
    private static readonly Lazy<Dictionary<string, string>> ByName = new(Build);

    public static string? Resolve(string? country)
    {
        var trimmed = country?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        // Already a region code. Checked against the real list so "XX" does not get through.
        if (trimmed.Length == 2 && trimmed.All(char.IsAsciiLetter))
        {
            var upper = trimmed.ToUpperInvariant();
            return ByName.Value.ContainsValue(upper) ? upper : null;
        }

        return ByName.Value.GetValueOrDefault(trimmed.ToLowerInvariant());
    }

    private static Dictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            RegionInfo region;
            try
            {
                region = new RegionInfo(culture.Name);
            }
            catch (ArgumentException)
            {
                // Some cultures on some platforms have no region. Nothing to learn from them.
                continue;
            }

            // English and native names both, so "Pakistan" and "Deutschland" each resolve.
            map.TryAdd(region.EnglishName.ToLowerInvariant(), region.TwoLetterISORegionName);
            map.TryAdd(region.NativeName.ToLowerInvariant(), region.TwoLetterISORegionName);
        }

        return map;
    }
}
