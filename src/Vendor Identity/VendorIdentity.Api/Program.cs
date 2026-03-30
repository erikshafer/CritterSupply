using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Serialization;
using JasperFx;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VendorIdentity.Api.Auth;
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
builder.Services.AddDbContext<VendorIdentityDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("postgres")
                           ?? throw new Exception("The connection string for the PostgreSQL database was not found");
    options.UseNpgsql(connectionString);
});

builder.Services.ConfigureSystemTextJsonForWolverineOrMinimalApi(opts =>
{
    opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    opts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// JWT configuration
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
    ?? throw new Exception("JWT settings not found in configuration");
builder.Services.AddSingleton(jwtSettings);
builder.Services.AddSingleton<JwtTokenService>();

// JWT Bearer authentication (validates tokens issued by this API)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

// CORS for VendorPortal.Web (Blazor WASM at port 5241)
builder.Services.AddCors(options =>
{
    options.AddPolicy("VendorPortalWeb", policy =>
    {
        policy.WithOrigins("http://localhost:5241")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Required for cookies (refresh token)
    });
});

builder.Host.UseWolverine(opts =>
{
    opts.Discovery.DisableConventionalDiscovery();
    // Discover handlers from the VendorIdentity assembly
    opts.Discovery.IncludeAssembly(typeof(VendorIdentity.TenantManagement.VendorTenant).Assembly);
    // Discover auth endpoints from the API assembly
    opts.Discovery.IncludeAssembly(typeof(VendorLoginEndpoint).Assembly);

    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    opts.UseFluentValidation();

    // RabbitMQ configuration
    var rabbitMqHost = builder.Configuration["RabbitMQ:hostname"] ?? "localhost";
    opts.UseRabbitMq(rabbit =>
    {
        rabbit.HostName = rabbitMqHost;
    })
    .AutoProvision()
    .AutoPurgeOnStartup();

    // Publish user lifecycle events to Vendor Portal for team management read models
    opts.PublishMessage<Messages.Contracts.VendorIdentity.VendorTenantCreated>()
        .ToRabbitQueue("vendor-portal-tenant-created");

    opts.PublishMessage<Messages.Contracts.VendorIdentity.VendorUserInvited>()
        .ToRabbitQueue("vendor-portal-user-invited");

    opts.PublishMessage<Messages.Contracts.VendorIdentity.VendorUserActivated>()
        .ToRabbitQueue("vendor-portal-user-activated");

    opts.PublishMessage<Messages.Contracts.VendorIdentity.VendorUserDeactivated>()
        .ToRabbitQueue("vendor-portal-user-deactivated");

    opts.PublishMessage<Messages.Contracts.VendorIdentity.VendorUserReactivated>()
        .ToRabbitQueue("vendor-portal-user-reactivated");

    opts.PublishMessage<Messages.Contracts.VendorIdentity.VendorUserRoleChanged>()
        .ToRabbitQueue("vendor-portal-user-role-changed");

    opts.PublishMessage<Messages.Contracts.VendorIdentity.VendorUserInvitationResent>()
        .ToRabbitQueue("vendor-portal-invitation-resent");

    opts.PublishMessage<Messages.Contracts.VendorIdentity.VendorUserInvitationRevoked>()
        .ToRabbitQueue("vendor-portal-invitation-revoked");

    opts.PublishMessage<Messages.Contracts.VendorIdentity.VendorTenantSuspended>()
        .ToRabbitQueue("vendor-portal-tenant-suspended");

    opts.PublishMessage<Messages.Contracts.VendorIdentity.VendorTenantTerminated>()
        .ToRabbitQueue("vendor-portal-tenant-terminated");

    opts.PublishMessage<Messages.Contracts.VendorIdentity.VendorTenantReinstated>()
        .ToRabbitQueue("vendor-portal-tenant-reinstated");
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddWolverineHttp();

var app = builder.Build();

app.UseCors("VendorPortalWeb");

app.UseAuthentication();
app.UseAuthorization();

// Apply EF Core migrations on startup (development only)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<VendorIdentityDbContext>();
    await dbContext.Database.MigrateAsync();
    await VendorIdentitySeedData.SeedAsync(dbContext);
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
