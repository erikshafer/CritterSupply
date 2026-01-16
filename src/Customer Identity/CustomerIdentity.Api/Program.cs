using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using CustomerIdentity.AddressBook;
using JasperFx;
using JasperFx.Resources;
using Marten;
using Weasel.Core;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Marten;

var builder = WebApplication.CreateBuilder(args);
builder.Host.ApplyJasperFxExtensions();

var martenConnectionString = builder.Configuration.GetConnectionString("marten")
                             ?? "Host=localhost;Port=5432;Database=critter_supply_customers;Username=postgres;Password=postgres";

builder.Services.AddMarten(opts =>
    {
        opts.Connection(martenConnectionString);
        opts.AutoCreateSchemaObjects = AutoCreate.All;
        opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        opts.DatabaseSchemaName = "customers";
        opts.DisableNpgsqlLogging = true;

        // Configure document storage for CustomerAddress
        opts.Schema.For<CustomerAddress>()
            .Index(x => x.CustomerId)
            .Index(x => x.IsDefault);
    })
    .UseLightweightSessions()
    .IntegrateWithWolverine();

builder.Services.AddResourceSetupOnStartup();

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
