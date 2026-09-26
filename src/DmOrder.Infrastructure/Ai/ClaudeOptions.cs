namespace DmOrder.Infrastructure.Ai;

/// <summary>
/// Configuration for the Claude-backed message reader.
///
/// The API key is read from configuration - environment variables in every real deployment - and is
/// never sent to the browser and never committed. An unset key is a supported state, not a
/// misconfiguration: the feature is then simply absent (see <see cref="NotConfiguredOrderMessageReader"/>).
/// </summary>
public sealed class ClaudeOptions
{
    public const string SectionName = "Claude";

    /// <summary>Unset means no AI provider is configured.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Haiku, because reading a short message is the cheapest thing a model can usefully do and a
    /// seller is waiting with a customer on the line. Overridable without a code change.
    /// </summary>
    public string Model { get; set; } = "claude-haiku-4-5";

    /// <summary>
    /// Enough for a filled-in draft and a short reply. Extraction output is small and bounded by
    /// the schema; this is a backstop, not a tuning knob.
    /// </summary>
    public int MaxTokens { get; set; } = 2000;

    /// <summary>
    /// Give up rather than hold a seller's request open. They can retype the order faster than they
    /// can wait out a hung call.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
