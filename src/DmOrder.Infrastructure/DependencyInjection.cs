using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Identity;
using DmOrder.Infrastructure.Ai;
using DmOrder.Infrastructure.Billing;
using DmOrder.Infrastructure.Identity;
using DmOrder.Infrastructure.Orders;
using DmOrder.Infrastructure.Persistence;
using DmOrder.Infrastructure.Phones;
using DmOrder.Infrastructure.Persistence.Interceptors;
using DmOrder.Infrastructure.Services;
using DmOrder.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
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
        AddIdentity(services, configuration);
        AddFileStorage(services, configuration);

        AddAi(services, configuration);

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

    private static void AddIdentity(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // Length is enforced by the request validator (minimum 10). Composition rules mostly
                // push people towards predictable substitutions, so they are deliberately off.
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.MaxFailedAccessAttempts = 8;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddScoped<IUserAccountService, UserAccountService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IUserDisplayNameLookup, UserDisplayNameLookup>();
        services.AddScoped<IUserDirectory, UserDirectory>();

        services.AddOptions<OrderReferenceOptions>()
            .Bind(configuration.GetSection(OrderReferenceOptions.SectionName));
        services.AddScoped<IOrderReferenceFactory, OrderReferenceFactory>();

        services.AddOptions<PlanOptions>().Bind(configuration.GetSection(PlanOptions.SectionName));
        services.AddSingleton<IPlanPolicy, PlanPolicy>();

        // The only provider that exists. Every call that would take money throws, and IsConfigured
        // is false so nothing offers an upgrade button that cannot work.
        services.AddSingleton<IPaymentProvider, UnconfiguredPaymentProvider>();

        // Thread-safe and expensive to build, so shared. See LibPhoneNumbers.
        services.AddSingleton<IPhoneNumbers, LibPhoneNumbers>();
        services.AddScoped<IdentitySeeder>();
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

    /// <summary>
    /// Reading pasted messages into draft orders.
    ///
    /// An unset key selects the not-configured reader rather than failing at startup. Deploying
    /// without AI is a supported state: the endpoint answers 503, the dashboard hides the button,
    /// and nothing else in the product is affected. Same shape as the payment provider.
    /// </summary>
    private static void AddAi(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ClaudeOptions.SectionName);

        services.AddOptions<ClaudeOptions>()
            .Bind(section)
            .ValidateOnStart();

        var configured = !string.IsNullOrWhiteSpace(section[nameof(ClaudeOptions.ApiKey)]);

        if (configured)
        {
            services.AddSingleton<IOrderMessageReader, ClaudeOrderMessageReader>();
        }
        else
        {
            services.AddSingleton<IOrderMessageReader, NotConfiguredOrderMessageReader>();
        }
    }
}
