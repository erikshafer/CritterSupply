using Marten;
using ProductCatalog.Products;
using ProductCatalog.Shared;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Marten;

var builder = WebApplication.CreateBuilder(args);

// Marten document store configuration
var connectionString = builder.Configuration.GetConnectionString("Postgres")
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

var app = builder.Build();

// Wolverine HTTP endpoints with FluentValidation middleware
app.MapWolverineEndpoints(opts =>
{
    opts.UseFluentValidationProblemDetailMiddleware();
});

app.Run();

// Expose for integration tests
public partial class Program { }
