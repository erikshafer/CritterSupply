using Marten;
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

// Add Wolverine with HTTP support
builder.Host.UseWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);

    // TODO: Configure RabbitMQ subscriptions for integration messages (Phase 2)
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
builder.Services.AddScoped<Storefront.Clients.IShoppingClient, Storefront.Clients.ShoppingClient>();
builder.Services.AddScoped<Storefront.Clients.IOrdersClient, Storefront.Clients.OrdersClient>();
builder.Services.AddScoped<Storefront.Clients.ICustomerIdentityClient, Storefront.Clients.CustomerIdentityClient>();
builder.Services.AddScoped<Storefront.Clients.ICatalogClient, Storefront.Clients.CatalogClient>();

var app = builder.Build();

// Map Wolverine HTTP endpoints
app.MapWolverineEndpoints();

app.Run();

// Make Program accessible for testing
public partial class Program;
