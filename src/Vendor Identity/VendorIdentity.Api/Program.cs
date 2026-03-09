using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

builder.Host.ApplyJasperFxExtensions();

// Configure EF Core with Postgres
var connectionString = builder.Configuration.GetConnectionString("postgres")
                       ?? throw new Exception("The connection string for the PostgreSQL database was not found");

builder.Services.AddDbContext<VendorIdentityDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.ConfigureSystemTextJsonForWolverineOrMinimalApi(opts =>
{
    opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    opts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Host.UseWolverine(opts =>
{
    opts.Discovery.DisableConventionalDiscovery();
    // Discover handlers from the VendorIdentity assembly
    opts.Discovery.IncludeAssembly(typeof(VendorIdentity.TenantManagement.VendorTenant).Assembly);

    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    opts.UseFluentValidation();

    // RabbitMQ configuration
    var rabbitMqHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
    opts.UseRabbitMq(rabbit =>
    {
        rabbit.HostName = rabbitMqHost;
    })
    .AutoProvision()
    .AutoPurgeOnStartup();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddWolverineHttp();

var app = builder.Build();

// Apply EF Core migrations on startup (development only)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<VendorIdentityDbContext>();
    await dbContext.Database.MigrateAsync();
}

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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Vendor Identity API");
    });
}

// Map Aspire default endpoints (/health, /alive)
app.MapDefaultEndpoints();

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
