using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Stores;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Stores;

public sealed class UpdateStoreProfileHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage)
{
    public async Task<StoreDetailResponse> HandleAsync(
        UpdateStoreProfileRequest request,
        CancellationToken cancellationToken)
    {
        var store = await StoreLoader.RequireOwnStoreAsync(db, currentUser, cancellationToken);

        store.UpdateProfile(
            request.Name,
            request.Description,
            request.ContactPhone,
            request.WhatsApp,
            request.ContactEmail,
            request.InstagramUrl,
            request.FacebookUrl,
            request.TiktokUrl,
            request.AddressText,
            request.City,
            request.Country,
            request.Currency);

        store.UpdateSeo(request.SeoTitle, request.SeoDescription);

        await db.SaveChangesAsync(cancellationToken);

        var mediaUrls = await MediaUrlResolver.ForStoreAsync(db, storage, store, cancellationToken);
        return store.ToDetail(mediaUrls);
    }
}

public sealed class ChangeStoreSlugHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage)
{
    public async Task<StoreDetailResponse> HandleAsync(
        ChangeStoreSlugRequest request,
        CancellationToken cancellationToken)
    {
        var store = await StoreLoader.RequireOwnStoreAsync(db, currentUser, cancellationToken);
        var slug = request.Slug.Trim().ToLowerInvariant();

        if (slug != store.Slug
            && await db.Stores.AnyAsync(s => s.Slug == slug && s.Id != store.Id, cancellationToken))
        {
            throw new ConflictException("That address is already taken.");
        }

        // Changing a slug breaks every link the seller has already shared. The API allows it because
        // early mistakes are common, but the UI warns before submitting.
        store.ChangeSlug(slug);

        await db.SaveChangesAsync(cancellationToken);

        var mediaUrls = await MediaUrlResolver.ForStoreAsync(db, storage, store, cancellationToken);
        return store.ToDetail(mediaUrls);
    }
}

public sealed class UpdateStoreProfileValidator : AbstractValidator<UpdateStoreProfileRequest>
{
    public UpdateStoreProfileValidator(IPhoneNumbers phones)
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("A store name is required.").MaximumLength(120);
        RuleFor(x => x.Description).MaximumLength(2000);
        // Both are optional, so the rule only applies once something has been typed. A seller's own
        // numbers are checked against the real numbering plan for the same reason a customer's are:
        // a wrong number in the storefront footer is a lost order.
        RuleFor(x => x.ContactPhone)
            .MaximumLength(32)
            .Must(phone => phones.Parse(phone).IsValid)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactPhone))
            .WithMessage("Enter a valid phone number, including the country code.");

        RuleFor(x => x.WhatsApp)
            .MaximumLength(32)
            .Must(phone => phones.Parse(phone).IsValid)
            .When(x => !string.IsNullOrWhiteSpace(x.WhatsApp))
            .WithMessage("Enter a valid WhatsApp number, including the country code.");

        RuleFor(x => x.ContactEmail)
            .MaximumLength(256)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.ContactEmail))
            .WithMessage("Enter a valid email address.");

        RuleFor(x => x.InstagramUrl).Must(BeHttpUrl).WithMessage("Enter a valid link starting with https://");
        RuleFor(x => x.FacebookUrl).Must(BeHttpUrl).WithMessage("Enter a valid link starting with https://");
        RuleFor(x => x.TiktokUrl).Must(BeHttpUrl).WithMessage("Enter a valid link starting with https://");

        RuleFor(x => x.AddressText).MaximumLength(500);
        RuleFor(x => x.City).MaximumLength(120);
        RuleFor(x => x.Country).MaximumLength(120);
        RuleFor(x => x.SeoTitle).MaximumLength(120);
        RuleFor(x => x.SeoDescription).MaximumLength(320);

        RuleFor(x => x.Currency)
            .Must(currency => currency is null || SupportedCurrencies.IsSupported(currency))
            .WithMessage("That currency is not supported yet.");
    }

    /// <summary>
    /// Social links are rendered as anchors on a public page, so the scheme must be http(s). Without
    /// this, a "link" of <c>javascript:…</c> would be stored and clicked by customers.
    /// </summary>
    private static bool BeHttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Length <= 500
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}

public sealed class ChangeStoreSlugValidator : AbstractValidator<ChangeStoreSlugRequest>
{
    public ChangeStoreSlugValidator()
    {
        RuleFor(x => x.Slug)
            .NotEmpty().WithMessage("A store address is required.")
            .Must(slug => StoreSlug.Validate(slug).IsValid)
            .WithMessage(request => StoreSlug.Validate(request.Slug).Error ?? "That address is not available.");
    }
}
