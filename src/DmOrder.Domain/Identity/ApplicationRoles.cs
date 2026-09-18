namespace DmOrder.Domain.Identity;

/// <summary>
/// The roles the platform recognises. Plain constants with no framework dependency, so every layer can
/// refer to the same names instead of scattering string literals.
///
/// Adding a role (e.g. StoreStaff) means adding it here and adding a policy in the API — not editing
/// endpoints one by one.
/// </summary>
public static class ApplicationRoles
{
    /// <summary>Owns exactly one store and can only ever see that store's data.</summary>
    public const string Seller = nameof(Seller);

    /// <summary>Platform owner. Cross-tenant by definition, and therefore never the default.</summary>
    public const string Admin = nameof(Admin);

    public static readonly IReadOnlyList<string> All = [Seller, Admin];
}
