using DmOrder.Application.Common.Interfaces;
using DmOrder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Infrastructure.Identity;

public sealed class UserDisplayNameLookup(AppDbContext db) : IUserDisplayNameLookup
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        // Only the display name is selected — nothing else about a user belongs in an order timeline.
        return await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
    }
}
