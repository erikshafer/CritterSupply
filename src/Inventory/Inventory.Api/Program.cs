using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Inventory;
using Inventory.Management;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Resources;
using Marten;
using Marten.Events.Projections;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Weasel.Core;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Marten;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

builder.Host.ApplyJasperFxExtensions();

var connectionString = builder.Configuration.GetConnectionString("postgres")
                             ?? throw new Exception("The connection string 'postgres' was not found");

builder.Services.AddMarten(opts =>
    {
        opts.Connection(connectionString);
        opts.AutoCreateSchemaObjects = AutoCreate.All;
        opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        opts.DatabaseSchemaName = Constants.Inventory.ToLowerInvariant();
        opts.DisableNpgsqlLogging = true;

        // Register ProductInventory aggregate for event sourcing
        opts.Projections.Snapshot<ProductInventory>(SnapshotLifecycle.Inline);
    })
    .AddAsyncDaemon(DaemonMode.Solo)
    .UseLightweightSessions()
    .IntegrateWithWolverine(config =>
    {
        config.UseWolverineManagedEventSubscriptionDistribution = true;
    });

builder.Services.AddResourceSetupOnStartup();

builder.Services.ConfigureSystemTextJsonForWolverineOrMinimalApi(opts =>
{
    opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    opts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Host.UseWolverine(opts =>
{
    // Discover handlers from the Inventory assembly
    opts.Discovery.IncludeAssembly(typeof(ProductInventory).Assembly);

    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    opts.OnException<ConcurrencyException>()
        .RetryOnce()
        .Then.RetryWithCooldown(100.Milliseconds(), 250.Milliseconds())
        .Then.Discard();

    opts.UseFluentValidation();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddWolverineHttp();

// Configure multi-issuer JWT authentication (ADR 0032)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer("Backoffice", options =>
    {
        options.Authority = "https://localhost:5249"; // Backoffice Identity BC
        options.Audience = "https://localhost:5249";  // Phase 1: self-referential
        options.RequireHttpsMetadata = false; // Development only
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RoleClaimType = "role" // Map JWT "role" claim to ClaimTypes.Role
        };
    })
    .AddJwtBearer("Vendor", options =>
    {
        options.Authority = "https://localhost:5240"; // Vendor Identity BC
        options.Audience = "https://localhost:5240";
        options.RequireHttpsMetadata = false; // Development only
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RoleClaimType = "role"
        };
    });

// Configure authorization policies (ADR 0032)
builder.Services.AddAuthorization(opts =>
{
    // Backoffice policies (accept Backoffice scheme only)
    opts.AddPolicy("CustomerService", policy =>
    {
        policy.AuthenticationSchemes.Add("Backoffice");
        policy.RequireRole("CustomerService", "OperationsManager", "SystemAdmin");
    });

    opts.AddPolicy("WarehouseClerk", policy =>
    {
        policy.AuthenticationSchemes.Add("Backoffice");
        policy.RequireRole("WarehouseClerk", "OperationsManager", "SystemAdmin");
    });

    opts.AddPolicy("OperationsManager", policy =>
    {
        policy.AuthenticationSchemes.Add("Backoffice");
        policy.RequireRole("OperationsManager", "SystemAdmin");
    });

    // Vendor policies (accept Vendor scheme only)
    opts.AddPolicy("VendorAdmin", policy =>
    {
        policy.AuthenticationSchemes.Add("Vendor");
        policy.RequireRole("VendorAdmin");
    });

    // Cross-issuer policies (accept Backoffice OR Vendor)
    opts.AddPolicy("AnyAuthenticated", policy =>
    {
        policy.AuthenticationSchemes.Add("Backoffice");
        policy.AuthenticationSchemes.Add("Vendor");
        policy.RequireAuthenticatedUser();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger(options =>
    {
        options.RouteTemplate = "api/{documentName}/swagger.json";
    });
    app.UseSwaggerUI(opts =>
    {
        opts.RoutePrefix = "api";
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Inventory API");
    });
}

// Map Aspire default endpoints (/health, /alive)
app.MapDefaultEndpoints();

// Add authentication and authorization middleware (ADR 0032)
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapHealthChecks("/api/v1/health").AllowAnonymous();
}

app.MapWolverineEndpoints(opts =>
{
    opts.UseFluentValidationProblemDetailMiddleware();
});

app.MapGet("/", (HttpResponse response) =>
{
    response.Headers.Append("Location", "/api");
    response.StatusCode = StatusCodes.Status301MovedPermanently;
}).ExcludeFromDescription();

return await app.RunJasperFxCommands(args);

[ExcludeFromCodeCoverage]
public partial class Program { }
