using Marten;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ProductCatalog.Products;
using ProductCatalog.Shared;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Marten;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry configuration for Wolverine tracing and metrics
builder.Services.AddOpenTelemetry()
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

// Marten document store configuration
var connectionString = builder.Configuration.GetConnectionString("marten")
    ?? "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres";

builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = "productcatalog"; // Use dedicated schema in shared database

    // Product document configuration
    opts.Schema.For<Product>()
        .Index(x => x.Sku)         // Index SKU for queries
        .Index(x => x.Category)    // Index for category queries
        .Index(x => x.Status)      // Index for status filtering
        .SoftDeleted();            // Built-in soft delete support
})
    .UseLightweightSessions()
    .IntegrateWithWolverine();

// Wolverine messaging and HTTP
builder.Host.UseWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);
    opts.Discovery.IncludeAssembly(typeof(Product).Assembly);
    opts.UseFluentValidation();
});

// Wolverine HTTP
builder.Services.AddWolverineHttp();

// Register services
builder.Services.AddSingleton<IImageValidator, StubImageValidator>();

// Add Swagger/OpenAPI support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Seed product data in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
    await ProductCatalog.Products.SeedData.SeedProductsAsync(store);
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
