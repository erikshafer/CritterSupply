using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
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
using Shopping;
using Shopping.Cart;
using Weasel.Core;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Marten;
using Wolverine.RabbitMQ;

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

        opts.DatabaseSchemaName = Constants.Shopping.ToLowerInvariant();
        opts.DisableNpgsqlLogging = true;

        // Register aggregates for event sourcing
        opts.Projections.Snapshot<Cart>(SnapshotLifecycle.Inline);
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
    // Discover handlers from the Shopping assembly
    opts.Discovery.IncludeAssembly(typeof(Cart).Assembly);

    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    opts.OnException<ConcurrencyException>()
        .RetryOnce()
        .Then.RetryWithCooldown(100.Milliseconds(), 250.Milliseconds())
        .Then.Discard();

    opts.UseFluentValidation();

    // Configure RabbitMQ for publishing integration messages
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

    // Publish integration messages to their respective queues
    opts.PublishMessage<Messages.Contracts.Shopping.ItemAdded>()
        .ToRabbitQueue("storefront-notifications");
    opts.PublishMessage<Messages.Contracts.Shopping.ItemRemoved>()
        .ToRabbitQueue("storefront-notifications");
    opts.PublishMessage<Messages.Contracts.Shopping.ItemQuantityChanged>()
        .ToRabbitQueue("storefront-notifications");
    opts.PublishMessage<Messages.Contracts.Shopping.CouponApplied>()
        .ToRabbitQueue("storefront-notifications");
    opts.PublishMessage<Messages.Contracts.Shopping.CouponRemoved>()
        .ToRabbitQueue("storefront-notifications");

    // Publish CheckoutInitiated to Orders BC
    opts.PublishMessage<Messages.Contracts.Shopping.CheckoutInitiated>()
        .ToRabbitQueue("orders-checkout-initiated");
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure HttpClient for Customer Identity BC integration
builder.Services.AddHttpClient("CustomerIdentity", client =>
{
    var customerIdentityBaseUrl = builder.Configuration.GetValue<string>("CustomerIdentity:BaseUrl")
                                  ?? "http://localhost:5002";
    client.BaseAddress = new Uri(customerIdentityBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Configure HttpClient for Pricing BC integration
builder.Services.AddHttpClient("PricingClient", client =>
{
    var pricingBaseUrl = builder.Configuration.GetValue<string>("Pricing:BaseUrl")
                         ?? "http://localhost:5242";
    client.BaseAddress = new Uri(pricingBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

// Configure HttpClient for Promotions BC integration
builder.Services.AddHttpClient("PromotionsClient", client =>
{
    var promotionsBaseUrl = builder.Configuration.GetValue<string>("Promotions:BaseUrl")
                            ?? "http://localhost:5250";
    client.BaseAddress = new Uri(promotionsBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

// Register HTTP client implementations
builder.Services.AddScoped<Shopping.Clients.IPricingClient, Shopping.Api.Clients.PricingClient>();
builder.Services.AddScoped<Shopping.Clients.IPromotionsClient, Shopping.Api.Clients.PromotionsClient>();

builder.Services.AddWolverineHttp();

// Configure multi-issuer JWT authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer("Backoffice", options =>
    {
        options.Authority = "https://localhost:5249"; // Backoffice Identity BC
        options.Audience = "https://localhost:5249";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RoleClaimType = "role"
        };
    })
    .AddJwtBearer("Vendor", options =>
    {
        options.Authority = "https://localhost:5240"; // Vendor Identity BC
        options.Audience = "https://localhost:5240";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization();

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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Shopping API");
    });
}

// Map Aspire default endpoints (/health, /alive)
app.MapDefaultEndpoints();

// Add authentication and authorization middleware
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
