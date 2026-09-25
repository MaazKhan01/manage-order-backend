namespace DmOrder.Application.Features.Orders;

/// <summary>One answer as the public form submits it. Untrusted until validated.</summary>
public sealed record SubmitFieldAnswer(Guid FieldId, string? Value, IReadOnlyList<string>? Values, Guid? MediaId);

public sealed record SubmitOrderRequest(
    string ProductSlug,
    int Quantity,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    string? DeliveryAddress,
    string? CustomerNote,
    IReadOnlyList<SubmitFieldAnswer> Answers,
    /// <summary>
    /// A honeypot. Real people never fill this in because it is hidden; naive bots fill everything.
    /// A filled value is accepted with a normal-looking response and quietly discarded.
    /// </summary>
    string? Website);

/// <summary>
/// What a customer sees after ordering. Deliberately thin: an order number to quote and nothing
/// that would let someone enumerate or read back orders they did not place.
/// </summary>
/// <summary>
/// What the customer sees after submitting.
///
/// <c>Reference</c> is the one they need to keep: it is how they track the order later, and the only
/// order identifier they are ever shown. <c>OrderNumber</c> is the seller's own running count, useful
/// when the two of them talk.
/// </summary>
public sealed record SubmitOrderResponse(
    int OrderNumber,
    string Reference,
    string StoreName,
    string? WhatsApp);
