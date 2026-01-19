using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using CustomerIdentity.AddressBook;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;

var builder = WebApplication.CreateBuilder(args);
builder.Host.ApplyJasperFxExtensions();

// Configure EF Core with Postgres
var connectionString = builder.Configuration.GetConnectionString("postgres")
                       ?? throw new Exception("The connection string for the PostgreSQL database was not found");

builder.Services.AddDbContext<CustomerIdentityDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.ConfigureSystemTextJsonForWolverineOrMinimalApi(opts =>
{
    opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    opts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Host.UseWolverine(opts =>
{
    // Discover handlers from the CustomerIdentity assembly
    opts.Discovery.IncludeAssembly(typeof(CustomerAddress).Assembly);

    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    opts.UseFluentValidation();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register address verification service (stub for development)
builder.Services.AddSingleton<IAddressVerificationService, StubAddressVerificationService>();

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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Customers API");
    });
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
