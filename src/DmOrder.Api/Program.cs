using Asp.Versioning;
using DmOrder.Api.Common;
using DmOrder.Api.Configuration;
using DmOrder.Api.Endpoints;
using DmOrder.Api.Middleware;
using DmOrder.Application;
using DmOrder.Application.Common.Interfaces;
using DmOrder.Infrastructure;
using DmOrder.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;

// A local .env (never committed) is loaded before the host is built so it can supply configuration
// the same way the frontend is configured. Real environments set real environment variables.
DotEnvLoader.Load(Directory.GetCurrentDirectory());

var builder = WebApplication.CreateBuilder(args);

// --- Configuration ---------------------------------------------------------
// Secrets come from environment variables (or user-secrets in development), never from a committed file.
builder.Configuration.AddInMemoryCollection(EnvironmentVariableMappings.Collect());
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddOptions<PlatformBrandingOptions>()
    .Bind(builder.Configuration.GetSection(PlatformBrandingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<FrontendOptions>()
    .Bind(builder.Configuration.GetSection(FrontendOptions.SectionName))
    .ValidateOnStart();

// --- Logging ---------------------------------------------------------------
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// --- Application layers ----------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IStoreContext, HttpStoreContext>();

// --- HTTP concerns ---------------------------------------------------------
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
});

builder.Services.AddAuthentication();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthorizationPolicies.Seller, policy => policy.RequireRole(ApplicationRoles.Seller))
    .AddPolicy(AuthorizationPolicies.Admin, policy => policy.RequireRole(ApplicationRoles.Admin));

var frontendOrigins = builder.Configuration
    .GetSection(FrontendOptions.SectionName)
    .Get<FrontendOptions>()?.AllowedOrigins ?? ["http://localhost:3000"];

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(frontendOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

builder.Services.AddOpenApi();

var app = builder.Build();

// --- Pipeline --------------------------------------------------------------
// Order matters: exception handling first so everything below it produces a consistent problem
// response, correlation id next so failures are traceable.
app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
else
{
    app.UseHsts();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Liveness: is the process up? Deliberately does not touch the database, so a database outage does
// not make the orchestrator kill a healthy process.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();

// Readiness: can we actually serve traffic (database reachable)?
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();

app.MapApiEndpoints();

app.Run();

/// <summary>Exposed so the integration test host can reference the entry point assembly.</summary>
public partial class Program;
