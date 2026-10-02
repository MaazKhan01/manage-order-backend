namespace DmOrder.Infrastructure.Storage;

/// <summary>
/// How a stored object is named, shared by every provider.
///
/// Both implementations have to agree about this exactly: a key written by one must be readable by
/// the other, because switching providers must not orphan what is already stored, and the rules that
/// keep a hostile key from escaping its prefix should not be two separate pieces of code that can
/// drift apart.
/// </summary>
internal static class StorageKeys
{
    /// <summary>
    /// A new key under <paramref name="keyPrefix"/>.
    ///
    /// The stored name is generated, never taken from the upload. A filename is attacker-controlled
    /// data: it can carry a path, a second extension, or a name that means something to a filesystem.
    /// Only the extension survives, lowercased, and only because the served content type depends on
    /// it.
    /// </summary>
    public static string Build(string keyPrefix, string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var safeExtension = string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.ToLowerInvariant();

        return $"{keyPrefix.Trim('/')}/{Guid.CreateVersion7():n}{safeExtension}";
    }

    /// <summary>
    /// Rejects a key that is not one we generated.
    ///
    /// Keys come back from the database, so this is defence in depth rather than the first line.
    /// It matters most for the object-storage provider: S3 keys are opaque strings, so "../" is not
    /// special to them and a traversal attempt would not fail on its own - it would simply read or
    /// delete a different object than intended.
    /// </summary>
    public static void Validate(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey)
            || storageKey.Contains("..", StringComparison.Ordinal)
            || storageKey.StartsWith('/')
            || storageKey.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to use a storage key that was not generated here.");
        }
    }
}
