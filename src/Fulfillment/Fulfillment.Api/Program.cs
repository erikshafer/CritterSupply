using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Fulfillment;
using Fulfillment.Routing;
using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using JasperFx.Resources;
using Marten;
using Marten.Events.Projections;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
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

        opts.DatabaseSchemaName = Constants.Fulfillment.ToLowerInvariant();
        opts.DisableNpgsqlLogging = true;

        // Register aggregates for event sourcing
        opts.Projections.Snapshot<Shipment>(SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<WorkOrder>(SnapshotLifecycle.Inline);

        // ShipmentStatusView projection (customer-facing tracking)
        opts.Projections.Add<ShipmentStatusViewProjection>(ProjectionLifecycle.Inline);

        // CarrierPerformanceView projection (carrier metrics)
        opts.Projections.Add<CarrierPerformanceViewProjection>(ProjectionLifecycle.Inline);

        // MultiShipmentView projection (split orders + reshipments)
        opts.Projections.Add<MultiShipmentViewProjection>(ProjectionLifecycle.Inline);
    })
    .AddAsyncDaemon(DaemonMode.Solo)
    .UseLightweightSessions()
    .IntegrateWithWolverine(config =>
    {
        config.UseWolverineManagedEventSubscriptionDistribution = true;
    });

builder.Services.AddResourceSetupOnStartup();

// Routing engine registration
// TODO: Replace StubFulfillmentRoutingEngine after Inventory BC Remaster (Gap #2: multi-warehouse allocation)
builder.Services.AddScoped<IFulfillmentRoutingEngine, StubFulfillmentRoutingEngine>();

// Carrier label service registration
builder.Services.AddScoped<ICarrierLabelService, StubCarrierLabelService>();

// System clock registration — injectable for time-based testing
builder.Services.AddSingleton<ISystemClock, SystemClock>();

builder.Services.ConfigureSystemTextJsonForWolverineOrMinimalApi(opts =>
{
    opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    opts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Host.UseWolverine(opts =>
{
    // Discover handlers from the Fulfillment assembly (both Shipments and WorkOrders)
    opts.Discovery.IncludeAssembly(typeof(Fulfillment.Shipments.ShipmentStatus).Assembly);

    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    opts.OnException<ConcurrencyException>()
        .RetryOnce()
        .Then.RetryWithCooldown(100.Milliseconds(), 250.Milliseconds())
        .Then.Discard();

    opts.UseFluentValidation();

    // Configure RabbitMQ for integration messaging
    var rabbitConfig = builder.Configuration.GetSection("RabbitMQ");
    opts.UseRabbitMq(rabbit =>
    {
        rabbit.HostName = rabbitConfig["hostname"] ?? "localhost";
        rabbit.VirtualHost = rabbitConfig["virtualhost"] ?? "/";
        rabbit.Port = rabbitConfig.GetValue<int?>("port") ?? 5672;
        rabbit.UserName = rabbitConfig["username"] ?? "guest";
        rabbit.Password = rabbitConfig["password"] ?? "guest";
    }).AutoProvision();

    // Listen for FulfillmentRequested from Orders BC
    opts.ListenToRabbitQueue("fulfillment-requests").ProcessInline();

    // Publish fulfillment integration messages to Orders BC
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentHandedToCarrier>()
        .ToRabbitQueue("orders-fulfillment-events");
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDelivered>()
        .ToRabbitQueue("orders-fulfillment-events");
    opts.PublishMessage<Messages.Contracts.Fulfillment.ReturnToSenderInitiated>()
        .ToRabbitQueue("orders-fulfillment-events");
    opts.PublishMessage<Messages.Contracts.Fulfillment.TrackingNumberAssigned>()
        .ToRabbitQueue("orders-fulfillment-events");
    opts.PublishMessage<Messages.Contracts.Fulfillment.BackorderCreated>()
        .ToRabbitQueue("orders-fulfillment-events");
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentLostInTransit>()
        .ToRabbitQueue("orders-fulfillment-events");
    opts.PublishMessage<Messages.Contracts.Fulfillment.ReshipmentCreated>()
        .ToRabbitQueue("orders-fulfillment-events");
    opts.PublishMessage<Messages.Contracts.Fulfillment.OrderSplitIntoShipments>()
        .ToRabbitQueue("orders-fulfillment-events");
    opts.PublishMessage<Messages.Contracts.Fulfillment.FulfillmentCancelled>()
        .ToRabbitQueue("orders-fulfillment-events");

    // Also publish to Storefront for real-time customer updates
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentHandedToCarrier>()
        .ToRabbitQueue("storefront-fulfillment-events");
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDelivered>()
        .ToRabbitQueue("storefront-fulfillment-events");
    opts.PublishMessage<Messages.Contracts.Fulfillment.TrackingNumberAssigned>()
        .ToRabbitQueue("storefront-fulfillment-events");

    // Publish to Returns BC for return eligibility window establishment
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDelivered>()
        .ToRabbitQueue("returns-fulfillment-events");

    // Publish to Correspondence BC for customer notifications
    opts.PublishMessage<Messages.Contracts.Fulfillment.ReturnToSenderInitiated>()
        .ToRabbitQueue("correspondence-fulfillment-events");

    // Publish to Backoffice BC for fulfillment pipeline metrics
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentHandedToCarrier>()
        .ToRabbitQueue("backoffice-shipment-dispatched");
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDelivered>()
        .ToRabbitQueue("backoffice-shipment-delivered");
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure JWT authentication with Backoffice and Vendor issuers
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer("Backoffice", options =>
    {
        options.Authority = "https://localhost:5249";
        options.Audience = "https://localhost:5249";
        if (builder.Environment.IsDevelopment())
        {
            options.RequireHttpsMetadata = false;
        }
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
        options.Authority = "https://localhost:5240";
        options.Audience = "https://localhost:5240";
        if (builder.Environment.IsDevelopment())
        {
            options.RequireHttpsMetadata = false;
        }
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RoleClaimType = "role"
        };
    });

// Configure authorization policies
builder.Services.AddAuthorization(opts =>
{
    // Backoffice policies
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

    // Vendor policies
    opts.AddPolicy("VendorAdmin", policy =>
    {
        policy.AuthenticationSchemes.Add("Vendor");
        policy.RequireRole("VendorAdmin");
    });

    // Cross-issuer policy: accept tokens from either Backoffice or Vendor
    opts.AddPolicy("AnyAuthenticated", policy =>
    {
        policy.AuthenticationSchemes.Add("Backoffice");
        policy.AuthenticationSchemes.Add("Vendor");
        policy.RequireAuthenticatedUser();
    });
});

builder.Services.AddWolverineHttp();

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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Fulfillment API");
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
