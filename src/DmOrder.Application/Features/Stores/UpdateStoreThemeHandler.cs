using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Media;
using DmOrder.Domain.Stores;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Stores;

public sealed class UpdateStoreThemeHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage)
{
    public async Task<StoreDetailResponse> HandleAsync(
        UpdateStoreThemeRequest request,
        CancellationToken cancellationToken)
    {
        var store = await StoreLoader.RequireOwnStoreAsync(db, currentUser, cancellationToken);

        store.Theme.Update(
            request.PrimaryColor,
            request.AccentColor,
            request.BackgroundColor,
            request.FontChoice,
            request.ButtonStyle,
            request.LayoutVariant);

        await db.SaveChangesAsync(cancellationToken);

        var mediaUrls = await MediaUrlResolver.ForStoreAsync(db, storage, store, cancellationToken);
        return store.ToDetail(mediaUrls);
    }
}

/// <summary>
/// Points the store's logo, cover or background at an uploaded image, or clears it.
/// </summary>
public sealed class SetStoreImageHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage)
{
    public async Task<StoreDetailResponse> HandleAsync(
        MediaPurpose purpose,
        SetStoreImageRequest request,
        CancellationToken cancellationToken)
    {
        var store = await StoreLoader.RequireOwnStoreAsync(db, currentUser, cancellationToken);

        if (request.MediaId is { } mediaId)
        {
            // The media id comes from the client, so it must be proven to belong to this store before
            // it is attached. Without this check a seller could point their logo at another seller's
            // upload and read it through their own storefront.
            var belongsToStore = await db.MediaAssets
                .AnyAsync(m => m.Id == mediaId && m.StoreId == store.Id, cancellationToken);

            if (!belongsToStore)
            {
                throw new NotFoundException("Image", mediaId);
            }
        }

        switch (purpose)
        {
            case MediaPurpose.StoreLogo:
                store.SetLogo(request.MediaId);
                break;
            case MediaPurpose.StoreCover:
                store.SetCover(request.MediaId);
                break;
            case MediaPurpose.StoreBackground:
                store.Theme.SetBackgroundMedia(request.MediaId);
                break;
            default:
                throw new BusinessRuleException($"{purpose} is not a store image.");
        }

        await db.SaveChangesAsync(cancellationToken);

        var mediaUrls = await MediaUrlResolver.ForStoreAsync(db, storage, store, cancellationToken);
        return store.ToDetail(mediaUrls);
    }
}

public sealed class UpdateStoreThemeValidator : AbstractValidator<UpdateStoreThemeRequest>
{
    public UpdateStoreThemeValidator()
    {
        RuleFor(x => x.PrimaryColor).Must(BeHexColor).WithMessage("Use a colour like #1A2B3C.");
        RuleFor(x => x.AccentColor).Must(BeHexColor).WithMessage("Use a colour like #1A2B3C.");
        RuleFor(x => x.BackgroundColor).Must(BeHexColor).WithMessage("Use a colour like #1A2B3C.");

        // IsInEnum rejects a cast from an arbitrary integer in the request body, which would otherwise
        // reach the renderer as an unknown variant.
        RuleFor(x => x.FontChoice).IsInEnum().WithMessage("Choose one of the available fonts.");
        RuleFor(x => x.ButtonStyle).IsInEnum().WithMessage("Choose one of the available button styles.");
        RuleFor(x => x.LayoutVariant).IsInEnum().WithMessage("Choose one of the available layouts.");
    }

    private static bool BeHexColor(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length == 7
        && value[0] == '#'
        && value[1..].All(char.IsAsciiHexDigit);
}
