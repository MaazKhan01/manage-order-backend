using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace DmOrder.Infrastructure.Identity;

/// <summary>
/// Ensures the roles exist, and optionally creates the first platform admin.
///
/// The admin is created only when both <c>SEED_ADMIN_EMAIL</c> and <c>SEED_ADMIN_PASSWORD</c> are
/// configured, and only if no admin exists yet. There is deliberately no default admin password: a
/// well-known credential on a deployed platform is a back door, not a convenience.
/// </summary>
public sealed class IdentitySeeder(
    RoleManager<ApplicationRole> roleManager,
    UserManager<ApplicationUser> userManager,
    IDateTimeProvider clock,
    ILogger<IdentitySeeder> logger)
{
    public async Task SeedAsync(string? adminEmail, string? adminPassword, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var role in ApplicationRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new ApplicationRole(role));
                logger.LogInformation("Created role {Role}", role);
            }
        }

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            return;
        }

        if (await userManager.FindByEmailAsync(adminEmail) is not null)
        {
            return;
        }

        var now = clock.UtcNow;
        var admin = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = adminEmail,
            Email = adminEmail,
            DisplayName = "Platform Admin",
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var created = await userManager.CreateAsync(admin, adminPassword);
        if (!created.Succeeded)
        {
            logger.LogError(
                "Could not seed the admin account: {Errors}",
                string.Join("; ", created.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(admin, ApplicationRoles.Admin);
        logger.LogInformation("Seeded platform admin {Email}", adminEmail);
    }
}
