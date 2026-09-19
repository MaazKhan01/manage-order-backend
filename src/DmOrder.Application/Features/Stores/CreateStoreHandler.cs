using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Stores;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DmOrder.Application.Features.Stores;

public sealed class CreateStoreHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage,
    ILogger<CreateStoreHandler> logger)
{
    public async Task<StoreDetailResponse> HandleAsync(
        CreateStoreRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        // One store per seller in V1. Checked here for a clean message; the unique index on
        // OwnerUserId is what actually guarantees it under concurrency.
        if (await db.Stores.AnyAsync(s => s.OwnerUserId == userId, cancellationToken))
        {
            throw new ConflictException("You already have a store.");
        }

        var slug = request.Slug.Trim().ToLowerInvariant();

        if (await db.Stores.AnyAsync(s => s.Slug == slug, cancellationToken))
        {
            throw new ConflictException("That address is already taken.");
        }

        var store = Store.Create(userId, request.Name, slug, request.Currency);

        db.Stores.Add(store);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Two sellers can pass the check above simultaneously. The index rejects the loser, and a
            // 409 is the honest answer rather than a 500.
            logger.LogInformation("Store creation lost a race on a unique constraint");
            throw new ConflictException("That address is already taken.");
        }

        logger.LogInformation("Store {StoreId} created with slug {Slug}", store.Id, store.Slug);

        var mediaUrls = await MediaUrlResolver.ForStoreAsync(db, storage, store, cancellationToken);
        return store.ToDetail(mediaUrls);
    }

    /// <summary>
    /// SQLSTATE 23505 is "unique_violation". Read from <see cref="System.Data.Common.DbException"/>
    /// rather than a provider type, so Application stays free of Npgsql, and matched exactly rather
    /// than by sniffing the message text.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is System.Data.Common.DbException { SqlState: "23505" };
}

public sealed class CreateStoreValidator : AbstractValidator<CreateStoreRequest>
{
    public CreateStoreValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("A store name is required.")
            .MaximumLength(120);

        RuleFor(x => x.Slug)
            .NotEmpty().WithMessage("A store address is required.")
            .Must(slug => StoreSlug.Validate(slug).IsValid)
            .WithMessage(request => StoreSlug.Validate(request.Slug).Error ?? "That address is not available.");

        RuleFor(x => x.Currency)
            .Must(currency => currency is null || SupportedCurrencies.IsSupported(currency))
            .WithMessage("That currency is not supported yet.");
    }
}
