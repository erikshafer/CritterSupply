using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Serialization;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using JasperFx.Resources;
using Marten;
using Marten.Events.Projections;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Pricing;
using Pricing.Products;
using Weasel.Core;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Marten;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

builder.Host.ApplyJasperFxExtensions();

var connectionString = builder.Configuration.GetConnectionString("postgres")
                       ?? throw new Exception("The connection string 'postgres' was not found");

builder.Services.AddMarten(opts =>
    {
        opts.Connection(connectionString);
        opts.AutoCreateSchemaObjects = AutoCreate.All;
        opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        opts.DatabaseSchemaName = Constants.Pricing.ToLowerInvariant();
        opts.DisableNpgsqlLogging = true;

        // Configure ProductPrice as an event-sourced aggregate
        opts.Events.StreamIdentity = StreamIdentity.AsGuid;

        // Configure ProductPrice aggregate snapshot for efficient event-replay reads
        opts.Projections.Snapshot<ProductPrice>(SnapshotLifecycle.Inline);

        // Configure CurrentPriceView inline projection for hot-path queries
        opts.Projections.Add<CurrentPriceViewProjection>(ProjectionLifecycle.Inline);
    })
    .AddAsyncDaemon(DaemonMode.Solo)
    .UseLightweightSessions()
    .IntegrateWithWolverine(config =>
    {
        config.UseWolverineManagedEventSubscriptionDistribution = true;
    });

builder.Services.AddResourceSetupOnStartup();

builder.Services.ConfigureSystemTextJsonForWolverineOrMinimalApi(opts =>
{
    opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    opts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// JWT Bearer authentication with multi-issuer support (Vendor + Backoffice)
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? "dev-only-signing-key-change-in-production-must-be-at-least-32-chars";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "vendor-identity";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "vendor-portal";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    // Vendor JWT scheme (Vendor Portal admins managing pricing)
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
    // Backoffice JWT scheme (Backoffice PricingManager role)
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
    // Backoffice PricingManager can set base prices and manage schedules
    opts.AddPolicy("PricingManager", policy =>
    {
        policy.AuthenticationSchemes.Add("Backoffice");
        policy.RequireRole("PricingManager", "SystemAdmin");
    });
});

builder.Host.UseWolverine(opts =>
{
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    opts.OnException<ConcurrencyException>()
        .RetryOnce()
        .Then.RetryWithCooldown(100.Milliseconds(), 250.Milliseconds())
        .Then.Discard();

    opts.UseFluentValidation();

    // Discover all handlers in the Pricing domain assembly
    opts.Discovery.IncludeAssembly(typeof(ProductPrice).Assembly);

    // Discover HTTP endpoints in the Pricing.Api assembly
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);

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

    // Listen for ProductAdded from Product Catalog BC
    opts.ListenToRabbitQueue("pricing-product-added")
        .ProcessInline();
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
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Pricing API");
    });
}

app.UseAuthentication();
app.UseAuthorization();

// Map Aspire default endpoints (/health, /alive)
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapHealthChecks("/api/v1/health").AllowAnonymous();
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

// Seed prices for demo (development only)
if (app.Environment.IsDevelopment())
{
    var store = app.Services.GetRequiredService<IDocumentStore>();
    await SeedPricesAsync(store);
}

return await app.RunJasperFxCommands(args);

static async Task SeedPricesAsync(IDocumentStore store)
{
    await using var session = store.LightweightSession();
    var existing = await session.Query<CurrentPriceView>().CountAsync();
    if (existing > 0) return; // idempotent

    var now = DateTimeOffset.UtcNow;
    var systemId = Guid.Empty;

    var seedPrices = new Dictionary<string, decimal>
    {
        ["DOG-BOWL-001"]      = 12.99m,
        ["DOG-TOY-ROPE"]      = 8.99m,
        ["DOG-COLLAR-LED"]    = 24.99m,
        ["DOG-BED-ORTHO"]     = 59.99m,
        ["DOG-TREATS-CHK"]    = 14.99m,
        ["CAT-TREE-5FT"]      = 89.99m,
        ["CAT-LITTER-CLM"]    = 22.99m,
        ["CAT-TOY-LASER"]     = 9.99m,
        ["CAT-FOUNTAIN-2L"]   = 34.99m,
        ["CAT-CARRIER-SM"]    = 44.99m,
        ["BIRD-CAGE-MED"]     = 49.99m,
        ["BIRD-SEED-MIX"]     = 15.99m,
        ["BIRD-TOY-SWING"]    = 7.99m,
        ["FISH-TANK-20G"]     = 79.99m,
        ["FISH-FOOD-FLAKE"]   = 8.99m,
        ["FISH-DECOR-CAVE"]   = 12.99m,
        ["HAMSTER-CAGE-LG"]   = 39.99m,
        ["RABBIT-HAY-5LB"]    = 19.99m,
        ["GUINEA-PIG-HIDEY"]  = 14.99m,
        ["REPTILE-TANK-40G"]  = 99.99m,
        ["REPTILE-LAMP-UVB"]  = 29.99m,
        ["REPTILE-SUBSTRATE"] = 17.99m,
        ["PET-GROOMING-KIT"]  = 29.99m,
        ["PET-GATE-WIDE"]     = 54.99m,
        ["PET-CAMERA-WIFI"]   = 149.99m,
        ["PET-FIRST-AID"]     = 24.99m,
        ["XMAS-PET-SWEATER"]  = 19.99m,
    };

    foreach (var (sku, amount) in seedPrices)
    {
        var streamId = ProductPrice.StreamId(sku);
        session.Events.StartStream<ProductPrice>(streamId,
            new ProductRegistered(streamId, sku, now),
            new InitialPriceSet(streamId, sku, Money.Of(amount), null, null, systemId, now));
    }

    await session.SaveChangesAsync();
}

[ExcludeFromCodeCoverage]
public partial class Program { }
