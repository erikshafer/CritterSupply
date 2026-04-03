using JasperFx.Events.Projections;
using Listings.Projections;
using Marten;
using Marten.Events.Projections;
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

// CORS — allow Backoffice.Web (Blazor WASM) to call Listings.Api directly
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
    ?? "Host=localhost;Port=5433;Database=listings;Username=postgres;Password=postgres";

builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = "listings";

    // Event-sourced Listing aggregate — inline snapshot for queryable state
    opts.Projections.Snapshot<Listings.Listing.Listing>(SnapshotLifecycle.Inline);

    // Multi-stream projection: per-SKU index of active listing stream IDs
    opts.Projections.Add<ListingsActiveViewProjection>(ProjectionLifecycle.Inline);
})
    .UseLightweightSessions()
    .IntegrateWithWolverine();

// JWT Bearer authentication
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? "dev-only-signing-key-change-in-production-must-be-at-least-32-chars";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "backoffice-identity";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "listings-api";

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

// Wolverine messaging and HTTP
builder.Host.UseWolverine(opts =>
{
    // API assembly (HTTP endpoints)
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);

    // Domain assembly (command handlers, integration message handlers)
    opts.Discovery.IncludeAssembly(typeof(Listings.Listing.Listing).Assembly);

    opts.UseFluentValidation();

    // CRITICAL: AutoApplyTransactions ensures Marten sessions are committed by Wolverine.
    // Without this, HTTP endpoint handlers that return (IResult, OutgoingMessages) tuples
    // will silently discard event appends and projection updates.
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
    .AutoProvision()
    // Declare exchange-to-queue bindings so marketplace outcome events are routed here (M38.0)
    .DeclareExchange("marketplaces-listing-activated", ex => ex.BindQueue("listings-marketplace-outcome-events", ""))
    .DeclareExchange("marketplaces-submission-rejected", ex => ex.BindQueue("listings-marketplace-outcome-events", ""));

    // Listen to product catalog events for ProductSummaryView (Session 2)
    opts.ListenToRabbitQueue("listings-product-catalog-events");

    // Listen to product recall priority exchange
    opts.ListenToRabbitQueue("listings-product-recall");

    // Listen for marketplace listing outcome events (M38.0 bidirectional feedback)
    opts.ListenToRabbitQueue("listings-marketplace-outcome-events");

    // Outbound: publish lifecycle events so downstream BCs can react (M38.0)
    opts.PublishMessage<Messages.Contracts.Listings.ListingActivated>()
        .ToRabbitExchange("listings-listing-activated");
    opts.PublishMessage<Messages.Contracts.Listings.ListingEnded>()
        .ToRabbitExchange("listings-listing-ended");
});

// Wolverine HTTP
builder.Services.AddWolverineHttp();

// Add Swagger/OpenAPI support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Listings API");
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

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Service = "Listings.Api" }))
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
