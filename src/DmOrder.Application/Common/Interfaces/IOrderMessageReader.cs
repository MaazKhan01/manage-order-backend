namespace DmOrder.Application.Common.Interfaces;

/// <summary>
/// Reads a pasted customer message and says what it looks like an order for.
///
/// The port is shaped like the task rather than like a model call: it takes a message and the
/// seller's own catalogue, and returns a draft. It deliberately does not take a prompt and return
/// text - prompt construction and response parsing belong to whichever provider implements this,
/// not to the feature that uses it. See ADR 0009.
///
/// Implementations must treat <see cref="OrderMessageContext.Message"/> as hostile input. It was
/// written by a stranger and may be aimed at the model rather than at the seller.
/// </summary>
public interface IOrderMessageReader
{
    /// <summary>
    /// False when no provider is configured. The feature is then absent rather than broken - the
    /// endpoint says so and the UI does not offer it. Mirrors <see cref="IPaymentProvider"/>.
    /// </summary>
    bool IsConfigured { get; }

    Task<OrderDraft> ReadAsync(OrderMessageContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Everything the reader is allowed to see.
///
/// The catalogue and questions are built server-side from the authenticated seller's store. They
/// are never taken from the request, so one seller's message can never be read against another
/// seller's products.
/// </summary>
public sealed record OrderMessageContext(
    /// <summary>The pasted message. Untrusted.</summary>
    string Message,
    IReadOnlyList<CatalogueLine> Products,
    IReadOnlyList<QuestionLine> Questions,
    /// <summary>ISO currency of the store, so a bare "2000" is read in the right units.</summary>
    string Currency,
    /// <summary>
    /// Where the store is, used only to read a local phone number and to resolve a relative date
    /// like "Thursday". Free text, because the store's country is free text.
    /// </summary>
    string? StoreCountry,
    /// <summary>Today, from the clock, so "tomorrow" resolves without the model guessing the date.</summary>
    DateOnly Today);

public sealed record CatalogueLine(string Name, decimal? Price);

public sealed record QuestionLine(string Label, string FieldType, IReadOnlyList<string> Options);

/// <summary>
/// What the reader found. Every field is optional, because a real message usually leaves something
/// out - that is the whole reason the seller is shown a form rather than a saved order.
///
/// Note what is NOT here: no identifiers of any kind. The reader returns names and labels as free
/// text and the server matches them against the store's own data, so a hallucinated or injected id
/// is unrepresentable rather than merely rejected.
/// </summary>
public sealed record OrderDraft(
    string? ProductName,
    int? Quantity,
    string? CustomerName,
    string? CustomerPhone,
    string? DeliveryAddress,
    DateOnly? DeliveryDate,
    string? CustomerNote,
    IReadOnlyList<DraftAnswer> Answers,
    /// <summary>
    /// A message the seller can send back, whose job is to ask for whatever the customer left out.
    /// That is the useful draft at this moment - the confirmation with a tracking reference comes
    /// after the order exists.
    /// </summary>
    string? SuggestedReply)
{
    public static OrderDraft Empty { get; } =
        new(null, null, null, null, null, null, null, [], null);
}

/// <summary>An answer to one of the seller's own questions, matched by label rather than by id.</summary>
public sealed record DraftAnswer(string QuestionLabel, string Value);
