using System.Text;
using JasperFx;
using JasperFx.Events.Projections;
using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Wolverine;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Marten;
using Wolverine.RabbitMQ;
using Wolverine.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Host.ApplyJasperFxExtensions();

// JWT Bearer authentication (validates tokens issued by BackofficeIdentity.Api)
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "https://localhost:5249";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "backoffice";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = jwtIssuer;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        // Support JWT from query string for SignalR WebSocket connections
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub/backoffice"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

// Authorization policies (from ADR 0031: Backoffice RBAC Model)
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CustomerService", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("cs-agent"))
    .AddPolicy("WarehouseClerk", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("warehouse-clerk"))
    .AddPolicy("OperationsManager", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("operations-manager"))
    .AddPolicy("Executive", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("executive"))
    .AddPolicy("PricingManager", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("pricing-manager"))
    .AddPolicy("ProductManager", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("product-manager"))
    .AddPolicy("CopyWriter", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("copy-writer"))
    .AddPolicy("SystemAdmin", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("system-admin"));

// Marten document store — owns BFF projections (AdminDailyMetrics, AlertFeedView, etc.)
builder.Services.AddMarten(opts =>
{
    var connectionString = builder.Configuration.GetConnectionString("postgres")
        ?? "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres";
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = "backoffice";

    // Snapshot projection for OrderNote (zero-lag queries, excludes deleted notes in projections)
    opts.Projections.Snapshot<Backoffice.OrderNote.OrderNote>(Marten.Events.Projections.SnapshotLifecycle.Inline);

    // BFF-owned projections (inline lifecycle: zero lag)
    // AdminDailyMetrics: Executive dashboard KPIs (Session 6)
    opts.Projections.Add<Backoffice.Projections.AdminDailyMetricsProjection>(ProjectionLifecycle.Inline);

    // AlertFeedView: Operations alert feed (Session 7)
    opts.Projections.Add<Backoffice.Projections.AlertFeedViewProjection>(ProjectionLifecycle.Inline);
})
.AddAsyncDaemon(JasperFx.Events.Daemon.DaemonMode.Solo)
.UseLightweightSessions()
.IntegrateWithWolverine();

// CORS for future Backoffice.Web (Blazor WASM at port 5244)
builder.Services.AddCors(options =>
{
    options.AddPolicy("BackofficeWeb", policy =>
    {
        policy.WithOrigins("http://localhost:5244")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Add HTTP clients for downstream BC queries
builder.Services.AddHttpClient("CustomerIdentityClient", client =>
{
    var url = builder.Configuration["ApiClients:CustomerIdentityApiUrl"] ?? "http://localhost:5235";
    client.BaseAddress = new Uri(url);
});

builder.Services.AddHttpClient("OrdersClient", client =>
{
    var url = builder.Configuration["ApiClients:OrdersApiUrl"] ?? "http://localhost:5231";
    client.BaseAddress = new Uri(url);
});

builder.Services.AddHttpClient("ReturnsClient", client =>
{
    var url = builder.Configuration["ApiClients:ReturnsApiUrl"] ?? "http://localhost:5245";
    client.BaseAddress = new Uri(url);
});

builder.Services.AddHttpClient("CorrespondenceClient", client =>
{
    var url = builder.Configuration["ApiClients:CorrespondenceApiUrl"] ?? "http://localhost:5248";
    client.BaseAddress = new Uri(url);
});

builder.Services.AddHttpClient("InventoryClient", client =>
{
    var url = builder.Configuration["ApiClients:InventoryApiUrl"] ?? "http://localhost:5233";
    client.BaseAddress = new Uri(url);
});

builder.Services.AddHttpClient("FulfillmentClient", client =>
{
    var url = builder.Configuration["ApiClients:FulfillmentApiUrl"] ?? "http://localhost:5234";
    client.BaseAddress = new Uri(url);
});

builder.Services.AddHttpClient("CatalogClient", client =>
{
    var url = builder.Configuration["ApiClients:CatalogApiUrl"] ?? "http://localhost:5133";
    client.BaseAddress = new Uri(url);
});

// Register HTTP client implementations
builder.Services.AddScoped<Backoffice.Clients.ICustomerIdentityClient, Backoffice.Api.Clients.CustomerIdentityClient>();
builder.Services.AddScoped<Backoffice.Clients.IOrdersClient, Backoffice.Api.Clients.OrdersClient>();
builder.Services.AddScoped<Backoffice.Clients.IReturnsClient, Backoffice.Api.Clients.ReturnsClient>();
builder.Services.AddScoped<Backoffice.Clients.ICorrespondenceClient, Backoffice.Api.Clients.CorrespondenceClient>();
builder.Services.AddScoped<Backoffice.Clients.IInventoryClient, Backoffice.Api.Clients.InventoryClient>();
builder.Services.AddScoped<Backoffice.Clients.IFulfillmentClient, Backoffice.Api.Clients.FulfillmentClient>();
builder.Services.AddScoped<Backoffice.Clients.ICatalogClient, Backoffice.Api.Clients.CatalogClient>();

// SignalR
builder.Services.AddSignalR();

builder.Host.UseWolverine(opts =>
{
    // CRITICAL: Auto-apply transactional middleware to handlers that use persistence
    opts.Policies.AutoApplyTransactions();

    // Include the API assembly (HTTP endpoints, dashboard queries)
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);

    // Include the domain assembly (integration message handlers, composition models)
    opts.Discovery.IncludeAssembly(typeof(Backoffice.RealTime.IBackofficeWebSocketMessage).Assembly);

    // Configure SignalR transport for server→client push
    opts.UseSignalR();

    // Publish rules: route all Backoffice WebSocket messages via SignalR transport
    opts.Publish(x =>
    {
        x.MessagesImplementing<Backoffice.RealTime.IBackofficeWebSocketMessage>();
        x.ToSignalR();
    });

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

    // RabbitMQ subscriptions for AdminDailyMetrics projection (Session 6)
    // Subscribe to Orders BC events for dashboard metrics
    opts.ListenToRabbitQueue("backoffice-order-placed").ProcessInline();
    opts.ListenToRabbitQueue("backoffice-order-cancelled").ProcessInline();

    // Subscribe to Payments BC events for dashboard metrics
    opts.ListenToRabbitQueue("backoffice-payment-captured").ProcessInline();
    opts.ListenToRabbitQueue("backoffice-payment-failed").ProcessInline();

    // RabbitMQ subscriptions for AlertFeedView projection (Session 7)
    // Subscribe to Inventory BC events for alerts
    opts.ListenToRabbitQueue("backoffice-low-stock-detected").ProcessInline();

    // Subscribe to Fulfillment BC events for alerts
    opts.ListenToRabbitQueue("backoffice-shipment-delivery-failed").ProcessInline();

    // Subscribe to Returns BC events for alerts
    opts.ListenToRabbitQueue("backoffice-return-expired").ProcessInline();

    // Future subscriptions (Sessions 8-9):
    // opts.ListenToRabbitQueue("backoffice-shipment-dispatched").ProcessInline();
    // opts.ListenToRabbitQueue("backoffice-stock-replenished").ProcessInline();
    // opts.ListenToRabbitQueue("backoffice-return-requested").ProcessInline();

    // Subscribe to Correspondence BC events
    // opts.ListenToRabbitQueue("backoffice-correspondence-failed").ProcessInline();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddWolverineHttp();

var app = builder.Build();

app.UseCors("BackofficeWeb");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger(options => { options.RouteTemplate = "api/{documentName}/swagger.json"; });
    app.UseSwaggerUI(opts =>
    {
        opts.RoutePrefix = "api";
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Backoffice API");
    });
}

app.MapDefaultEndpoints();

app.MapWolverineEndpoints(opts =>
{
    opts.UseFluentValidationProblemDetailMiddleware();
});

// SignalR hub for real-time notifications
// DisableAntiforgery: ASP.NET Core 10+ enables antiforgery protection on SignalR hubs by default.
// WebSocket connections are CSRF-safe by design (browsers enforce same-origin on WS upgrades),
// so antiforgery tokens do not add security here and blocking the negotiation handshake breaks E2E tests.
app.MapHub<Backoffice.Api.BackofficeHub>("/hub/backoffice")
    .DisableAntiforgery();

app.MapGet("/", (HttpResponse response) =>
{
    response.Headers.Append("Location", "/api");
    response.StatusCode = StatusCodes.Status301MovedPermanently;
}).ExcludeFromDescription();

return await app.RunJasperFxCommands(args);

public partial class Program { }
