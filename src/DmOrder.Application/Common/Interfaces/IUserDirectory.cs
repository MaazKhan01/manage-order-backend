using DmOrder.Application.Common.Models;

namespace DmOrder.Application.Common.Interfaces;

/// <summary>One account, as the platform admin needs to see it.</summary>
public sealed record DirectoryUser(
    Guid Id,
    string DisplayName,
    string Email,
    bool IsActive,
    DateTimeOffset CreatedAt);

/// <summary>
/// Read and write access to seller accounts, for the platform admin.
///
/// Identity lives in Infrastructure (ADR 0008), so Application asks for accounts through this rather
/// than reaching for <c>UserManager</c>. <see cref="IUserDisplayNameLookup"/> answers "what is this
/// person called"; this answers the admin's questions, which need the email, the account state and
/// paging.
/// </summary>
public interface IUserDirectory
{
    /// <summary>
    /// Accounts in the Seller role, newest first. <paramref name="search"/> matches the display name
    /// or the email.
    /// </summary>
    Task<PagedResult<DirectoryUser>> ListSellersAsync(
        PageRequest page,
        string? search,
        bool? isActive,
        SortRequest sort,
        CancellationToken cancellationToken);

    /// <summary>
    /// The given accounts, keyed by id, for joining onto rows that carry only an owner id. Ids that
    /// no longer exist are simply absent.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DirectoryUser>> GetByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);

    /// <summary>Seller accounts, total and active.</summary>
    Task<(int Total, int Active)> CountSellersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Suspends or restores a seller account. A suspended account keeps its data but can no longer
    /// sign in or refresh a session.
    ///
    /// Implementations must refuse to touch an account that is not a Seller, so this can never be
    /// used to lock an administrator — including the caller — out of the platform.
    /// </summary>
    Task SetSellerActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken);
}
