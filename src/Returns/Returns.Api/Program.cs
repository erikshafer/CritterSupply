using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using JasperFx.Resources;
using Marten;
using Marten.Events.Projections;
using Returns;
using Returns.Returns;
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
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Returns API");
    });
}

// Map Aspire default endpoints (/health, /alive)
app.MapDefaultEndpoints();

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
