using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Resources;
using Marten;
using Marten.Events.Projections;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Orders;
using Orders.Checkout;
using Orders.Placement;
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

        opts.DatabaseSchemaName = Constants.Orders.ToLowerInvariant();
        opts.DisableNpgsqlLogging = true;

        // Configure Order saga document storage
        opts.Schema.For<Order>()
            .Identity(x => x.Id)
            .UseNumericRevisions(true)
            .Index(x => x.CustomerId); // Index for querying orders by customer

        // Configure Checkout aggregate as an event sourced stream
        opts.Projections.Snapshot<Checkout>(SnapshotLifecycle.Inline);

        // projections here
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
    // This is almost an automatic default to have Wolverine apply transactional
    // middleware to any endpoint or handler that uses persistence services
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    // Opt into the transactional inbox/outbox on all messaging endpoints
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    opts.OnException<ConcurrencyException>()
        .RetryOnce()
        .Then.RetryWithCooldown(100.Milliseconds(), 250.Milliseconds())
        .Then.Discard();

    opts.UseFluentValidation();

    // Discover all handlers in the Orders domain assembly (Order saga, Checkout, PlaceOrderHandler, etc.)
    // The assembly is decorated with [assembly: WolverineModule] in AssemblyAttributes.cs,
    // consistent with all other bounded context assemblies in CritterSupply.
    opts.Discovery.IncludeAssembly(typeof(Order).Assembly);

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

    // Listen for CheckoutInitiated from Shopping BC
    opts.ListenToRabbitQueue("orders-checkout-initiated")
        .ProcessInline();

    // Route CartCheckoutCompleted internally to start Order saga
    // (CompleteCheckout handler publishes this after checkout finalization)
    opts.PublishMessage<Messages.Contracts.Shopping.CartCheckoutCompleted>()
        .ToLocalQueue("order-placement")
        .UseDurableInbox();

    // Route FulfillmentRequested to Fulfillment BC via RabbitMQ
    opts.PublishMessage<Messages.Contracts.Fulfillment.FulfillmentRequested>()
        .ToRabbitQueue("fulfillment-requests");

    // Listen for Fulfillment BC integration messages (ShipmentDispatched, ShipmentDelivered, ShipmentDeliveryFailed)
    opts.ListenToRabbitQueue("orders-fulfillment-events")
        .ProcessInline();

    // Listen for Returns BC integration messages (ReturnRequested, ReturnCompleted, ReturnDenied)
    opts.ListenToRabbitQueue("orders-returns-events")
        .ProcessInline();

    // Publish OrderPlaced to storefront-notifications queue
    opts.PublishMessage<Messages.Contracts.Orders.OrderPlaced>()
        .ToRabbitQueue("storefront-notifications");
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddWolverineHttp();

// Configure multi-issuer JWT authentication (ADR 0032)
builder.Services.AddAuthentication("Backoffice")
    .AddJwtBearer("Backoffice", options =>
    {
        options.Authority = "https://localhost:5249"; // Backoffice Identity BC
        options.Audience = "https://localhost:5249";  // Phase 1: self-referential
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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Orders API");
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
