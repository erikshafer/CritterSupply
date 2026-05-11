using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Resources;
using Marten;
using Marten.Events.Projections;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Returns;
using Returns.ReturnProcessing;
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

// Configure Marten for Event Sourcing + Document Store
builder.Services.AddMarten(opts =>
    {
        opts.Connection(connectionString);
        opts.AutoCreateSchemaObjects = AutoCreate.All;
        opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        opts.DatabaseSchemaName = Constants.Returns.ToLowerInvariant();
        opts.DisableNpgsqlLogging = true;

        // Configure Return as an event-sourced aggregate with inline snapshots
        opts.Events.StreamIdentity = StreamIdentity.AsGuid;
        opts.Projections.Snapshot<Return>(SnapshotLifecycle.Inline);

        // Configure ReturnEligibilityWindow document with OrderId as primary key
        opts.Schema.For<ReturnEligibilityWindow>()
            .Identity(x => x.Id) // Use Id (which equals OrderId) as primary key
            .Index(x => x.CustomerId) // Index for customer queries
            .Index(x => x.WindowExpiresAt); // Index for expiry queries

        // Index Return snapshots for order-based queries (GetReturnsForOrder)
        opts.Schema.For<Return>()
            .Index(x => x.OrderId);
    })
    .AddAsyncDaemon(DaemonMode.Solo)
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
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    opts.OnException<ConcurrencyException>()
        .RetryOnce()
        .Then.RetryWithCooldown(100.Milliseconds(), 250.Milliseconds())
        .Then.Discard();

    opts.UseFluentValidation();

    // Discover all handlers in the Returns domain assembly
    opts.Discovery.IncludeAssembly(typeof(Return).Assembly);

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

    // Inbound: Listen for ShipmentDelivered from Fulfillment BC
    opts.ListenToRabbitQueue("returns-fulfillment-events")
        .ProcessInline();

    // M47.0 / Slice 1 — Inbound: Listen for replacement-reservation
    // outcomes from Inventory BC (cross-product exchange flow).
    // See ADR 0061 and docs/planning/milestones/m47-0-plan.md.
    opts.ListenToRabbitQueue("returns-inventory-events")
        .UseDurableInbox();

    // M47.0 / Slice 1 — Outbound: request replacement-SKU reservation
    // from Inventory BC when ApproveExchange runs the cross-product
    // branch. Routed to the same `inventory-returns-events` queue that
    // Inventory listens on.
    opts.PublishMessage<Messages.Contracts.Inventory.ReserveReplacementForExchange>()
        .ToRabbitQueue("inventory-returns-events");

    // M47.0 / Slice 2 — Returns ↔ Payments cross-product exchange delta
    // capture + partial refund. See ADR 0062 and
    // docs/planning/milestones/m47-0-plan.md.
    //
    // Inbound: Payments replies (capture success/failure, refund issued)
    // ride a shared `returns-payments-events` queue. The handlers in
    // Returns.Integration route each by message type.
    opts.ListenToRabbitQueue("returns-payments-events")
        .UseDurableInbox();

    // Outbound: capture-delta request rides the existing
    // ExchangeAdditionalPaymentRequired contract (already constructed
    // by ApproveExchangeHandler). Route an additional copy to the new
    // `payments-returns-events` queue so Payments receives the request.
    opts.PublishMessage<Messages.Contracts.Returns.ExchangeAdditionalPaymentRequired>()
        .ToRabbitQueue("payments-returns-events");

    // Outbound: partial-refund request — new contract emitted by
    // ShipReplacementItemHandler when the replacement is cheaper.
    opts.PublishMessage<Messages.Contracts.Payments.ExchangePartialRefundRequested>()
        .ToRabbitQueue("payments-returns-events");

    // M47.0 / Slice 4 — Outbound: refund-the-captured-delta request.
    // Emitted by SubmitInspectionHandler when an inspection rejects a
    // cross-product exchange that already captured the additional-payment
    // delta. Payments side replies via the existing
    // ExchangePartialRefundIssued contract on `returns-payments-events`.
    opts.PublishMessage<Messages.Contracts.Payments.RefundExchangeDeltaRequested>()
        .ToRabbitQueue("payments-returns-events");

    // M47.0 / Slice 4 — Outbound: release the held replacement reservation
    // when an exchange is cancelled (payment-capture failure) or rejected
    // (inspection failure with captured delta). Inventory side handler
    // is fully idempotent against missing reservations.
    opts.PublishMessage<Messages.Contracts.Inventory.ReleaseExchangeReservation>()
        .ToRabbitQueue("inventory-returns-events");

    // === Outbound: Orders BC ===
    // Orders saga needs: ReturnRequested, ReturnCompleted, ReturnDenied, ReturnRejected, ReturnExpired
    opts.PublishMessage<Messages.Contracts.Returns.ReturnRequested>()
        .ToRabbitQueue("orders-returns-events");
    opts.PublishMessage<Messages.Contracts.Returns.ReturnCompleted>()
        .ToRabbitQueue("orders-returns-events");
    opts.PublishMessage<Messages.Contracts.Returns.ReturnDenied>()
        .ToRabbitQueue("orders-returns-events");
    opts.PublishMessage<Messages.Contracts.Returns.ReturnRejected>()
        .ToRabbitQueue("orders-returns-events");
    opts.PublishMessage<Messages.Contracts.Returns.ReturnExpired>()
        .ToRabbitQueue("orders-returns-events");
    opts.PublishMessage<Messages.Contracts.Returns.ReturnReceived>()
        .ToRabbitQueue("orders-returns-events");

    // === Outbound: Cross-product exchange events to Orders BC ===
    opts.PublishMessage<Messages.Contracts.Returns.CrossProductExchangeRequested>()
        .ToRabbitQueue("orders-returns-events");
    opts.PublishMessage<Messages.Contracts.Returns.ExchangeAdditionalPaymentRequired>()
        .ToRabbitQueue("orders-returns-events");
    opts.PublishMessage<Messages.Contracts.Returns.ExchangeAdditionalPaymentCaptured>()
        .ToRabbitQueue("orders-returns-events");
    opts.PublishMessage<Messages.Contracts.Returns.ExchangePartialRefundIssued>()
        .ToRabbitQueue("orders-returns-events");
    // M47.0 / Slice 4 — exchange cancellation propagated so Orders saga
    // knows the Return is in a terminal "did not happen" state.
    opts.PublishMessage<Messages.Contracts.Returns.ExchangeCancelled>()
        .ToRabbitQueue("orders-returns-events");

    // === Outbound: Customer Experience BC (Storefront) ===
    // Real-time updates for return status via SignalR
    opts.PublishMessage<Messages.Contracts.Returns.ReturnRequested>()
        .ToRabbitQueue("storefront-returns-events");
    opts.PublishMessage<Messages.Contracts.Returns.ReturnApproved>()
        .ToRabbitQueue("storefront-returns-events");
    opts.PublishMessage<Messages.Contracts.Returns.ReturnDenied>()
        .ToRabbitQueue("storefront-returns-events");
    opts.PublishMessage<Messages.Contracts.Returns.ReturnRejected>()
        .ToRabbitQueue("storefront-returns-events");
    opts.PublishMessage<Messages.Contracts.Returns.ReturnExpired>()
        .ToRabbitQueue("storefront-returns-events");
    opts.PublishMessage<Messages.Contracts.Returns.ReturnCompleted>()
        .ToRabbitQueue("storefront-returns-events");
    opts.PublishMessage<Messages.Contracts.Returns.ReturnReceived>()
        .ToRabbitQueue("storefront-returns-events");

    // === Outbound: Cross-product exchange events to Storefront ===
    opts.PublishMessage<Messages.Contracts.Returns.CrossProductExchangeRequested>()
        .ToRabbitQueue("storefront-returns-events");
    opts.PublishMessage<Messages.Contracts.Returns.ExchangeAdditionalPaymentRequired>()
        .ToRabbitQueue("storefront-returns-events");
    opts.PublishMessage<Messages.Contracts.Returns.ExchangeAdditionalPaymentCaptured>()
        .ToRabbitQueue("storefront-returns-events");
    opts.PublishMessage<Messages.Contracts.Returns.ExchangePartialRefundIssued>()
        .ToRabbitQueue("storefront-returns-events");
    // M47.0 / Slice 4 — customer-visible cancellation notification.
    opts.PublishMessage<Messages.Contracts.Returns.ExchangeCancelled>()
        .ToRabbitQueue("storefront-returns-events");

    // === Outbound: Backoffice BC (M33.0 Session 2) ===
    // Dashboard metrics and operations alerts
    opts.PublishMessage<Messages.Contracts.Returns.ReturnRequested>()
        .ToRabbitQueue("backoffice-return-requested");
    opts.PublishMessage<Messages.Contracts.Returns.ReturnApproved>()
        .ToRabbitQueue("backoffice-return-approved");
    opts.PublishMessage<Messages.Contracts.Returns.ReturnDenied>()
        .ToRabbitQueue("backoffice-return-denied");
    opts.PublishMessage<Messages.Contracts.Returns.ReturnRejected>()
        .ToRabbitQueue("backoffice-return-rejected");
    opts.PublishMessage<Messages.Contracts.Returns.ReturnExpired>()
        .ToRabbitQueue("backoffice-return-expired");
    opts.PublishMessage<Messages.Contracts.Returns.ReturnCompleted>()
        .ToRabbitQueue("backoffice-return-completed");
    opts.PublishMessage<Messages.Contracts.Returns.ReturnReceived>()
        .ToRabbitQueue("backoffice-return-received");
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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Returns API");
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
