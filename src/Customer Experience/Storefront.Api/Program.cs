using Marten;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Storefront.RealTime;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.RabbitMQ;
using Wolverine.SignalR;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// Add Marten for document store (not event sourcing - BFF doesn't own domain data)
builder.Services.AddMarten(opts =>
{
    var connectionString = builder.Configuration.GetConnectionString("postgres")!;
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = "storefront"; // Isolated schema for BFF
});

// Add Wolverine HTTP support (required for Wolverine.HTTP endpoints)
builder.Services.AddWolverineHttp();

// Add SignalR for real-time communication
builder.Services.AddSignalR();

// Add CORS — required for browser-side SignalR negotiate.
// Storefront.Web and Storefront.Api run on different ports, making them different origins
// under the browser Same-Origin Policy (e.g., http://localhost:5238 vs http://localhost:5237).
// Without CORS, the browser blocks the HTTP POST to /hub/storefront/negotiate, causing
// "TypeError: Failed to fetch" and preventing the SignalR WebSocket from being established.
builder.Services.AddCors(options =>
{
    // In production the BFF and web app are served from the same origin (reverse proxy),
    // so CORS is rarely needed there. In development and E2E tests each service runs on its
    // own port, making them different origins. We read allowed origins from configuration
    // so CI/dev environments can use "*" while a production appsettings can be restrictive.
    var allowedOrigins = builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>();

    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins is { Length: > 0 })
            policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
        else
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

// Add Wolverine with message handler discovery
builder.Host.UseWolverine(opts =>
{
    // Enable FluentValidation for all Wolverine handlers
    opts.UseFluentValidation();

    // Discover handlers in both API and Domain assemblies
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly); // Storefront.Api (Queries, Hub)
    opts.Discovery.IncludeAssembly(typeof(IStorefrontWebSocketMessage).Assembly); // Storefront (Notifications)

    // Configure SignalR transport
    opts.UseSignalR();

    // Configure publishing rules: route all IStorefrontWebSocketMessage to SignalR
    opts.Publish(x =>
    {
        x.MessagesImplementing<IStorefrontWebSocketMessage>();
        x.ToSignalR();
    });

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

    // Subscribe to Fulfillment BC integration messages
    opts.ListenToRabbitQueue("storefront-fulfillment-events")
        .ProcessInline();
});

// Add HTTP clients for downstream BC queries
builder.Services.AddHttpClient("ShoppingClient", client =>
{
    var url = builder.Configuration["ApiClients:ShoppingApiUrl"] ?? "http://localhost:5236";
    client.BaseAddress = new Uri(url);
});

builder.Services.AddHttpClient("OrdersClient", client =>
{
    var url = builder.Configuration["ApiClients:OrdersApiUrl"] ?? "http://localhost:5231";
    client.BaseAddress = new Uri(url);
});

builder.Services.AddHttpClient("CustomerIdentityClient", client =>
{
    var url = builder.Configuration["ApiClients:CustomerIdentityApiUrl"] ?? "http://localhost:5235";
    client.BaseAddress = new Uri(url);
});

builder.Services.AddHttpClient("CatalogClient", client =>
{
    var url = builder.Configuration["ApiClients:CatalogApiUrl"] ?? "http://localhost:5133";
    client.BaseAddress = new Uri(url);
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

// Map Aspire default endpoints (/health, /alive)
app.MapDefaultEndpoints();

// Enable CORS — must be placed before MapWolverineEndpoints and MapHub so that
// the negotiate POST to /hub/storefront/negotiate includes CORS response headers.
app.UseCors();

// Map Wolverine HTTP endpoints
app.MapWolverineEndpoints(opts =>
{
    opts.UseFluentValidationProblemDetailMiddleware();
});

// Map SignalR hub
// DisableAntiforgery: ASP.NET Core 10+ enables antiforgery protection on SignalR hubs by default.
// WebSocket connections are CSRF-safe by design (browsers enforce same-origin on WS upgrades),
// so antiforgery tokens do not add security here and blocking the negotiation handshake breaks E2E tests.
app.MapHub<Storefront.Api.StorefrontHub>("/hub/storefront")
    .DisableAntiforgery();

// Redirect root to Swagger/API documentation
app.MapGet("/", (HttpResponse response) =>
{
    response.Headers.Append("Location", "/api");
    response.StatusCode = StatusCodes.Status301MovedPermanently;
}).ExcludeFromDescription();

app.Run();

// Make Program accessible for testing
public partial class Program;
