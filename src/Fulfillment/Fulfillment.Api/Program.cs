using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Fulfillment;
using Fulfillment.Shipments;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Resources;
using Marten;
using Marten.Events.Projections;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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

        // Register Shipment aggregate for event sourcing
        opts.Projections.Snapshot<Shipment>(SnapshotLifecycle.Inline);
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
    // Discover handlers from the Fulfillment assembly
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
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDispatched>()
        .ToRabbitQueue("orders-fulfillment-events");
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDelivered>()
        .ToRabbitQueue("orders-fulfillment-events");
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDeliveryFailed>()
        .ToRabbitQueue("orders-fulfillment-events");

    // Also publish to Storefront for real-time customer updates
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDispatched>()
        .ToRabbitQueue("storefront-fulfillment-events");
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDelivered>()
        .ToRabbitQueue("storefront-fulfillment-events");
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDeliveryFailed>()
        .ToRabbitQueue("storefront-fulfillment-events");

    // Publish to Returns BC for return eligibility window establishment
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDelivered>()
        .ToRabbitQueue("returns-fulfillment-events");
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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Fulfillment API");
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
