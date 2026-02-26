using Marten;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Storefront.Notifications;
using Wolverine;
using Wolverine.Http;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry configuration for Wolverine tracing and metrics
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Storefront"))
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

// Add Marten for document store (not event sourcing - BFF doesn't own domain data)
builder.Services.AddMarten(opts =>
{
    var connectionString = builder.Configuration.GetConnectionString("marten")!;
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = "storefront"; // Isolated schema for BFF
});

// Add Wolverine HTTP support (required for Wolverine.HTTP endpoints)
builder.Services.AddWolverineHttp();

// Register EventBroadcaster as singleton (in-memory pub/sub for SSE)
builder.Services.AddSingleton<IEventBroadcaster, EventBroadcaster>();

// Add Wolverine with message handler discovery
builder.Host.UseWolverine(opts =>
{
    // Discover handlers in both API and Domain assemblies
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly); // Storefront.Api (Queries)
    opts.Discovery.IncludeAssembly(typeof(Storefront.Notifications.IEventBroadcaster).Assembly); // Storefront (Notifications)

    // Configure RabbitMQ for subscribing to integration messages
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

    // Subscribe to Shopping BC integration messages
    opts.ListenToRabbitQueue("storefront-notifications")
        .ProcessInline(); // Process messages immediately (no buffering)
});

// Add HTTP clients for downstream BC queries
builder.Services.AddHttpClient("ShoppingClient", client =>
{
    client.BaseAddress = new Uri("http://localhost:5236"); // Shopping BC
});

builder.Services.AddHttpClient("OrdersClient", client =>
{
    client.BaseAddress = new Uri("http://localhost:5231"); // Orders BC
});

builder.Services.AddHttpClient("CustomerIdentityClient", client =>
{
    client.BaseAddress = new Uri("http://localhost:5235"); // Customer Identity BC
});

builder.Services.AddHttpClient("CatalogClient", client =>
{
    client.BaseAddress = new Uri("http://localhost:5133"); // Product Catalog BC
});

// Register HTTP client implementations
builder.Services.AddScoped<Storefront.Clients.IShoppingClient, Storefront.Api.Clients.ShoppingClient>();
builder.Services.AddScoped<Storefront.Clients.IOrdersClient, Storefront.Api.Clients.OrdersClient>();
builder.Services.AddScoped<Storefront.Clients.ICustomerIdentityClient, Storefront.Api.Clients.CustomerIdentityClient>();
builder.Services.AddScoped<Storefront.Clients.ICatalogClient, Storefront.Api.Clients.CatalogClient>();

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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Storefront BFF API");
    });
}

// Map Wolverine HTTP endpoints
app.MapWolverineEndpoints();

// Redirect root to Swagger/API documentation
app.MapGet("/", (HttpResponse response) =>
{
    response.Headers.Append("Location", "/api");
    response.StatusCode = StatusCodes.Status301MovedPermanently;
}).ExcludeFromDescription();

app.Run();

// Make Program accessible for testing
public partial class Program;
