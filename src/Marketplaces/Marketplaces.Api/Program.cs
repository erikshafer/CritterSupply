using Marten;
using Marketplaces.Adapters;
using Marketplaces.CategoryMappings;
using Marketplaces.Credentials;
using Marketplaces.Marketplaces;
using Marketplaces.Products;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.IdentityModel.Tokens;
using Polly;
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

// CORS — allow Backoffice.Web (Blazor WASM) to call Marketplaces.Api directly
var backofficeOrigin = builder.Configuration["Cors:BackofficeOrigin"]
    ?? "http://localhost:5244";

builder.Services.AddCors(options =>
{
    options.AddPolicy("BackofficePolicy", policy =>
        policy.WithOrigins(backofficeOrigin)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials());
});

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

    // ProductSummaryView ACL — product data from Product Catalog BC, keyed by SKU (D-2a)
    opts.Schema.For<ProductSummaryView>().Identity(x => x.Id);
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

// Vault client — DevVaultClient for Development, EnvironmentVaultClient for production (ADR 0051)
if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<IVaultClient, DevVaultClient>();
else
    builder.Services.AddSingleton<IVaultClient, EnvironmentVaultClient>();

// Marketplace adapters — resolved by ChannelCode at runtime.
// UseRealAdapters flag enables production adapters; stubs remain the default for Development/CI.
var useRealAdapters = builder.Configuration.GetValue<bool>("Marketplaces:UseRealAdapters");

if (useRealAdapters)
{
    // Retry strategy: 3 attempts, exponential backoff starting at 1s.
    // Default ShouldHandle covers 408, 429, 500+ and HttpRequestException.
    // 401 is intentionally excluded from retry — auth failures are handled by each
    // adapter's token-refresh path; see ADR 0056 for the full 401 design rationale.
    var retryOptions = new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        Delay = TimeSpan.FromSeconds(1)
    };

    // Circuit breaker: opens after 50% failure rate across 5+ requests in 30s,
    // stays open for 30s. Per-adapter scope (each named HttpClient has its own breaker).
    // Default ShouldHandle covers 5xx and HttpRequestException; excludes 401.
    var circuitBreakerOptions = new HttpCircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        MinimumThroughput = 5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(30)
    };

    builder.Services.AddHttpClient("AmazonSpApi")
        .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30))
        .AddResilienceHandler("amazon-resilience", pipeline =>
        {
            pipeline.AddRetry(retryOptions);
            pipeline.AddCircuitBreaker(circuitBreakerOptions);
        });
    builder.Services.AddSingleton<IMarketplaceAdapter, AmazonMarketplaceAdapter>();

    builder.Services.AddHttpClient("WalmartApi")
        .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30))
        .AddResilienceHandler("walmart-resilience", pipeline =>
        {
            pipeline.AddRetry(retryOptions);
            pipeline.AddCircuitBreaker(circuitBreakerOptions);
        });
    builder.Services.AddSingleton<IMarketplaceAdapter, WalmartMarketplaceAdapter>();

    builder.Services.AddHttpClient("EbayApi")
        .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30))
        .AddResilienceHandler("ebay-resilience", pipeline =>
        {
            pipeline.AddRetry(retryOptions);
            pipeline.AddCircuitBreaker(circuitBreakerOptions);
        });
    builder.Services.AddSingleton<IMarketplaceAdapter, EbayMarketplaceAdapter>();
}
else
{
    builder.Services.AddSingleton<IMarketplaceAdapter, StubAmazonAdapter>();
    builder.Services.AddSingleton<IMarketplaceAdapter, StubWalmartAdapter>();
    builder.Services.AddSingleton<IMarketplaceAdapter, StubEbayAdapter>();
}

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

    // Queue for consuming Product Catalog events for ProductSummaryView ACL (D-2c)
    opts.ListenToRabbitQueue("marketplaces-product-catalog-events");

    // Publish integration messages to RabbitMQ exchanges
    opts.PublishMessage<Messages.Contracts.Marketplaces.MarketplaceListingActivated>()
        .ToRabbitExchange("marketplaces-listing-activated");
    opts.PublishMessage<Messages.Contracts.Marketplaces.MarketplaceSubmissionRejected>()
        .ToRabbitExchange("marketplaces-submission-rejected");
    opts.PublishMessage<Messages.Contracts.Marketplaces.MarketplaceRegistered>()
        .ToRabbitExchange("marketplaces-registered");
    opts.PublishMessage<Messages.Contracts.Marketplaces.MarketplaceDeactivated>()
        .ToRabbitExchange("marketplaces-deactivated");
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

app.UseCors("BackofficePolicy");

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
