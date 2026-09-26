using DmOrder.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;

namespace DmOrder.Api.IntegrationTests;

/// <summary>
/// Boots the real API against a real PostgreSQL database.
///
/// Deliberately not an in-memory provider: the things worth testing here are unique constraints,
/// cascades, transactions and tenant filters, and an in-memory provider silently passes all of them.
/// Deliberately not Testcontainers either — Docker is not available on the development machine, so the
/// suite targets a dedicated test database configured by TEST_DATABASE_CONNECTION_STRING.
///
/// That database is wiped between tests. Never point it at anything you care about.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string DefaultTestConnectionString =
        "Host=localhost;Port=5432;Database=dmorder_test;Username=postgres;Password=postgres";

    private readonly string _uploadRoot =
        Path.Combine(Path.GetTempPath(), "dmorder-tests", Guid.CreateVersion7().ToString("n"));

    private Respawner? _respawner;
    private NpgsqlConnection? _connection;

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
        ?? DefaultTestConnectionString;

    public ApiFactory()
    {
        GuardAgainstWipingTheDevelopmentDatabase();

        // Program reads DATABASE_CONNECTION_STRING from the environment (and from a .env file, which
        // only fills in variables that are not already set). Setting it here is what actually points
        // the application under test at the test database — UseSetting alone is overridden by the
        // configuration sources Program adds afterwards.
        Environment.SetEnvironmentVariable("DATABASE_CONNECTION_STRING", ConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", ConnectionString);

        // Never let a stray .env seed an admin into the test database.
        Environment.SetEnvironmentVariable("SEED_ADMIN_EMAIL", string.Empty);
        Environment.SetEnvironmentVariable("SEED_ADMIN_PASSWORD", string.Empty);

        // Never let a developer's real API key turn the suite into a billed network call. With a key
        // present the draft-from-message endpoint would contact Anthropic for real: slow, flaky,
        // chargeable, and it would silently invert the "no provider configured" test.
        Environment.SetEnvironmentVariable("Claude__ApiKey", string.Empty);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Appended last so it wins over everything Program registered.
        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString,
                ["Jwt:Secret"] = "integration-tests-signing-key-not-used-anywhere-else",
                ["Jwt:Issuer"] = "dmorder-api-tests",
                ["Jwt:Audience"] = "dmorder-web-tests",
                ["PlatformBranding:ProjectName"] = "DM Order",
                ["PlatformBranding:ProjectShortName"] = "DMO",
                ["Seed:AdminEmail"] = null,
                ["Claude:ApiKey"] = null,
                ["Seed:AdminPassword"] = null,

                // TestServer gives every request a null remote address, so the whole suite shares one
                // rate-limit partition and would throttle itself. The limiter is still wired up and is
                // verified directly by RateLimitingTests, which lowers these on its own host.
                ["RateLimiting:AuthPermitLimit"] = "10000",
                ["RateLimiting:PublicWritePermitLimit"] = "10000",

                // Uploads go to a throwaway folder, so a test run never leaves files in the repo.
                ["FileStorage:LocalRootPath"] = _uploadRoot,
            }));
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        // Migrate with a standalone context, before anything touches Services.
        //
        // Building the host runs the API's startup work, which seeds roles and therefore expects the
        // schema to already exist. Resolving AppDbContext from Services to migrate would be circular:
        // the host would fail to build for want of the very tables we are about to create.
        await using (var migrationContext = CreateStandaloneContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        using (var scope = Services.CreateScope())
        {
            // Last line of defence: assert the application actually ended up on the test database.
            // Getting this wrong once would silently destroy development data.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actual = db.Database.GetDbConnection().Database;
            var expected = new NpgsqlConnectionStringBuilder(ConnectionString).Database;

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Integration tests are pointed at '{actual}' but expected '{expected}'. Refusing to run.");
            }
        }

        _connection = new NpgsqlConnection(ConnectionString);
        await _connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            TablesToIgnore =
            [
                // Migration history must survive, otherwise every reset would re-run migrations.
                new Respawn.Graph.Table("__EFMigrationsHistory"),

                // Roles are reference data seeded once at startup, not test data. Truncating them
                // makes registration fail to assign a role from the second test onwards — which is
                // exactly the kind of failure that looks like a bug in the code under test.
                new Respawn.Graph.Table("roles"),
            ],
            SchemasToInclude = ["public"],
        });
    }

    private AppDbContext CreateStandaloneContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>Returns the database to an empty state so tests cannot leak into one another.</summary>
    public async Task ResetDatabaseAsync()
    {
        if (_respawner is not null && _connection is not null)
        {
            await _respawner.ResetAsync(_connection);
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        await base.DisposeAsync();

        try
        {
            if (Directory.Exists(_uploadRoot))
            {
                Directory.Delete(_uploadRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a green test run over.
        }
    }

    /// <summary>
    /// The suite truncates every table it can see. If someone ever copies the development connection
    /// string into TEST_DATABASE_CONNECTION_STRING, that would destroy their data on the next run.
    /// </summary>
    private void GuardAgainstWipingTheDevelopmentDatabase()
    {
        var testDatabase = new NpgsqlConnectionStringBuilder(ConnectionString).Database;

        if (string.IsNullOrWhiteSpace(testDatabase) || !testDatabase.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"TEST_DATABASE_CONNECTION_STRING must point at a database whose name ends in '_test'. " +
                $"Got '{testDatabase}'. The integration suite wipes every table it can see.");
        }
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
