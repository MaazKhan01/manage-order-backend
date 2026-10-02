using System.ComponentModel.DataAnnotations;

namespace DmOrder.Application.Common.Models;

/// <summary>
/// The SaaS product's own branding — never a seller's. Seller branding lives on the Store entity and is
/// per-tenant data. Keeping the two apart is what stops the platform name leaking into storefronts.
///
/// Everything here comes from configuration so renaming the product is an environment change,
/// not a code change.
///
/// Lives in Application rather than Api because it is product identity, not web configuration:
/// the password-reset email needs the product name and the platform URL, and a handler must not
/// have to reach up into the web layer for them.
/// </summary>
public sealed class PlatformBrandingOptions
{
    public const string SectionName = "PlatformBranding";

    [Required]
    public string ProjectName { get; set; } = "DM Order";

    [Required]
    public string ProjectShortName { get; set; } = "DMO";

    public string ProjectDescription { get; set; } =
        "A mini storefront and order manager for sellers who sell through social media.";

    public string PlatformUrl { get; set; } = "http://localhost:3000";

    public string SupportEmail { get; set; } = "support@example.com";

    public string LogoUrl { get; set; } = "/brand/logo.svg";

    public string FaviconUrl { get; set; } = "/favicon.ico";
}
