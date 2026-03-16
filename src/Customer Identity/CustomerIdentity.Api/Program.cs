using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using CustomerIdentity.AddressBook;
using JasperFx;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

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
    opts.Discovery.DisableConventionalDiscovery();
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

// Configure authentication with Cookie (for customer login) + JWT (for Backoffice access)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "CritterSupply.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    })
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

builder.Services.AddAuthorization(opts =>
{
    // Backoffice policies for customer service access
    opts.AddPolicy("CustomerService", policy =>
    {
        policy.AuthenticationSchemes.Add("Backoffice");
        policy.RequireRole("CustomerService", "OperationsManager", "SystemAdmin");
    });

    opts.AddPolicy("OperationsManager", policy =>
    {
        policy.AuthenticationSchemes.Add("Backoffice");
        policy.RequireRole("OperationsManager", "SystemAdmin");
    });
});

builder.Services.AddWolverineHttp();

var app = builder.Build();

// Apply EF Core migrations on startup (development only)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CustomerIdentityDbContext>();
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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Customers API");
    });
}

app.UseAuthentication();
app.UseAuthorization();

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
