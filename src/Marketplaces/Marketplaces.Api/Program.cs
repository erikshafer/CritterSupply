using Marten;
using Marketplaces.Adapters;
using Marketplaces.CategoryMappings;
using Marketplaces.Credentials;
using Marketplaces.Marketplaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Marten;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// Marten document store configuration
var connectionString = builder.Configuration.GetConnectionString("postgres")
    ?? "Host=localhost;Port=5433;Database=marketplaces;Username=postgres;Password=postgres";

builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = "marketplaces";

    // Marketplace is a document entity — not event-sourced (D4)
    opts.Schema.For<Marketplace>().Identity(x => x.Id);

    // CategoryMapping uses composite key: "{ChannelCode}:{InternalCategory}" (D5)
    opts.Schema.For<CategoryMapping>().Identity(x => x.Id);
})
    .UseLightweightSessions()
    .IntegrateWithWolverine();

// JWT Bearer authentication
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? "dev-only-signing-key-change-in-production-must-be-at-least-32-chars";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "backoffice-identity";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "marketplaces-api";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization();

// Vault client — Development only; production safety guard below
if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<IVaultClient, DevVaultClient>();

// Marketplace adapter stubs (Development) — resolved by ChannelCode at runtime
builder.Services.AddSingleton<IMarketplaceAdapter, StubAmazonAdapter>();
builder.Services.AddSingleton<IMarketplaceAdapter, StubWalmartAdapter>();
builder.Services.AddSingleton<IMarketplaceAdapter, StubEbayAdapter>();

builder.Services.AddSingleton<IReadOnlyDictionary<string, IMarketplaceAdapter>>(sp =>
    sp.GetServices<IMarketplaceAdapter>()
      .ToDictionary(a => a.ChannelCode, StringComparer.OrdinalIgnoreCase));

// Wolverine messaging and HTTP
builder.Host.UseWolverine(opts =>
{
    // API assembly (HTTP endpoints)
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);

    // Domain assembly (command handlers)
    opts.Discovery.IncludeAssembly(typeof(Marketplace).Assembly);

    opts.UseFluentValidation();

    // CRITICAL (GR-1): AutoApplyTransactions ensures Marten sessions are committed by Wolverine.
    // Without this, handlers that write to Marten will silently discard changes.
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    // Configure RabbitMQ for integration messages
    var rabbitConfig = builder.Configuration.GetSection("RabbitMQ");
    opts.UseRabbitMq(rabbit =>
    {
        rabbit.HostName = rabbitConfig["hostname"] ?? "localhost";
        rabbit.VirtualHost = rabbitConfig["virtualhost"] ?? "/";
        rabbit.Port = rabbitConfig.GetValue<int?>("port") ?? 5672;
        rabbit.UserName = rabbitConfig["username"] ?? "guest";
        rabbit.Password = rabbitConfig["password"] ?? "guest";
    })
    .AutoProvision();

    // Queue for consuming ListingApproved from Listings BC
    opts.ListenToRabbitQueue("marketplaces-listings-events");

    // Publish integration messages to RabbitMQ exchanges
    opts.PublishMessage<Messages.Contracts.Marketplaces.MarketplaceListingActivated>()
        .ToRabbitExchange("marketplaces-listing-activated");
    opts.PublishMessage<Messages.Contracts.Marketplaces.MarketplaceSubmissionRejected>()
        .ToRabbitExchange("marketplaces-submission-rejected");
});

// Wolverine HTTP
builder.Services.AddWolverineHttp();

// Add Swagger/OpenAPI support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Production safety guard — DevVaultClient must never run outside Development
if (!app.Environment.IsDevelopment() &&
    builder.Services.Any(s => s.ImplementationType == typeof(DevVaultClient)))
    throw new InvalidOperationException(
        "DevVaultClient must not be used in Production. Configure a real IVaultClient.");

// Seed marketplace data in development
if (app.Environment.IsDevelopment())
{
    await MarketplacesSeedData.SeedAsync(app);
}

// Configure Swagger UI (development only)
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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Marketplaces API");
    });
}

app.UseAuthentication();
app.UseAuthorization();

// Map Aspire default endpoints (/health, /alive)
app.MapDefaultEndpoints();

// Wolverine HTTP endpoints with FluentValidation middleware
app.MapWolverineEndpoints(opts =>
{
    opts.UseFluentValidationProblemDetailMiddleware();
});

// Health check endpoint — AllowAnonymous per D11 (GR-2 only applies to domain endpoints)
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Service = "Marketplaces.Api" }))
    .AllowAnonymous();

// Redirect root to Swagger/API documentation
app.MapGet("/", (HttpResponse response) =>
{
    response.Headers.Append("Location", "/api");
    response.StatusCode = StatusCodes.Status301MovedPermanently;
}).ExcludeFromDescription();

app.Run();

// Expose for integration tests
public partial class Program { }
