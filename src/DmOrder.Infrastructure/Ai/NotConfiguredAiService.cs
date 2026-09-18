using DmOrder.Application.Common.Interfaces;

namespace DmOrder.Infrastructure.Ai;

/// <summary>
/// Placeholder registration for <see cref="IAiService"/>. V1 ships no AI features, so this fails loudly
/// rather than silently returning empty text. Replace with a real provider implementation when an AI
/// feature is actually specified; nothing outside this folder needs to change.
/// </summary>
public sealed class NotConfiguredAiService : IAiService
{
    public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "No AI provider is configured. Register a real IAiService implementation before using AI features.");
}
