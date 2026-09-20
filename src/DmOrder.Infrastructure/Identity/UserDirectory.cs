using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Common.Models;
using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Identity;
using DmOrder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Infrastructure.Identity;

/// <summary>
/// Seller accounts for the platform admin, read straight from the Identity tables.
///
/// Everything here is restricted to the Seller role. That is not cosmetic: it is what stops the
/// admin API from being able to suspend an administrator, which would let one admin lock the others
/// — or themselves — out of the platform with no way back in short of a database edit.
/// </summary>
public sealed class UserDirectory(AppDbContext db) : IUserDirectory
{
    /// <summary>Ids of every user in the Seller role. The admin API never sees anyone else.</summary>
    private IQueryable<Guid> SellerUserIds =>
        from userRole in db.UserRoles
        join role in db.Roles on userRole.RoleId equals role.Id
        where role.Name == ApplicationRoles.Seller
        select userRole.UserId;

    public async Task<PagedResult<DirectoryUser>> ListSellersAsync(
        PageRequest page,
        string? search,
        bool? isActive,
        SortRequest sort,
        CancellationToken cancellationToken)
    {
        var sellers = db.Users.AsNoTracking().Where(u => SellerUserIds.Contains(u.Id));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = SearchPattern.Contains(search);

            // NormalizedEmail is already upper-cased by Identity, so matching goes through Email.
            sellers = sellers.Where(u =>
                EF.Functions.Like(u.DisplayName.ToLower(), pattern, SearchPattern.EscapeCharacter)
                || EF.Functions.Like(u.Email!.ToLower(), pattern, SearchPattern.EscapeCharacter));
        }

        if (isActive is { } active)
        {
            sellers = sellers.Where(u => u.IsActive == active);
        }

        // Ties break on Id so paging cannot skip or repeat an account.
        sellers = sort switch
        {
            { Field: "displayName", Descending: true } => sellers.OrderByDescending(u => u.DisplayName).ThenBy(u => u.Id),
            { Field: "displayName" } => sellers.OrderBy(u => u.DisplayName).ThenBy(u => u.Id),
            { Field: "email", Descending: true } => sellers.OrderByDescending(u => u.Email).ThenBy(u => u.Id),
            { Field: "email" } => sellers.OrderBy(u => u.Email).ThenBy(u => u.Id),
            { Descending: false } => sellers.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id),
            _ => sellers.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id),
        };

        return await sellers
            .Select(u => new DirectoryUser(u.Id, u.DisplayName, u.Email ?? string.Empty, u.IsActive, u.CreatedAt))
            .ToPagedResultAsync(page, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, DirectoryUser>> GetByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, DirectoryUser>();
        }

        return await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new DirectoryUser(u.Id, u.DisplayName, u.Email ?? string.Empty, u.IsActive, u.CreatedAt))
            .ToDictionaryAsync(u => u.Id, u => u, cancellationToken);
    }

    public async Task<(int Total, int Active)> CountSellersAsync(CancellationToken cancellationToken)
    {
        var sellers = db.Users.AsNoTracking().Where(u => SellerUserIds.Contains(u.Id));

        var total = await sellers.CountAsync(cancellationToken);
        var active = await sellers.CountAsync(u => u.IsActive, cancellationToken);

        return (total, active);
    }

    public async Task SetSellerActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        // Tracked, because this one writes.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        // An account that is not a Seller is reported as missing rather than refused, for the same
        // reason cross-tenant reads 404: the admin API should not confirm who the administrators are.
        if (user is null || !await SellerUserIds.ContainsAsync(userId, cancellationToken))
        {
            throw new NotFoundException("Seller", userId);
        }

        user.IsActive = isActive;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        // Suspending must end the session, not merely prevent the next sign-in. Access tokens are
        // short-lived and refresh re-checks IsActive, so revoking here closes the window.
        if (!isActive)
        {
            var tokens = await db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var token in tokens)
            {
                token.RevokedAt = DateTimeOffset.UtcNow;
                token.RevokedReason = RefreshTokenRevocationReasons.UserDeactivated;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
