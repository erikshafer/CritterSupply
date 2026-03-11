using System.Text;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using VendorPortal.Api.Hubs;
using VendorPortal.RealTime;
using VendorPortal.VendorProductCatalog;
using Wolverine;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Marten;
using Wolverine.RabbitMQ;
using Wolverine.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Host.ApplyJasperFxExtensions();

// JWT Bearer authentication (validates tokens issued by VendorIdentity.Api)
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new Exception("Jwt:SigningKey not found in configuration");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "vendor-identity";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "vendor-portal";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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

        // Support JWT from query string for SignalR WebSocket connections
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub/vendor-portal"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Marten document store — owns the VendorProductCatalog lookup and future Vendor Portal projections
var connectionString = builder.Configuration.GetConnectionString("postgres")
    ?? "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres";

builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = "vendorportal";
})
.UseLightweightSessions()
.IntegrateWithWolverine();

// CORS for VendorPortal.Web (Blazor WASM at port 5241)
builder.Services.AddCors(options =>
{
    options.AddPolicy("VendorPortalWeb", policy =>
    {
        policy.WithOrigins("http://localhost:5241")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// SignalR — also called by opts.UseSignalR(), but called here explicitly for CORS and hub options clarity
builder.Services.AddSignalR();

builder.Host.UseWolverine(opts =>
{
    // Include the API assembly (HTTP endpoints, dashboard)
    opts.Discovery.IncludeAssembly(typeof(VendorPortal.Api.Dashboard.GetDashboardEndpoint).Assembly);
    // Include the domain assembly (message handlers: VendorProductAssociatedHandler, analytics handlers, etc.)
    opts.Discovery.IncludeAssembly(typeof(VendorProductCatalogEntry).Assembly);

    // Configure SignalR transport for server→client push (Phase 3).
    // Phase 4 will upgrade to WolverineHub for bidirectional client→server routing.
    opts.UseSignalR();

    // Publish rules: route all tenant/user hub messages via Wolverine's SignalR transport.
    // These messages are delivered to all connected clients; the VendorPortalHub's group
    // management (OnConnectedAsync) gates which connections receive each message.
    opts.Publish(x =>
    {
        x.MessagesImplementing<IVendorTenantMessage>();
        x.ToSignalR();
    });

    opts.Publish(x =>
    {
        x.MessagesImplementing<IVendorUserMessage>();
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

    // Subscribe to VendorTenantCreated events published by Vendor Identity.
    // Initializes VendorAccount document with default notification preferences.
    opts.ListenToRabbitQueue("vendor-portal-tenant-created")
        .ProcessInline();

    // Subscribe to VendorProductAssociated events published by Product Catalog.
    opts.ListenToRabbitQueue("vendor-portal-product-associated")
        .ProcessInline();

    // Subscribe to Order events for sales analytics fan-out.
    opts.ListenToRabbitQueue("vendor-portal-order-placed")
        .ProcessInline();

    // Subscribe to Inventory events for low-stock alerts and inventory snapshot updates.
    opts.ListenToRabbitQueue("vendor-portal-low-stock-detected")
        .ProcessInline();

    opts.ListenToRabbitQueue("vendor-portal-inventory-adjusted")
        .ProcessInline();

    opts.ListenToRabbitQueue("vendor-portal-stock-replenished")
        .ProcessInline();

    // Subscribe to Product Catalog BC change request response messages
    opts.ListenToRabbitQueue("vendor-portal-description-change-approved")
        .ProcessInline();

    opts.ListenToRabbitQueue("vendor-portal-description-change-rejected")
        .ProcessInline();

    opts.ListenToRabbitQueue("vendor-portal-image-change-approved")
        .ProcessInline();

    opts.ListenToRabbitQueue("vendor-portal-image-change-rejected")
        .ProcessInline();

    opts.ListenToRabbitQueue("vendor-portal-data-correction-approved")
        .ProcessInline();

    opts.ListenToRabbitQueue("vendor-portal-data-correction-rejected")
        .ProcessInline();

    opts.ListenToRabbitQueue("vendor-portal-more-info-requested")
        .ProcessInline();

    // Subscribe to VendorTenantTerminated events for compensation (auto-reject in-flight change requests).
    opts.ListenToRabbitQueue("vendor-portal-tenant-terminated")
        .ProcessInline();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddWolverineHttp();

var app = builder.Build();

app.UseCors("VendorPortalWeb");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger(options => { options.RouteTemplate = "api/{documentName}/swagger.json"; });
    app.UseSwaggerUI(opts =>
    {
        opts.RoutePrefix = "api";
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Vendor Portal API");
    });
}

app.MapDefaultEndpoints();

app.MapWolverineEndpoints(opts =>
{
    opts.UseFluentValidationProblemDetailMiddleware();
});

app.MapHub<VendorPortalHub>("/hub/vendor-portal")
    .DisableAntiforgery(); // JWT-authenticated hub — safe to disable (no ambient browser credentials)

app.MapGet("/", (HttpResponse response) =>
{
    response.Headers.Append("Location", "/api");
    response.StatusCode = StatusCodes.Status301MovedPermanently;
}).ExcludeFromDescription();

return await app.RunJasperFxCommands(args);

public partial class Program { }
