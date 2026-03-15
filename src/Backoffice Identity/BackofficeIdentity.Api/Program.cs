using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Serialization;
using BackofficeIdentity.Api.Auth;
using BackofficeIdentity.Authentication;
using BackofficeIdentity.Identity;
using JasperFx;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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

builder.Services.AddDbContext<BackofficeIdentityDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.ConfigureSystemTextJsonForWolverineOrMinimalApi(opts =>
{
    opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    opts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// JWT configuration
builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

// JWT Authentication (for protected endpoints)
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"]
    ?? throw new Exception("Jwt:SecretKey not found in configuration");
var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new Exception("Jwt:Issuer not found in configuration");
var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new Exception("Jwt:Audience not found in configuration");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

// Authorization policies per ADR 0031
builder.Services.AddAuthorization(opts =>
{
    // Leaf policies (single role + SystemAdmin)
    opts.AddPolicy("CopyWriter", policy => policy.RequireRole("CopyWriter", "SystemAdmin"));
    opts.AddPolicy("PricingManager", policy => policy.RequireRole("PricingManager", "SystemAdmin"));
    opts.AddPolicy("WarehouseClerk", policy => policy.RequireRole("WarehouseClerk", "SystemAdmin"));
    opts.AddPolicy("CustomerService", policy => policy.RequireRole("CustomerService", "SystemAdmin"));
    opts.AddPolicy("OperationsManager", policy => policy.RequireRole("OperationsManager", "SystemAdmin"));
    opts.AddPolicy("Executive", policy => policy.RequireRole("Executive", "SystemAdmin"));
    opts.AddPolicy("SystemAdmin", policy => policy.RequireRole("SystemAdmin"));

    // Composite policies
    opts.AddPolicy("PricingManagerOrAbove", policy =>
        policy.RequireRole("PricingManager", "OperationsManager", "SystemAdmin"));
    opts.AddPolicy("CustomerServiceOrAbove", policy =>
        policy.RequireRole("CustomerService", "OperationsManager", "SystemAdmin"));
    opts.AddPolicy("WarehouseOrOperations", policy =>
        policy.RequireRole("WarehouseClerk", "OperationsManager", "SystemAdmin"));
});

builder.Host.UseWolverine(opts =>
{
    opts.Discovery.DisableConventionalDiscovery();
    // Discover handlers from the BackofficeIdentity domain assembly
    opts.Discovery.IncludeAssembly(typeof(BackofficeUser).Assembly);
    // Discover endpoints from the API assembly
    opts.Discovery.IncludeAssembly(typeof(JwtTokenGenerator).Assembly);

    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();

    opts.UseFluentValidation();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddWolverineHttp();

var app = builder.Build();

// Apply EF Core migrations on startup (development only)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeIdentityDbContext>();
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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Backoffice Identity API");
    });
}

// Map Aspire default endpoints (/health, /alive)
app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

app.MapWolverineEndpoints(opts =>
{
    opts.UseFluentValidationProblemDetailMiddleware();
});

app.MapGet("/", (HttpResponse response) =>
{
    response.Headers.Append("Location", "/api");
    response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status301MovedPermanently;
}).ExcludeFromDescription();

return await app.RunJasperFxCommands(args);

[ExcludeFromCodeCoverage]
public partial class Program { }
