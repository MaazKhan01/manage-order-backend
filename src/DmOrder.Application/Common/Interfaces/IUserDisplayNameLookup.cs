namespace DmOrder.Application.Common.Interfaces;

/// <summary>
/// Resolves user ids to display names.
///
/// Order history and notes record *who* acted, and a timeline showing raw GUIDs is useless. Identity
/// lives in Infrastructure (ADR 0008), so Application asks for names through this rather than
/// reaching for UserManager.
/// </summary>
public interface IUserDisplayNameLookup
{
    /// <summary>
    /// Names for the given users, keyed by id. Ids that no longer exist are simply absent — a
    /// deleted account must not break an order that references it.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);
}
