using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Resources;
using Marten;
using Marten.Events.Projections;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Shopping;
using Shopping.Cart;
using Weasel.Core;
using CheckoutAggregate = Shopping.Checkout.Checkout;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Marten;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);
builder.Host.ApplyJasperFxExtensions();

// OpenTelemetry configuration for Wolverine tracing and metrics
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Shopping"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()  // HTTP request tracing
            .AddSource("Wolverine")           // Wolverine message handler tracing
            .AddOtlpExporter();               // Export to Jaeger via OTLP
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter("Wolverine")            // Wolverine metrics (success/failure counters)
            .AddOtlpExporter();               // Export metrics to Jaeger via OTLP
    });

var martenConnectionString = builder.Configuration.GetConnectionString("marten")
                             ?? throw new Exception("The connection string for Marten was not found");

builder.Services.AddMarten(opts =>
    {
        opts.Connection(martenConnectionString);
        opts.AutoCreateSchemaObjects = AutoCreate.All;
        opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        opts.DatabaseSchemaName = Constants.Shopping.ToLowerInvariant();
        opts.DisableNpgsqlLogging = true;

        // Register aggregates for event sourcing
        opts.Projections.Snapshot<Cart>(SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<CheckoutAggregate>(SnapshotLifecycle.Inline);
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

builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Shopping API");
    });
}

if (app.Environment.IsDevelopment())
{
    app.MapHealthChecks("/api/v1/health").AllowAnonymous();
    app.MapHealthChecks("/health");
    app.MapHealthChecks("/alive", new HealthCheckOptions
    {
        Predicate = r => r.Tags.Contains("live")
    }).AllowAnonymous();
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
