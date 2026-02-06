using Marten;
using Storefront.Notifications;
using Wolverine;
using Wolverine.Http;

var builder = WebApplication.CreateBuilder(args);

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

    // TODO (Phase 2b): Configure RabbitMQ subscriptions for integration messages
    // When Shopping.Api and Orders.Api are configured to publish messages to RabbitMQ:
    // - Subscribe to Shopping.ItemAdded, Shopping.ItemRemoved, Shopping.ItemQuantityChanged
    // - Subscribe to Orders.OrderPlaced, Payments.PaymentCaptured
    //
    // For now, SSE infrastructure is tested via Wolverine.InvokeMessageAndWaitAsync
    // which directly injects messages into handlers without needing RabbitMQ
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

var app = builder.Build();

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
