using System.Security.Cryptography;
using DmOrder.Domain.Exceptions;

namespace DmOrder.Domain.Orders;

/// <summary>
/// The customer-facing order reference, e.g. <c>DM-2026-K4P7QX</c>.
///
/// This is the only order identifier a customer ever sees or types. It is distinct from two things
/// already on <see cref="Order"/>:
///
///   • <c>Id</c> is the API identifier and never leaves the seller's side.
///   • <c>OrderNumber</c> is the seller's own running count — "#12" — which restarts per store and
///     is therefore useless as a platform-wide lookup key.
///
/// The suffix is **random, not sequential**. A running number in a public reference lets anyone
/// count a store's orders, guess their neighbours, and estimate a business's volume from two
/// receipts. Tracking additionally requires the phone number the order was placed with, so a
/// guessed reference on its own reveals nothing — the randomness is what stops bulk enumeration
/// being worth attempting at all.
/// </summary>
public static class OrderReference
{
    /// <summary>
    /// Crockford-style alphabet: no I, L, O or U.
    ///
    /// I/1, L/1 and O/0 are the pairs people mistype when reading a code off a screen and saying it
    /// down the phone, and U is dropped so the generator cannot spell anything unfortunate.
    /// </summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private const int SuffixLength = 6;

    /// <summary>The default prefix. Configurable, because the platform can be renamed.</summary>
    public const string DefaultPrefix = "DM";

    /// <summary>
    /// 32^6 ≈ 1.07 billion suffixes per prefix-year. Collisions are rare enough that the caller's
    /// retry-on-unique-violation loop is a safety net rather than a routine path.
    /// </summary>
    public static string Generate(string prefix, int year)
    {
        var normalisedPrefix = NormalisePrefix(prefix);
        var suffix = new char[SuffixLength];

        for (var i = 0; i < SuffixLength; i++)
        {
            // Cryptographic randomness, not Random: a predictable sequence would reintroduce exactly
            // the guessability the random suffix exists to remove.
            suffix[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"{normalisedPrefix}-{year:D4}-{new string(suffix)}";
    }

    /// <summary>
    /// Tidies what a customer typed into what is stored.
    ///
    /// People paste references with stray spaces, type lower case, and leave the hyphens out. All of
    /// that should find their order rather than telling them they got it wrong — and the letters that
    /// are not in the alphabet are exactly the ones they might have confused, so O becomes 0 and I
    /// becomes 1 rather than failing.
    /// </summary>
    public static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = new string([.. value.Where(c => !char.IsWhiteSpace(c) && c != '-')])
            .ToUpperInvariant()
            .Replace('O', '0')
            .Replace('I', '1')
            .Replace('L', '1');

        // Re-insert the hyphens from the fixed shape: PREFIX (2+) - YEAR (4) - SUFFIX (6).
        if (cleaned.Length < 4 + SuffixLength + 1)
        {
            return cleaned;
        }

        var prefixLength = cleaned.Length - 4 - SuffixLength;
        return $"{cleaned[..prefixLength]}-{cleaned.Substring(prefixLength, 4)}-{cleaned[^SuffixLength..]}";
    }

    /// <summary>True when the value has the right shape. Says nothing about whether it exists.</summary>
    public static bool IsWellFormed(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Split('-');
        if (parts.Length != 3) return false;

        var (prefix, year, suffix) = (parts[0], parts[1], parts[2]);

        return prefix.Length is >= 2 and <= 8
               && prefix.All(char.IsAsciiLetterUpper)
               && year.Length == 4
               && year.All(char.IsAsciiDigit)
               && suffix.Length == SuffixLength
               && suffix.All(c => Alphabet.Contains(c));
    }

    private static string NormalisePrefix(string prefix)
    {
        var cleaned = new string([.. (prefix ?? string.Empty).Where(char.IsAsciiLetter)]).ToUpperInvariant();

        if (cleaned.Length is < 2 or > 8)
        {
            throw new BusinessRuleException(
                "The order reference prefix must be 2 to 8 letters.");
        }

        return cleaned;
    }
}
