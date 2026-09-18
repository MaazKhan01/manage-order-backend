namespace DmOrder.Application.Common.Interfaces;

/// <summary>
/// Provider-agnostic seam for future AI features (turning a WhatsApp message into a structured order,
/// spotting missing order details, drafting replies).
///
/// Nothing in V1 calls this. It exists so that adding AI later does not mean threading a vendor SDK
/// through the application, and so the API key stays server-side from day one.
/// </summary>
public interface IAiService
{
    Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken);
}

public sealed record AiRequest(string SystemPrompt, string UserPrompt, int? MaxTokens = null);

public sealed record AiCompletion(string Text, string Model);
