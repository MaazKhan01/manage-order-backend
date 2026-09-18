namespace DmOrder.Application.Common.Interfaces;

/// <summary>
/// The persistence surface available to use cases. Concrete DbSets are added here as entities are
/// introduced, so Application can project straight into DTOs without a repository per entity.
/// See docs/ADR/0002-efcore-in-application.md for why this is preferred over generic repositories.
/// </summary>
public interface IAppDbContext
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
