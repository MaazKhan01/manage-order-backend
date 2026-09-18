namespace DmOrder.Domain.Identity;

/// <summary>
/// The claim names this application issues and reads.
///
/// Short, explicit names rather than the long WS-* URIs, and no reliance on the framework's inbound or
/// outbound claim-type mapping — that mapping differs between handlers and has silently broken
/// authorization in more than one codebase. What is written here is exactly what appears in the token.
/// </summary>
public static class AppClaimTypes
{
    public const string UserId = "sub";
    public const string Email = "email";
    public const string Role = "role";

    /// <summary>
    /// The seller's tenant. Populated from the database at login or refresh, never from anything the
    /// client sends.
    /// </summary>
    public const string StoreId = "store_id";
}
