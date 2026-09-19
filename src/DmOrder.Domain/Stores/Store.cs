using DmOrder.Domain.Common;
using DmOrder.Domain.Exceptions;

namespace DmOrder.Domain.Stores;

/// <summary>
/// A seller's store — the tenant. Everything a seller owns hangs off this.
///
/// One store per seller in V1, enforced by a unique index on <see cref="OwnerUserId"/>. The
/// relationship is modelled as one-to-many so supporting several stores later means dropping an
/// index, not migrating data.
/// </summary>
public sealed class Store : Entity
{
    // EF Core materialisation.
    private Store() { }

    private Store(Guid ownerUserId, string name, string slug, string currency)
    {
        OwnerUserId = ownerUserId;
        Name = name;
        Slug = slug;
        Currency = currency;
        Theme = StoreTheme.CreateDefault(Id);
    }

    public Guid OwnerUserId { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>The public address: <c>yourapp.com/{Slug}</c>. Lowercase and globally unique.</summary>
    public string Slug { get; private set; } = null!;

    public string? Description { get; private set; }

    // --- Contact --------------------------------------------------------
    public string? ContactPhone { get; private set; }
    public string? WhatsApp { get; private set; }
    public string? ContactEmail { get; private set; }

    // --- Social ---------------------------------------------------------
    public string? InstagramUrl { get; private set; }
    public string? FacebookUrl { get; private set; }
    public string? TiktokUrl { get; private set; }

    // --- Location -------------------------------------------------------
    public string? AddressText { get; private set; }
    public string? City { get; private set; }
    public string? Country { get; private set; }

    // --- Branding -------------------------------------------------------
    public Guid? LogoMediaId { get; private set; }
    public Guid? CoverMediaId { get; private set; }

    // --- SEO ------------------------------------------------------------
    public string? SeoTitle { get; private set; }
    public string? SeoDescription { get; private set; }

    public string Currency { get; private set; } = SupportedCurrencies.Default;

    /// <summary>The seller's own switch.</summary>
    public bool IsPublished { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>
    /// The platform admin's switch, kept separate from <see cref="IsPublished"/> so that a suspended
    /// store cannot simply be re-published by its owner.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    public StoreTheme Theme { get; private set; } = null!;

    /// <summary>A storefront renders only when the seller has published it and the admin has not suspended it.</summary>
    public bool IsVisibleToPublic => IsPublished && IsActive;

    public static Store Create(Guid ownerUserId, string name, string slug, string? currency)
    {
        // Normalised first, then validated. Slugs are always stored lowercase, so rejecting mixed-case
        // input would mean the rule depends on the caller having normalised it already — and every
        // caller would have to remember.
        var normalisedSlug = NormaliseSlug(slug);

        var slugCheck = StoreSlug.Validate(normalisedSlug);
        if (!slugCheck.IsValid)
        {
            throw new BusinessRuleException(slugCheck.Error!);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessRuleException("A store name is required.");
        }

        var resolvedCurrency = SupportedCurrencies.IsSupported(currency)
            ? currency!
            : SupportedCurrencies.Default;

        return new Store(ownerUserId, name.Trim(), normalisedSlug, resolvedCurrency);
    }

    private static string NormaliseSlug(string? slug) =>
        (slug ?? string.Empty).Trim().ToLowerInvariant();

    public void UpdateProfile(
        string name,
        string? description,
        string? contactPhone,
        string? whatsApp,
        string? contactEmail,
        string? instagramUrl,
        string? facebookUrl,
        string? tiktokUrl,
        string? addressText,
        string? city,
        string? country,
        string? currency)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessRuleException("A store name is required.");
        }

        Name = name.Trim();
        Description = Normalise(description);
        ContactPhone = Normalise(contactPhone);
        WhatsApp = Normalise(whatsApp);
        ContactEmail = Normalise(contactEmail);
        InstagramUrl = Normalise(instagramUrl);
        FacebookUrl = Normalise(facebookUrl);
        TiktokUrl = Normalise(tiktokUrl);
        AddressText = Normalise(addressText);
        City = Normalise(city);
        Country = Normalise(country);

        if (SupportedCurrencies.IsSupported(currency))
        {
            Currency = currency!;
        }

        // A published store that loses its last contact channel would become unreachable, which is
        // worse than refusing the edit.
        if (IsPublished && !HasContactChannel)
        {
            throw new BusinessRuleException(
                "A published store needs at least one way to be contacted: phone, WhatsApp or email.");
        }
    }

    public void UpdateSeo(string? seoTitle, string? seoDescription)
    {
        SeoTitle = Normalise(seoTitle);
        SeoDescription = Normalise(seoDescription);
    }

    public void ChangeSlug(string slug)
    {
        var normalisedSlug = NormaliseSlug(slug);

        var slugCheck = StoreSlug.Validate(normalisedSlug);
        if (!slugCheck.IsValid)
        {
            throw new BusinessRuleException(slugCheck.Error!);
        }

        Slug = normalisedSlug;
    }

    public void SetLogo(Guid? mediaId) => LogoMediaId = mediaId;

    public void SetCover(Guid? mediaId) => CoverMediaId = mediaId;

    /// <summary>
    /// Publishing is the moment a store becomes a real public page, so the bar is what a customer
    /// needs: a name, an address, and some way to reach the seller. Deliberately nothing
    /// category-specific — a florist with no products yet may still want their page live.
    /// </summary>
    public void Publish(DateTimeOffset now)
    {
        if (!IsActive)
        {
            throw new BusinessRuleException("This store has been deactivated and cannot be published.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new BusinessRuleException("Add a store name before publishing.");
        }

        if (!HasContactChannel)
        {
            throw new BusinessRuleException(
                "Add at least one way for customers to reach you — phone, WhatsApp or email — before publishing.");
        }

        if (IsPublished)
        {
            return;
        }

        IsPublished = true;
        PublishedAt = now;
    }

    public void Unpublish()
    {
        IsPublished = false;
    }

    /// <summary>Admin action. Deactivating also takes the storefront offline immediately.</summary>
    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }

    public bool HasContactChannel =>
        !string.IsNullOrWhiteSpace(ContactPhone)
        || !string.IsNullOrWhiteSpace(WhatsApp)
        || !string.IsNullOrWhiteSpace(ContactEmail);

    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
