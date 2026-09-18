using DmOrder.Api.Configuration;
using Microsoft.Extensions.Options;

namespace DmOrder.Api.Endpoints;

public static class PlatformEndpoints
{
    /// <summary>
    /// Exposes platform branding so server-rendered pages and order slips can show the product name
    /// without it being compiled into the frontend. Deliberately contains no seller data.
    /// </summary>
    public static RouteGroupBuilder MapPlatformEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/platform", (IOptions<PlatformBrandingOptions> branding) =>
            {
                var value = branding.Value;
                return Results.Ok(new PlatformBrandingResponse(
                    value.ProjectName,
                    value.ProjectShortName,
                    value.ProjectDescription,
                    value.PlatformUrl,
                    value.SupportEmail,
                    value.LogoUrl,
                    value.FaviconUrl));
            })
            .WithName("GetPlatformBranding")
            .WithSummary("Platform (not seller) branding.");

        return group;
    }
}

public sealed record PlatformBrandingResponse(
    string ProjectName,
    string ProjectShortName,
    string ProjectDescription,
    string PlatformUrl,
    string SupportEmail,
    string LogoUrl,
    string FaviconUrl);
