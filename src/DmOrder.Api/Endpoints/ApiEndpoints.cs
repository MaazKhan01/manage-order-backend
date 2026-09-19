using Asp.Versioning;
using Asp.Versioning.Builder;

namespace DmOrder.Api.Endpoints;

/// <summary>
/// Defines the top-level route groups. Audience is encoded in the URL so that "can an anonymous
/// visitor reach this?" is answerable by reading the path, and so a seller endpoint cannot be left
/// public by forgetting an attribute.
///
///   /api/v1/public/...   anonymous storefront and auth
///   /api/v1/account/...  any authenticated user, whatever their role
///   /api/v1/seller/...   role Seller, scoped to their own store
///   /api/v1/admin/...    role Admin
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

        // Authenticated but role-agnostic: a platform admin has a profile too, so gating /me behind the
        // Seller role would lock admins out of their own account.
        var accountApi = api.MapGroup("/account")
            .RequireAuthorization()
            .WithTags("Account");

        var sellerApi = api.MapGroup("/seller")
            .RequireAuthorization(AuthorizationPolicies.Seller)
            .WithTags("Seller");

        var adminApi = api.MapGroup("/admin")
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .WithTags("Admin");

        // Feature modules register themselves against the group that matches their audience.
        publicApi.MapPlatformEndpoints();
        publicApi.MapPublicAuthEndpoints();
        publicApi.MapPublicStoreEndpoints();
        publicApi.MapPublicCatalogueEndpoints();
        publicApi.MapPublicOrderEndpoints();

        accountApi.MapSellerAccountEndpoints();

        sellerApi.MapSellerStoreEndpoints();
        sellerApi.MapSellerCatalogueEndpoints();
        sellerApi.MapSellerCustomFieldEndpoints();
        sellerApi.MapSellerOrderEndpoints();
        sellerApi.MapSellerCustomerEndpoints();
        sellerApi.MapSellerMediaEndpoints();

        // Populated from Phase 9.
        _ = adminApi;

        return app;
    }
}
