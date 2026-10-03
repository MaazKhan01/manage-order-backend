using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Identity;
using DmOrder.Infrastructure.Ai;
using DmOrder.Infrastructure.Billing;
using DmOrder.Infrastructure.Email;
using DmOrder.Infrastructure.Identity;
using DmOrder.Infrastructure.Media;
using DmOrder.Infrastructure.Orders;
using DmOrder.Infrastructure.Persistence;
using DmOrder.Infrastructure.Spreadsheets;
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
        AddEmail(services, configuration);

        services.AddOptions<ImageOptimizerOptions>()
            .Bind(configuration.GetSection(ImageOptimizerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Not optional and not configurable away: an image that is not re-encoded is an image whose
        // EXIF - including GPS - would be published as uploaded.
        services.AddSingleton<IImageOptimizer, SkiaImageOptimizer>();

        // Stateless, so one instance serves every export.
        services.AddSingleton<ISpreadsheetWriter, ClosedXmlSpreadsheetWriter>();

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
            .AddEntityFrameworkStores<AppDbContext>()
            // Required for password reset tokens. Without it GeneratePasswordResetTokenAsync throws
            // at the moment a locked-out seller needs it most.
            .AddDefaultTokenProviders();

        // Two hours, not Identity's default day. The link is a bearer credential sitting in an
        // inbox; long enough to find the mail and act on it, short enough that an old one found
        // later is worthless.
        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromHours(2));

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
            // A half-configured bucket fails at startup rather than on a seller's first upload.
            .Validate(
                options => !options.ValidateS3().Any(),
                "FileStorage is set to the S3 provider but is missing required settings. "
                + "See FileStorageOptions.ValidateS3 for which.")
            .ValidateOnStart();

        var provider = configuration[$"{FileStorageOptions.SectionName}:Provider"];

        if (string.Equals(provider, "S3", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IFileStorage, S3FileStorage>();
        }
        else
        {
            // Development only. An ephemeral host wipes this on every deploy, which is why the
            // deployment checklist switches the provider before the first real release.
            services.AddSingleton<IFileStorage, LocalDiskFileStorage>();
        }
    }

    /// <summary>
    /// Transactional email - in practice, password reset and nothing else yet.
    ///
    /// An unset key selects the not-configured sender rather than failing at startup, so the rest of
    /// the product runs without it. Password reset is the one thing that then reports itself
    /// unavailable, which is honest: a reset flow that silently sends nothing is worse than one that
    /// says it is switched off.
    /// </summary>
    private static void AddEmail(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(EmailOptions.SectionName);

        services.AddOptions<EmailOptions>()
            .Bind(section)
            .Validate(
                options => !options.ValidateSender().Any(),
                "Email:FromAddress must be a valid address when an API key is set.")
            .ValidateOnStart();

        var configured = !string.IsNullOrWhiteSpace(section[nameof(EmailOptions.ApiKey)])
            && !string.IsNullOrWhiteSpace(section[nameof(EmailOptions.FromAddress)]);

        if (configured)
        {
            services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
            {
                client.BaseAddress = new Uri("https://api.resend.com/");
                // A locked-out seller is waiting on this request; failing fast beats holding it open.
                client.Timeout = TimeSpan.FromSeconds(15);
            });
        }
        else
        {
            services.AddSingleton<IEmailSender, NotConfiguredEmailSender>();
        }
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
