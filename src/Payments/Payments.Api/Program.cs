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
using Payments;
using Payments.Processing;
using Weasel.Core;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Marten;

var builder = WebApplication.CreateBuilder(args);
builder.Host.ApplyJasperFxExtensions();

// OpenTelemetry configuration for Wolverine tracing and metrics
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Payments"))
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

        opts.DatabaseSchemaName = Constants.Payments.ToLowerInvariant();
        opts.DisableNpgsqlLogging = true;

        // Register Payment aggregate for event sourcing
        opts.Projections.Snapshot<Payment>(SnapshotLifecycle.Inline);
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
    // Discover handlers from the Payments assembly
    opts.Discovery.IncludeAssembly(typeof(Payment).Assembly);

    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    opts.OnException<ConcurrencyException>()
        .RetryOnce()
        .Then.RetryWithCooldown(100.Milliseconds(), 250.Milliseconds())
        .Then.Discard();

    opts.UseFluentValidation();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

// Register IPaymentGateway - StubPaymentGateway for development, real gateway for production
builder.Services.AddSingleton<IPaymentGateway, StubPaymentGateway>();

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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Payments API");
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
