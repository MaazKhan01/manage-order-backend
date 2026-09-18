using DmOrder.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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

    private Respawner? _respawner;
    private NpgsqlConnection? _connection;

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
        ?? DefaultTestConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Jwt:Secret", "integration-tests-signing-key-not-used-anywhere-else");
        builder.UseSetting("Jwt:Issuer", "dmorder-api-tests");
        builder.UseSetting("Jwt:Audience", "dmorder-web-tests");
        builder.UseSetting("PlatformBranding:ProjectName", "DM Order");
        builder.UseSetting("PlatformBranding:ProjectShortName", "DMO");
    }

    // Implemented explicitly: WebApplicationFactory already has a public DisposeAsync returning
    // ValueTask, which cannot also satisfy xUnit's IAsyncLifetime.DisposeAsync returning Task.
    async Task IAsyncLifetime.InitializeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        _connection = new NpgsqlConnection(ConnectionString);
        await _connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            // Migration history must survive, otherwise every reset would re-run migrations.
            TablesToIgnore = [new Respawn.Graph.Table("__EFMigrationsHistory")],
            SchemasToInclude = ["public"],
        });
    }

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
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
