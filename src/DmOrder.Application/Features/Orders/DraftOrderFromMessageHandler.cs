using DmOrder.Application.Common;
using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Features.Catalogue;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Orders;

/// <summary>
/// The seller pastes the message a customer sent them; this says what it looks like an order for.
///
/// It creates nothing. The response is a draft the seller reviews and submits through the ordinary
/// manual-order form, so every rule that has always applied to an order still applies - the
/// validator, the tenancy checks, the subscription guard, the history entry. See ADR 0009 for why
/// that separation is the thing holding this feature up.
/// </summary>
public sealed record DraftOrderRequest(string Message);

public sealed class DraftOrderRequestValidator : AbstractValidator<DraftOrderRequest>
{
    /// <summary>
    /// Long enough for a rambling voice-note transcript, short enough that nobody can paste a novel
    /// into a paid model on our account. A WhatsApp message that reaches this is not an order.
    /// </summary>
    public const int MaxMessageLength = 4000;

    public DraftOrderRequestValidator()
    {
        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Paste the message you received.")
            .MaximumLength(MaxMessageLength)
            .WithMessage($"That message is too long to read - paste up to {MaxMessageLength} characters.");
    }
}

/// <summary>
/// What the seller sees before they confirm. Mirrors the manual-order form field for field, plus
/// what is still needed and a reply they can send to ask for it.
/// </summary>
public sealed record OrderDraftResponse(
    Guid? ProductId,
    string? ProductName,
    int? Quantity,
    string? CustomerName,
    string? CustomerPhone,
    string? DeliveryAddress,
    DateOnly? DeliveryDate,
    string? CustomerNote,
    IReadOnlyList<DraftAnswerResponse> Answers,
    /// <summary>Field names the seller still has to fill in, in the order the form shows them.</summary>
    IReadOnlyList<string> Missing,
    string? SuggestedReply);

public sealed record DraftAnswerResponse(Guid FieldId, string Label, string Value);

public sealed class DraftOrderFromMessageHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IOrderMessageReader reader,
    IPhoneNumbers phones,
    IDateTimeProvider clock)
{
    public async Task<OrderDraftResponse> HandleAsync(
        DraftOrderRequest request,
        CancellationToken cancellationToken)
    {
        // Store first, then configuration. Whether the caller has a store is a question about them;
        // whether a provider is switched on is a question about the deployment, and answering the
        // second one first would tell someone with no store more about the system than they should
        // learn from a request they were never entitled to make.
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);

        if (!reader.IsConfigured)
        {
            throw new OrderMessageReaderNotConfiguredException();
        }

        var store = await db.Stores
            .AsNoTracking()
            .Where(s => s.Id == storeId)
            .Select(s => new { s.Currency, s.Country })
            .FirstAsync(cancellationToken);

        // The catalogue and questions are built here, from the authenticated store. Nothing about
        // what the model is allowed to see comes from the request.
        var products = await db.Products
            .AsNoTracking()
            .Where(p => p.StoreId == storeId && p.DeletedAt == null && p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.Price })
            .Take(CatalogueLimit)
            .ToListAsync(cancellationToken);

        var questions = await db.CustomFields
            .AsNoTracking()
            .Where(f => f.StoreId == storeId && f.DeletedAt == null)
            .Include(f => f.Options)
            .OrderBy(f => f.DisplayOrder)
            .Take(QuestionLimit)
            .ToListAsync(cancellationToken);

        var draft = await reader.ReadAsync(
            new OrderMessageContext(
                request.Message,
                [.. products.Select(p => new CatalogueLine(p.Name, p.Price))],
                [.. questions.Select(q => new QuestionLine(
                    q.Label,
                    q.FieldType.ToString(),
                    [.. q.Options.OrderBy(o => o.DisplayOrder).Select(o => o.Value)]))],
                store.Currency,
                store.Country,
                DateOnly.FromDateTime(clock.UtcNow.UtcDateTime)),
            cancellationToken);

        return OrderDraftMapper.Map(
            draft,
            [.. products.Select(p => new DraftProduct(p.Id, p.Name, p.Price))],
            questions,
            NormalisePhone(draft.CustomerPhone, store.Country),
            DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));
    }

    /// <summary>Enough catalogue to match against without sending a whole shop on every call.</summary>
    private const int CatalogueLimit = 200;

    private const int QuestionLimit = 50;

    /// <summary>
    /// Returns the number in E.164, or null if it is not a real number.
    ///
    /// A half-read phone number is worse than none: it looks filled in, so the seller saves it, and
    /// the customer can never track their order. Better to report it missing and have them ask.
    /// </summary>
    private string? NormalisePhone(string? raw, string? country)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // E.164 first, which needs no region and is what the reader is asked for. Only if that
        // fails is the store's country used to read a locally-written number.
        var parsed = phones.Parse(raw);
        if (parsed.IsValid) return parsed.E164;

        if (CountryRegions.Resolve(country) is { } region)
        {
            var local = phones.Parse(raw, region);
            if (local.IsValid) return local.E164;
        }

        return null;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Thrown when no AI provider is configured. Surfaces as 503 rather than 500: the feature is absent,
/// which is a deployment fact, not a fault.
/// </summary>
public sealed class OrderMessageReaderNotConfiguredException()
    : Exception("No AI provider is configured, so messages cannot be read into orders.");
