namespace DmOrder.Application.Common.Interfaces;

/// <summary>
/// The authenticated caller, resolved from the request. Implemented in the API layer because
/// identity arrives over HTTP, but consumed by use cases that must not know about HTTP.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    string? Email { get; }

    bool IsAuthenticated { get; }

    bool IsInRole(string role);

    /// <summary>Returns the user id, or throws if the request is anonymous.</summary>
    Guid RequireUserId();
}
