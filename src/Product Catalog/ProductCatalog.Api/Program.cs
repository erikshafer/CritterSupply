using Marten;
using Messages.Contracts.ProductCatalog;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ProductCatalog.Products;
using ProductCatalog.Shared;
using System.Text;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Marten;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// Marten document store configuration
var connectionString = builder.Configuration.GetConnectionString("postgres")
    ?? "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres";

builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = "productcatalog"; // Use dedicated schema in shared database

    // Keep existing Product document for migration reads
    opts.Schema.For<Product>()
        .Index(x => x.Sku)         // Index SKU for queries
        .Index(x => x.Category)    // Index for category queries
        .Index(x => x.Status)      // Index for status filtering
        .SoftDeleted();            // Built-in soft delete support

    // Event-sourced product catalog — CatalogProduct is the write-side aggregate
    opts.Projections.Snapshot<CatalogProduct>(Marten.Events.Projections.SnapshotLifecycle.Inline);

    // ProductCatalogView is the read model, kept in sync inline with event appends
    opts.Projections.Add<ProductCatalogViewProjection>(JasperFx.Events.Projections.ProjectionLifecycle.Inline);
})
    .UseLightweightSessions()
    .IntegrateWithWolverine();

// JWT Bearer authentication with multi-issuer support (Vendor + Backoffice)
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? "dev-only-signing-key-change-in-production-must-be-at-least-32-chars";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "vendor-identity";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "vendor-portal";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    // Vendor JWT scheme (Vendor Portal admins managing vendor-assigned products)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    })
    // Backoffice JWT scheme (Backoffice admins managing product catalog)
    .AddJwtBearer("Backoffice", options =>
    {
        options.Authority = "https://localhost:5249";
        options.Audience = "https://localhost:5249";
        if (builder.Environment.IsDevelopment())
        {
            options.RequireHttpsMetadata = false;
        }
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RoleClaimType = "role"
        };
    });

// Authorization policies
builder.Services.AddAuthorization(opts =>
{
    // Vendor admins can add/update/assign vendor-specific products
    opts.AddPolicy("VendorAdmin", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("Admin"));

    // Backoffice CopyWriter can update product descriptions
    opts.AddPolicy("CopyWriter", policy =>
    {
        policy.AuthenticationSchemes.Add("Backoffice");
        policy.RequireRole("CopyWriter", "ProductManager", "SystemAdmin");
    });

    // Backoffice ProductManager has full product catalog control
    opts.AddPolicy("ProductManager", policy =>
    {
        policy.AuthenticationSchemes.Add("Backoffice");
        policy.RequireRole("ProductManager", "SystemAdmin");
    });
});


// Wolverine messaging and HTTP
builder.Host.UseWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);
    opts.Discovery.IncludeAssembly(typeof(Product).Assembly);
    opts.UseFluentValidation();

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

    // Publish VendorProductAssociated events to the Vendor Portal exchange
    opts.PublishMessage<VendorProductAssociated>()
        .ToRabbitExchange("vendor-portal-product-associated");

    // Publish product lifecycle integration events
    opts.PublishMessage<ProductAdded>()
        .ToRabbitExchange("product-catalog-product-added");
    opts.PublishMessage<ProductDiscontinued>()
        .ToRabbitExchange("product-catalog-product-discontinued");
});

// Wolverine HTTP
builder.Services.AddWolverineHttp();

// Register services
builder.Services.AddSingleton<IImageValidator, StubImageValidator>();

// Add Swagger/OpenAPI support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Seed product data in development (but NOT during test runs)
// Test runs should seed their own data per-test for isolation
if (app.Environment.IsDevelopment() && !IsRunningInTests())
{
    using var scope = app.Services.CreateScope();
    var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
    await ProductCatalog.Products.SeedData.SeedProductsAsync(store);
}

static bool IsRunningInTests()
{
    // Detect if we're running inside xUnit test runner
    return AppDomain.CurrentDomain.GetAssemblies()
        .Any(a => a.FullName?.StartsWith("xunit") == true);
}

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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Product Catalog API");
    });
}

app.UseAuthentication();
app.UseAuthorization();

// Map Aspire default endpoints (/health, /alive)
app.MapDefaultEndpoints();

// Wolverine HTTP endpoints with FluentValidation middleware
app.MapWolverineEndpoints(opts =>
{
    opts.UseFluentValidationProblemDetailMiddleware();
});

// Redirect root to Swagger/API documentation
app.MapGet("/", (HttpResponse response) =>
{
    response.Headers.Append("Location", "/api");
    response.StatusCode = StatusCodes.Status301MovedPermanently;
}).ExcludeFromDescription();

app.Run();

// Expose for integration tests
public partial class Program { }
