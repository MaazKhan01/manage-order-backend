using Asp.Versioning;
using Asp.Versioning.Builder;

namespace DmOrder.Api.Endpoints;

/// <summary>
/// Defines the three top-level route groups. Audience is encoded in the URL so that "can an anonymous
/// visitor reach this?" is answerable by reading the path, and so a seller endpoint cannot be left
/// public by forgetting an attribute.
///
///   /api/v1/public/...  anonymous storefront
///   /api/v1/seller/...  authenticated seller, scoped to their own store
///   /api/v1/admin/...   platform owner
/// </summary>
public static class ApiEndpoints
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        ApiVersionSet versionSet = app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        var api = app.MapGroup("/api/v{version:apiVersion}")
            .WithApiVersionSet(versionSet);

        var publicApi = api.MapGroup("/public")
            .AllowAnonymous()
            .WithTags("Public");

        var sellerApi = api.MapGroup("/seller")
            .RequireAuthorization(AuthorizationPolicies.Seller)
            .WithTags("Seller");

        var adminApi = api.MapGroup("/admin")
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .WithTags("Admin");

        // Feature modules register themselves against the group that matches their audience.
        publicApi.MapPlatformEndpoints();

        // Referenced so the groups are used; feature endpoints are added in later phases.
        _ = sellerApi;
        _ = adminApi;

        return app;
    }
}
