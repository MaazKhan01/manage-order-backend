using DmOrder.Application.Common.Interfaces;
using DmOrder.Infrastructure.Ai;
using DmOrder.Infrastructure.Persistence;
using DmOrder.Infrastructure.Persistence.Interceptors;
using DmOrder.Infrastructure.Services;
using DmOrder.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DmOrder.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        AddPersistence(services, configuration);
        AddFileStorage(services, configuration);

        // No AI feature ships in V1; the seam exists so adding one later is a one-line swap.
        services.AddScoped<IAiService, NotConfiguredAiService>();

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is not configured. Copy .env.example and set DATABASE_CONNECTION_STRING.");

        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<TenantGuardInterceptor>();

        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            });

            options.AddInterceptors(
                serviceProvider.GetRequiredService<AuditableEntityInterceptor>(),
                serviceProvider.GetRequiredService<TenantGuardInterceptor>());
        });

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
    }

    private static void AddFileStorage(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Only the local provider exists today. The S3-compatible implementation is added in the
        // deployment phase and selected here by FileStorage:Provider.
        services.AddSingleton<IFileStorage, LocalDiskFileStorage>();
    }
}
