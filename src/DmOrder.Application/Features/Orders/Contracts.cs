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
public sealed record SubmitOrderResponse(int OrderNumber, string StoreName, string? WhatsApp);
