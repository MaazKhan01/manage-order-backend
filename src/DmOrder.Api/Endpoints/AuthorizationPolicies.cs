namespace DmOrder.Api.Endpoints;

/// <summary>
/// Named policies instead of magic strings at call sites. Adding a role later means adding a policy
/// here, not editing every endpoint. The role names themselves live in
/// <see cref="DmOrder.Domain.Identity.ApplicationRoles"/>.
/// </summary>
public static class AuthorizationPolicies
{
    public const string Seller = nameof(Seller);
    public const string Admin = nameof(Admin);
}
