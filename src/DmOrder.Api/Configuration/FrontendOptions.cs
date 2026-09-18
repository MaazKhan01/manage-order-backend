namespace DmOrder.Api.Configuration;

/// <summary>Where the Next.js app lives. Drives CORS and any absolute links the API generates.</summary>
public sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    /// <summary>Origins allowed to call this API from a browser. Never "*" once credentials are involved.</summary>
    public string[] AllowedOrigins { get; set; } = ["http://localhost:3000"];
}
