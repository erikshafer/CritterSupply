using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Correspondence;
using Correspondence.Messages;
using Correspondence.Providers;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Resources;
using Marten;
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

var connectionString = builder.Configuration.GetConnectionString("marten")
                            ?? throw new Exception("The connection string 'marten' was not found");

builder.Services.AddMarten(opts =>
    {
        opts.Connection(connectionString);
        opts.AutoCreateSchemaObjects = AutoCreate.All;
        opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        opts.DatabaseSchemaName = Constants.Correspondence.ToLowerInvariant();
        opts.DisableNpgsqlLogging = true;
    })
    .AddAsyncDaemon(DaemonMode.Solo)
    .UseLightweightSessions()
    .IntegrateWithWolverine(config =>
    {
        config.UseWolverineManagedEventSubscriptionDistribution = true;
    });

builder.Services.AddResourceSetupOnStartup();

// Register email provider (stub for Phase 1)
builder.Services.AddSingleton<IEmailProvider, StubEmailProvider>();

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

    // Discover all handlers in the Correspondence domain assembly
    opts.Discovery.IncludeAssembly(typeof(Message).Assembly);

    // Configure RabbitMQ for integration messaging
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

    // Subscribe to integration events from other BCs
    opts.ListenToRabbitQueue("correspondence-orders-events")
        .ProcessInline();

    opts.ListenToRabbitQueue("correspondence-fulfillment-events")
        .ProcessInline();

    opts.ListenToRabbitQueue("correspondence-returns-events")
        .ProcessInline();

    // Publish integration events for monitoring/analytics
    opts.PublishMessage<Messages.Contracts.Correspondence.CorrespondenceQueued>()
        .ToRabbitQueue("monitoring-correspondence-events");

    opts.PublishMessage<Messages.Contracts.Correspondence.CorrespondenceDelivered>()
        .ToRabbitQueue("analytics-correspondence-events");

    opts.PublishMessage<Messages.Contracts.Correspondence.CorrespondenceFailed>()
        .ToRabbitQueue("admin-correspondence-failures");
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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Correspondence API");
    });
}

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
