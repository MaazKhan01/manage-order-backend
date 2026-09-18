namespace DmOrder.Api.Endpoints;

/// <summary>
/// Named policies instead of magic strings at call sites. Adding a role later (e.g. StoreStaff) means
/// adding a policy here, not editing every endpoint.
/// </summary>
public static class AuthorizationPolicies
{
    public const string Seller = nameof(Seller);
    public const string Admin = nameof(Admin);
}

public static class ApplicationRoles
{
    public const string Seller = nameof(Seller);
    public const string Admin = nameof(Admin);

    public static readonly IReadOnlyList<string> All = [Seller, Admin];
}
