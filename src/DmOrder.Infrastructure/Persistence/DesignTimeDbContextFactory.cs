using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DmOrder.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> only.
///
/// Without this, the EF tools build the API host to find the DbContext, which runs the whole startup
/// path — configuration validation, role seeding, a database connection. That makes creating a
/// migration depend on having a working environment, which is backwards: a migration is source code.
///
/// Reads DATABASE_CONNECTION_STRING when present so `database update` targets the real database, and
/// otherwise falls back to a placeholder, which is enough for `migrations add` because scaffolding a
/// migration never connects.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        LoadDotEnv();

        var connectionString =
            Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=dmorder;Username=postgres;Password=design-time";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }

    /// <summary>Mirrors the API's .env loading so `dotnet ef` works from a plain shell.</summary>
    private static void LoadDotEnv()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        for (var depth = 0; depth < 6 && directory is not null; depth++)
        {
            var candidate = Path.Combine(directory.FullName, ".env");
            if (File.Exists(candidate))
            {
                foreach (var rawLine in File.ReadLines(candidate))
                {
                    var line = rawLine.Trim();
                    var separator = line.IndexOf('=');

                    if (line.Length == 0 || line.StartsWith('#') || separator <= 0)
                    {
                        continue;
                    }

                    var key = line[..separator].Trim();
                    if (Environment.GetEnvironmentVariable(key) is null)
                    {
                        Environment.SetEnvironmentVariable(key, line[(separator + 1)..].Trim().Trim('"'));
                    }
                }

                return;
            }

            directory = directory.Parent;
        }
    }
}
