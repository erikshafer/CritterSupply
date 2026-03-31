using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Marten;
using Testcontainers.PostgreSql;
using Wolverine;
using Backoffice.E2ETests.Stubs;
using Backoffice.Clients;

namespace Backoffice.E2ETests;

/// <summary>
/// E2E test fixture that starts real Kestrel servers for BackofficeIdentity.Api,
/// Backoffice.Api, and a static file host serving Backoffice.Web (Blazor WASM),
/// backed by TestContainers PostgreSQL instances.
///
/// Architecture:
///   Playwright Browser (Chromium)
///         │
///         ▼
///   Backoffice.Web (WASM static files served by thin ASP.NET host, random port)
///         │ (cross-origin HTTP + WebSocket)
///         ├──────────────────────────┐
///         ▼                          ▼
///   Backoffice.Api                BackofficeIdentity.Api
///   (Kestrel, random port)        (Kestrel, random port)
///   ├── Marten (backoffice)       ├── EF Core (backofficeidentity)
///   ├── SignalR hub               ├── JWT issuance
///   ├── Wolverine (local only)    └── Demo account seeding
///   ├── Domain BC stubs
///   └── JWT validation
///         │                          │
///         └── Shared PostgreSQL ─────┘
///             (TestContainers)
///
/// Key constraints:
///   - All services use REAL Kestrel (not TestServer) — Playwright requires TCP ports.
///   - BackofficeIdentity.Api is a real server (not stubbed) — E2E tests need real JWTs.
///   - Domain BC clients (Orders, Returns, Inventory, etc.) are stubbed.
///   - CORS is opened to allow the WASM host's random-port origin.
///   - RabbitMQ transports are disabled — Wolverine runs in local-only mode.
/// </summary>
public sealed class E2ETestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("backoffice_e2e_test_db")
        .WithName($"backoffice-e2e-postgres-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private BackofficeIdentityApiKestrelFactory? _identityFactory;
    private BackofficeApiKestrelFactory? _backofficeApiFactory;
    private WasmStaticFileHost? _wasmHost;
    private StubListingsApiHost? _listingsApiHost;
    private StubMarketplacesApiHost? _marketplacesApiHost;

    /// <summary>Stub clients to configure domain BC behavior per scenario.</summary>
    public StubOrdersClient StubOrdersClient { get; } = new();
    public StubReturnsClient StubReturnsClient { get; } = new();
    public StubInventoryClient StubInventoryClient { get; } = new();
    public StubCustomerIdentityClient StubCustomerIdentityClient { get; } = new();
    public StubCatalogClient StubCatalogClient { get; } = new();
    public StubFulfillmentClient StubFulfillmentClient { get; } = new();
    public StubCorrespondenceClient StubCorrespondenceClient { get; } = new();
    public StubPricingClient StubPricingClient { get; } = new();
    public StubBackofficeIdentityClient StubBackofficeIdentityClient { get; } = new();

    /// <summary>Stub Listings API host for E2E scenarios that need listing data.</summary>
    internal StubListingsApiHost StubListingsApi { get; } = new();

    /// <summary>Stub Marketplaces API host for E2E scenarios that need marketplace/category mapping data.</summary>
    internal StubMarketplacesApiHost StubMarketplacesApi { get; } = new();

    /// <summary>Base URL of Backoffice.Web WASM host — what Playwright navigates to.</summary>
    public string WasmBaseUrl { get; private set; } = string.Empty;

    /// <summary>Base URL of Backoffice.Api — for direct API calls in test hooks.</summary>
    public string BackofficeApiBaseUrl { get; private set; } = string.Empty;

    /// <summary>Base URL of BackofficeIdentity.Api — for direct API calls in test hooks.</summary>
    public string IdentityApiBaseUrl { get; private set; } = string.Empty;

    /// <summary>Direct access to Backoffice.Api host for SignalR hub context injection.</summary>
    public IHost BackofficeApiHost { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Step 1: Start TestContainers PostgreSQL
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        // Step 2: Start BackofficeIdentity.Api (EF Core — JWT issuer)
        _identityFactory = new BackofficeIdentityApiKestrelFactory(connectionString);
        _identityFactory.StartKestrel();
        IdentityApiBaseUrl = _identityFactory.ServerAddress;

        // Step 3: Start Backoffice.Api (Marten — BFF, SignalR)
        _backofficeApiFactory = new BackofficeApiKestrelFactory(
            connectionString,
            IdentityApiBaseUrl,
            StubOrdersClient,
            StubReturnsClient,
            StubInventoryClient,
            StubCustomerIdentityClient,
            StubCatalogClient,
            StubFulfillmentClient,
            StubCorrespondenceClient,
            StubPricingClient,
            StubBackofficeIdentityClient);
        _backofficeApiFactory.StartKestrel();
        BackofficeApiBaseUrl = _backofficeApiFactory.ServerAddress;
        BackofficeApiHost = _backofficeApiFactory.Services.GetRequiredService<IHost>();

        // Step 4: Start stub Listings API (returns mock listing data for Backoffice.Web)
        _listingsApiHost = StubListingsApi;
        await _listingsApiHost.StartAsync();

        // Step 5: Start stub Marketplaces API (returns mock marketplace/category data for Backoffice.Web)
        _marketplacesApiHost = StubMarketplacesApi;
        await _marketplacesApiHost.StartAsync();

        // Step 6: Start WASM static file host serving Backoffice.Web with test API URLs
        _wasmHost = new WasmStaticFileHost(IdentityApiBaseUrl, BackofficeApiBaseUrl, _listingsApiHost.BaseUrl, _marketplacesApiHost.BaseUrl);
        await _wasmHost.StartAsync();
        WasmBaseUrl = _wasmHost.BaseUrl;
    }

    public async Task DisposeAsync()
    {
        if (_wasmHost != null) await _wasmHost.DisposeAsync();
        if (_marketplacesApiHost != null) await _marketplacesApiHost.DisposeAsync();
        if (_listingsApiHost != null) await _listingsApiHost.DisposeAsync();
        if (_backofficeApiFactory != null) await _backofficeApiFactory.DisposeAsync();
        if (_identityFactory != null) await _identityFactory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Cleans all Marten document/event data from the Backoffice test database.
    /// Call in DataHooks.AfterScenario for complete test isolation.
    /// Note: BackofficeIdentity EF Core seed data (admin users) is NOT cleaned — it's shared.
    /// </summary>
    public async Task CleanMartenDataAsync()
    {
        var store = _backofficeApiFactory?.Services.GetRequiredService<IDocumentStore>();
        if (store != null)
        {
            await store.Advanced.Clean.DeleteAllDocumentsAsync();
            await store.Advanced.Clean.DeleteAllEventDataAsync();
        }
    }

    /// <summary>
    /// Clears all stub client data between scenarios for test isolation.
    /// </summary>
    public void ClearAllStubs()
    {
        StubOrdersClient.Clear();
        StubReturnsClient.Clear();
        StubInventoryClient.Clear();
        StubCustomerIdentityClient.Clear();
        StubCatalogClient.Clear();
        StubFulfillmentClient.Clear();
        StubCorrespondenceClient.Clear();
        StubPricingClient.Clear();
        StubListingsApi.Clear();
        StubMarketplacesApi.Clear();

        // Reset session-expired simulation flags
        StubInventoryClient.SimulateSessionExpired = false;
        StubOrdersClient.SimulateSessionExpired = false;
        StubCustomerIdentityClient.SimulateSessionExpired = false;
        StubPricingClient.SimulateSessionExpired = false;
    }

    /// <summary>
    /// Seeds an admin user into BackofficeIdentity EF Core database.
    /// Used by authentication scenario steps to create test admin accounts.
    /// </summary>
    public void SeedAdminUser(Guid userId, string email, string fullName, string password)
    {
        if (_identityFactory == null)
            throw new InvalidOperationException("BackofficeIdentity factory not initialized. Call InitializeAsync() first.");

        using var scope = _identityFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeIdentity.Identity.BackofficeIdentityDbContext>();

        // Check if user already exists (by ID or email — demo seed data may use different IDs)
        if (dbContext.Users.Any(u => u.Id == userId || u.Email == email))
            return; // Already seeded

        // Split full name into first and last name
        var nameParts = fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : "Test";
        var lastName = nameParts.Length > 1 ? nameParts[1] : "User";

        // Use ASP.NET Core Identity's PasswordHasher (PBKDF2-SHA256) - matches production code
        var passwordHasher = new Microsoft.AspNetCore.Identity.PasswordHasher<BackofficeIdentity.Identity.BackofficeUser>();
        var passwordHash = passwordHasher.HashPassword(null!, password);

        var adminUser = new BackofficeIdentity.Identity.BackofficeUser
        {
            Id = userId,
            Email = email,
            PasswordHash = passwordHash,
            FirstName = firstName,
            LastName = lastName,
            Role = BackofficeIdentity.Identity.BackofficeRole.SystemAdmin, // Default to SystemAdmin for E2E tests
            Status = BackofficeIdentity.Identity.BackofficeUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.Users.Add(adminUser);
        dbContext.SaveChanges();
    }

    /// <summary>
    /// Seeds an admin user with a specific role into BackofficeIdentity EF Core database.
    /// Used by authorization scenario steps to create test admin accounts with specific roles.
    /// </summary>
    public void SeedAdminUserWithRole(Guid userId, string email, string fullName, string password, string role)
    {
        if (_identityFactory == null)
            throw new InvalidOperationException("BackofficeIdentity factory not initialized. Call InitializeAsync() first.");

        using var scope = _identityFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeIdentity.Identity.BackofficeIdentityDbContext>();

        // Check if user already exists (by ID or email — demo seed data may use different IDs)
        if (dbContext.Users.Any(u => u.Id == userId || u.Email == email))
            return; // Already seeded

        // Split full name into first and last name
        var nameParts = fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : "Test";
        var lastName = nameParts.Length > 1 ? nameParts[1] : "User";

        // Use ASP.NET Core Identity's PasswordHasher (PBKDF2-SHA256) - matches production code
        var passwordHasher = new Microsoft.AspNetCore.Identity.PasswordHasher<BackofficeIdentity.Identity.BackofficeUser>();
        var passwordHash = passwordHasher.HashPassword(null!, password);

        // Map role string to BackofficeRole enum
        var backofficeRole = role switch
        {
            "system-admin" => BackofficeIdentity.Identity.BackofficeRole.SystemAdmin,
            "operations-manager" => BackofficeIdentity.Identity.BackofficeRole.OperationsManager,
            "warehouse-clerk" => BackofficeIdentity.Identity.BackofficeRole.WarehouseClerk,
            "customer-service" => BackofficeIdentity.Identity.BackofficeRole.CustomerService,
            "copy-writer" => BackofficeIdentity.Identity.BackofficeRole.CopyWriter,
            "pricing-manager" => BackofficeIdentity.Identity.BackofficeRole.PricingManager,
            "product-manager" => BackofficeIdentity.Identity.BackofficeRole.SystemAdmin, // TEMP: ProductManager not in enum yet, map to SystemAdmin
            "executive" => BackofficeIdentity.Identity.BackofficeRole.Executive,
            "finance-clerk" => BackofficeIdentity.Identity.BackofficeRole.Executive, // finance-clerk not in enum; Executive has similar limited access
            // PascalCase aliases for convenience in Gherkin scenarios
            "ProductManager" => BackofficeIdentity.Identity.BackofficeRole.SystemAdmin, // TEMP: map to SystemAdmin
            "CopyWriter" => BackofficeIdentity.Identity.BackofficeRole.CopyWriter,
            "PricingManager" => BackofficeIdentity.Identity.BackofficeRole.PricingManager,
            "SystemAdmin" => BackofficeIdentity.Identity.BackofficeRole.SystemAdmin,
            _ => throw new ArgumentException($"Unknown role: {role}")
        };

        var adminUser = new BackofficeIdentity.Identity.BackofficeUser
        {
            Id = userId,
            Email = email,
            PasswordHash = passwordHash,
            FirstName = firstName,
            LastName = lastName,
            Role = backofficeRole,
            Status = BackofficeIdentity.Identity.BackofficeUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.Users.Add(adminUser);
        dbContext.SaveChanges();
    }

    /// <summary>
    /// Seeds an admin user with multiple roles into BackofficeIdentity EF Core database.
    /// Used by authorization scenario steps to test multi-role users.
    /// Note: BackofficeUser currently supports single role only, so this picks the first role.
    /// If multi-role support is needed, BackofficeIdentity schema needs updating.
    /// </summary>
    public void SeedAdminUserWithRoles(Guid userId, string email, string fullName, string password, string[] roles)
    {
        // For now, just seed with first role since BackofficeUser has single Role property
        // If multi-role support is needed, BackofficeIdentity BC needs schema migration
        if (roles.Length == 0)
            throw new ArgumentException("At least one role required");

        SeedAdminUserWithRole(userId, email, fullName, password, roles[0]);
    }

    /// <summary>
    /// Seeds standard test data for E2E scenarios (customers, orders, returns, alerts).
    /// </summary>
    public void SeedStandardScenario()
    {
        // Seed customer
        StubCustomerIdentityClient.AddCustomer(
            WellKnownTestData.Customers.TestCustomer,
            WellKnownTestData.Customers.TestCustomerEmail,
            WellKnownTestData.Customers.TestCustomerName);

        // Seed order
        StubOrdersClient.AddOrder(
            WellKnownTestData.Orders.TestOrder,
            WellKnownTestData.Customers.TestCustomer,
            "Confirmed",
            DateTimeOffset.UtcNow.AddDays(-2),
            WellKnownTestData.Orders.TestOrderTotal);

        // Seed return
        StubReturnsClient.AddReturn(
            WellKnownTestData.Returns.TestReturn,
            WellKnownTestData.Orders.TestOrder,
            WellKnownTestData.Customers.TestCustomer,
            "Requested",
            DateTimeOffset.UtcNow.AddDays(-1));

        // Seed low-stock alert
        StubInventoryClient.AddLowStockAlert(
            WellKnownTestData.Alerts.LowStockAlert,
            WellKnownTestData.Alerts.LowStockSku,
            10,
            50,
            DateTimeOffset.UtcNow.AddHours(-2),
            isAcknowledged: false);

        // Seed catalog product
        StubCatalogClient.AddProduct(
            WellKnownTestData.Products.CeramicDogBowl,
            "Ceramic Dog Bowl",
            "Premium ceramic feeding bowl for dogs",
            WellKnownTestData.Products.CeramicDogBowlPrice);
    }
}


/// <summary>
/// WebApplicationFactory for BackofficeIdentity.Api that binds to a real Kestrel TCP port.
/// Runs the real EF Core service with auto-seeded demo accounts.
/// Uses BackofficeIdentity.Api.TestMarker as anchor.
/// </summary>
internal sealed class BackofficeIdentityApiKestrelFactory(string connectionString)
    : WebApplicationFactory<BackofficeIdentity.Api.TestMarker>
{
    public string ServerAddress { get; private set; } = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Set environment to Development so EF Core migrations and demo seed data run
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:postgres"] = connectionString
            });
        });

        builder.ConfigureServices(services =>
        {
            // Disable external Wolverine transports (RabbitMQ)
            services.DisableAllExternalWolverineTransports();

            // Open CORS for test (random ports)
            services.AddCors(opts =>
            {
                opts.AddDefaultPolicy(policy => policy
                    .SetIsOriginAllowed(_ => true)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());
            });
        });
    }

    internal void StartKestrel()
    {
        UseKestrel(0);
        CreateDefaultClient();

        var serverAddresses = Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();

        var rawAddress = serverAddresses?.Addresses.FirstOrDefault() ?? string.Empty;
        ServerAddress = NormalizeAddress(rawAddress);
    }

    internal static string NormalizeAddress(string rawAddress) => rawAddress
        .Replace("//[::]:", "//localhost:")
        .Replace("//0.0.0.0:", "//localhost:");
}

/// <summary>
/// WebApplicationFactory for Backoffice.Api that binds to a real Kestrel TCP port.
/// Uses TestContainers Postgres for Marten and validates JWTs from the test BackofficeIdentity.Api.
/// Stubs all domain BC clients.
/// Uses Backoffice.Api.BackofficeHub as anchor (Program ambiguous).
/// </summary>
internal sealed class BackofficeApiKestrelFactory(
    string connectionString,
    string identityApiUrl,
    StubOrdersClient stubOrdersClient,
    StubReturnsClient stubReturnsClient,
    StubInventoryClient stubInventoryClient,
    StubCustomerIdentityClient stubCustomerIdentityClient,
    StubCatalogClient stubCatalogClient,
    StubFulfillmentClient stubFulfillmentClient,
    StubCorrespondenceClient stubCorrespondenceClient,
    StubPricingClient stubPricingClient,
    StubBackofficeIdentityClient stubBackofficeIdentityClient)
    : WebApplicationFactory<Backoffice.Api.BackofficeHub>
{
    public string ServerAddress { get; private set; } = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:postgres"] = connectionString,
                ["ApiClients:BackofficeIdentityApiUrl"] = identityApiUrl
            });
        });

        builder.ConfigureServices(services =>
        {
            // Override Marten connection to use TestContainers Postgres
            services.ConfigureMarten(opts => opts.Connection(connectionString));

            // Replace real HTTP clients with stubs
            services.RemoveAndReplaceClient<IOrdersClient>(stubOrdersClient);
            services.RemoveAndReplaceClient<IReturnsClient>(stubReturnsClient);
            services.RemoveAndReplaceClient<IInventoryClient>(stubInventoryClient);
            services.RemoveAndReplaceClient<ICustomerIdentityClient>(stubCustomerIdentityClient);
            services.RemoveAndReplaceClient<ICatalogClient>(stubCatalogClient);
            services.RemoveAndReplaceClient<IFulfillmentClient>(stubFulfillmentClient);
            services.RemoveAndReplaceClient<ICorrespondenceClient>(stubCorrespondenceClient);
            services.RemoveAndReplaceClient<IPricingClient>(stubPricingClient);
            services.RemoveAndReplaceClient<IBackofficeIdentityClient>(stubBackofficeIdentityClient);

            // Disable external Wolverine transports (RabbitMQ)
            services.DisableAllExternalWolverineTransports();

            // Open CORS for test (random ports)
            services.AddCors(opts =>
            {
                opts.AddDefaultPolicy(policy => policy
                    .SetIsOriginAllowed(_ => true)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());
            });
        });
    }

    internal void StartKestrel()
    {
        UseKestrel(0);
        CreateDefaultClient();

        var serverAddresses = Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();

        var rawAddress = serverAddresses?.Addresses.FirstOrDefault() ?? string.Empty;
        ServerAddress = BackofficeIdentityApiKestrelFactory.NormalizeAddress(rawAddress);
    }
}

/// <summary>
/// Lightweight ASP.NET Core host that serves the compiled Blazor WASM static files
/// from Backoffice.Web's build output, with a test-specific appsettings.json
/// that points at the test API server URLs.
///
/// Blazor WASM apps fetch appsettings.json via HTTP from their own origin at startup.
/// This host intercepts that request and returns the test configuration dynamically.
/// </summary>
internal sealed class WasmStaticFileHost : IAsyncDisposable
{
    private readonly string _identityApiUrl;
    private readonly string _backofficeApiUrl;
    private WebApplication? _app;

    public string BaseUrl { get; private set; } = string.Empty;

    private readonly string _listingsApiUrl;
    private readonly string _marketplacesApiUrl;

    public WasmStaticFileHost(string identityApiUrl, string backofficeApiUrl, string listingsApiUrl, string marketplacesApiUrl)
    {
        _identityApiUrl = identityApiUrl;
        _backofficeApiUrl = backofficeApiUrl;
        _listingsApiUrl = listingsApiUrl;
        _marketplacesApiUrl = marketplacesApiUrl;
    }

    public async Task StartAsync()
    {
        // Locate the Backoffice.Web wwwroot output directory
        var wasmRoot = FindWasmRoot();
        Console.WriteLine($"✅ [WasmStaticFileHost] Located wwwroot: {wasmRoot}");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // Random port

        // Open CORS for cross-origin API calls from WASM
        builder.Services.AddCors(opts =>
        {
            opts.AddDefaultPolicy(policy => policy
                .SetIsOriginAllowed(_ => true)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        });

        _app = builder.Build();

        _app.UseCors();

        // Add comprehensive request/response logging middleware BEFORE any other middleware
        _app.Use(async (context, next) =>
        {
            Console.WriteLine($"[WasmStaticFileHost] → Request: {context.Request.Method} {context.Request.Path}{context.Request.QueryString}");
            Console.WriteLine($"[WasmStaticFileHost]   Host: {context.Request.Host}");

            await next();

            Console.WriteLine($"[WasmStaticFileHost] ← Response: {context.Response.StatusCode} for {context.Request.Method} {context.Request.Path}");

            if (context.Response.StatusCode == 404)
            {
                Console.WriteLine($"❌ [WasmStaticFileHost] 404 NOT FOUND: {context.Request.Method} {context.Request.Path}");
            }
        });

        // CRITICAL: Serve static WASM files FIRST (before any route handlers)
        // This ensures _framework/*.dll, *.wasm, blazor.webassembly.js are served correctly
        _app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(wasmRoot),
            ServeUnknownFileTypes = true, // Required for .wasm, .dll, .dat files
            OnPrepareResponse = ctx =>
            {
                // Log static file responses for debugging
                Console.WriteLine($"[WasmStaticFileHost] Serving static file: {ctx.File.Name} ({ctx.File.Length} bytes)");
            }
        });

        // Intercept appsettings.json requests to inject test API URLs
        // This must come AFTER UseStaticFiles so it overrides the source appsettings.json
        _app.MapGet("/appsettings.json", () =>
        {
            var config = new
            {
                ApiClients = new
                {
                    BackofficeIdentityApiUrl = _identityApiUrl,
                    BackofficeApiUrl = _backofficeApiUrl,
                    ListingsApiUrl = _listingsApiUrl,
                    MarketplacesApiUrl = _marketplacesApiUrl
                }
            };

            Console.WriteLine("✅ [WasmStaticFileHost] Intercepted appsettings.json request");
            Console.WriteLine($"   BackofficeIdentityApiUrl: {_identityApiUrl}");
            Console.WriteLine($"   BackofficeApiUrl: {_backofficeApiUrl}");
            Console.WriteLine($"   ListingsApiUrl: {_listingsApiUrl}");
            Console.WriteLine($"   MarketplacesApiUrl: {_marketplacesApiUrl}");

            return Results.Json(config);
        });

        // SPA fallback — serve index.html for all non-file routes (Blazor client-side routing)
        // This must be last to catch client-side routes like /login, /dashboard
        _app.MapFallbackToFile("index.html", new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(wasmRoot)
        });

        await _app.StartAsync();

        var addresses = _app.Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();

        BaseUrl = BackofficeIdentityApiKestrelFactory.NormalizeAddress(
            addresses?.Addresses.FirstOrDefault() ?? string.Empty);

        Console.WriteLine($"✅ [WasmStaticFileHost] Started on: {BaseUrl}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_app != null) await _app.DisposeAsync();
    }

    /// <summary>
    /// Locates the Backoffice.Web wwwroot directory containing compiled WASM files.
    /// Walks up from the test output directory to find the bin build output.
    /// IMPORTANT: Must use bin/{Configuration}/net10.0/wwwroot (not src/*/wwwroot) to get compiled _framework files.
    /// Supports both Release (CI) and Debug (local development) configurations.
    /// </summary>
    private static string FindWasmRoot()
    {
        // Strategy: Start from test output directory and look for sibling Backoffice.Web publish output
        var current = AppContext.BaseDirectory;

        // Prefer Release in CI, but support local Debug too.
        // Check Release first so CI uses the correct build artifacts.
        var configurations = new[] { "Release", "Debug" };

        // Walk up directory tree to find repo root (has src/ directory)
        while (current != null)
        {
            var srcDir = Path.Combine(current, "src");
            if (Directory.Exists(srcDir))
            {
                // Try each configuration in priority order
                foreach (var config in configurations)
                {
                    // PRIORITY 1: Check publish output directory (has index.html + _framework)
                    var publishWwwroot = Path.Combine(current, "src", "Backoffice", "Backoffice.Web", "bin", config, "net10.0", "publish", "wwwroot");
                    if (Directory.Exists(publishWwwroot) && Directory.Exists(Path.Combine(publishWwwroot, "_framework")) && File.Exists(Path.Combine(publishWwwroot, "index.html")))
                    {
                        Console.WriteLine($"✅ [FindWasmRoot] Found publish wwwroot ({config}): {publishWwwroot}");
                        return publishWwwroot;
                    }

                    // PRIORITY 2: Check bin output directory (has _framework but may be missing index.html)
                    var binWwwroot = Path.Combine(current, "src", "Backoffice", "Backoffice.Web", "bin", config, "net10.0", "wwwroot");
                    if (Directory.Exists(binWwwroot) && Directory.Exists(Path.Combine(binWwwroot, "_framework")) && File.Exists(Path.Combine(binWwwroot, "index.html")))
                    {
                        Console.WriteLine($"✅ [FindWasmRoot] Found compiled wwwroot ({config}): {binWwwroot}");
                        return binWwwroot;
                    }
                }

                // PRIORITY 3: Check source directory as fallback (only if it has _framework compiled into it)
                var srcWwwroot = Path.Combine(current, "src", "Backoffice", "Backoffice.Web", "wwwroot");
                if (Directory.Exists(srcWwwroot))
                {
                    var hasFramework = Directory.Exists(Path.Combine(srcWwwroot, "_framework"));
                    var hasIndexHtml = File.Exists(Path.Combine(srcWwwroot, "index.html"));
                    Console.WriteLine($"⚠️  [FindWasmRoot] Found source wwwroot (has _framework: {hasFramework}, has index.html: {hasIndexHtml}): {srcWwwroot}");
                    if (hasFramework && hasIndexHtml)
                        return srcWwwroot;
                }

                // None of the locations have all required files
                throw new InvalidOperationException(
                    $"Could not locate Backoffice.Web/wwwroot directory with compiled _framework files AND index.html. " +
                    $"Tried configurations: {string.Join(", ", configurations)}. " +
                    $"Ensure the Backoffice.Web project is published before running E2E tests (run: dotnet publish src/Backoffice/Backoffice.Web).");
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not locate repository root (expected directory with src/ subdirectory). " +
            $"Started from: {AppContext.BaseDirectory}");
    }
}


/// <summary>
/// Lightweight stub Listings API host for E2E tests.
/// Serves mock listing data that Backoffice.Web (Blazor WASM) fetches via browser HTTP calls.
/// Supports seeding listings per scenario and clearing between scenarios.
/// Endpoints:
///   GET /api/listings/all?page=&amp;pageSize=&amp;status= — paginated listing list
///   GET /api/listings/{id} — single listing detail
/// </summary>
internal sealed class StubListingsApiHost : IAsyncDisposable
{
    private WebApplication? _app;
    private readonly List<StubListing> _listings = new();

    public string BaseUrl { get; private set; } = string.Empty;

    public sealed record StubListing(
        Guid ListingId,
        string Sku,
        string ChannelCode,
        string ProductName,
        string? Content,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ActivatedAt,
        DateTimeOffset? EndedAt,
        string? EndCause,
        string? PauseReason);

    public void SeedListing(Guid id, string sku, string channel, string productName, string status,
        string? content = null, DateTimeOffset? createdAt = null, DateTimeOffset? activatedAt = null)
    {
        _listings.Add(new StubListing(
            id, sku, channel, productName, content, status,
            createdAt ?? DateTimeOffset.UtcNow.AddDays(-1),
            activatedAt, null, null, null));
    }

    public void Clear() => _listings.Clear();

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddCors(opts =>
        {
            opts.AddDefaultPolicy(policy => policy
                .SetIsOriginAllowed(_ => true)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        });

        _app = builder.Build();
        _app.UseCors();

        // GET /api/listings/all — paginated list (mirrors Listings.Api endpoint)
        _app.MapGet("/api/listings/all", (int? page, int? pageSize, string? status) =>
        {
            var p = page ?? 1;
            var ps = Math.Clamp(pageSize ?? 25, 1, 100);

            var filtered = string.IsNullOrEmpty(status)
                ? _listings.ToList()
                : _listings.Where(l => l.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();

            var items = filtered
                .OrderByDescending(l => l.CreatedAt)
                .Skip((p - 1) * ps)
                .Take(ps)
                .ToList();

            return Results.Json(new
            {
                items,
                totalCount = filtered.Count,
                page = p,
                pageSize = ps
            });
        });

        // GET /api/listings/{id} — single listing detail
        _app.MapGet("/api/listings/{id:guid}", (Guid id) =>
        {
            var listing = _listings.FirstOrDefault(l => l.ListingId == id);
            return listing is not null
                ? Results.Json(listing)
                : Results.NotFound();
        });

        await _app.StartAsync();

        var addresses = _app.Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();

        BaseUrl = BackofficeIdentityApiKestrelFactory.NormalizeAddress(
            addresses?.Addresses.FirstOrDefault() ?? string.Empty);

        Console.WriteLine($"✅ [StubListingsApiHost] Started on: {BaseUrl}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_app != null) await _app.DisposeAsync();
    }
}


/// <summary>
/// Lightweight stub Marketplaces API host for E2E tests.
/// Serves mock marketplace and category mapping data that Backoffice.Web (Blazor WASM)
/// fetches via browser HTTP calls to the MarketplacesApi named HttpClient.
/// Supports seeding data per scenario and clearing between scenarios.
/// Endpoints:
///   GET /api/marketplaces — all marketplaces
///   GET /api/category-mappings?channelCode= — category mappings (optional channel filter)
/// </summary>
internal sealed class StubMarketplacesApiHost : IAsyncDisposable
{
    private WebApplication? _app;
    private readonly List<StubMarketplace> _marketplaces = new();
    private readonly List<StubCategoryMapping> _mappings = new();

    public string BaseUrl { get; private set; } = string.Empty;

    public sealed record StubMarketplace(
        string Id,
        string DisplayName,
        bool IsActive,
        bool IsOwnWebsite,
        string? ApiCredentialVaultPath,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public sealed record StubCategoryMapping(
        string Id,
        string ChannelCode,
        string InternalCategory,
        string MarketplaceCategoryId,
        string? MarketplaceCategoryPath,
        DateTimeOffset LastVerifiedAt);

    public void SeedMarketplace(string id, string displayName, bool isActive,
        DateTimeOffset? createdAt = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow.AddDays(-30);
        _marketplaces.Add(new StubMarketplace(
            id, displayName, isActive, false, $"marketplace/{id.ToLowerInvariant().Replace('_', '-')}",
            now, now));
    }

    public void SeedCategoryMapping(string channelCode, string internalCategory,
        string marketplaceCategoryId, string? marketplaceCategoryPath = null)
    {
        _mappings.Add(new StubCategoryMapping(
            $"{channelCode}:{internalCategory}",
            channelCode,
            internalCategory,
            marketplaceCategoryId,
            marketplaceCategoryPath,
            DateTimeOffset.UtcNow.AddDays(-7)));
    }

    public void Clear()
    {
        _marketplaces.Clear();
        _mappings.Clear();
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddCors(opts =>
        {
            opts.AddDefaultPolicy(policy => policy
                .SetIsOriginAllowed(_ => true)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        });

        _app = builder.Build();
        _app.UseCors();

        // GET /api/marketplaces — all marketplaces (mirrors Marketplaces.Api endpoint)
        _app.MapGet("/api/marketplaces", () =>
        {
            return Results.Json(_marketplaces.OrderBy(m => m.Id).ToList());
        });

        // GET /api/category-mappings — category mappings with optional channel filter
        _app.MapGet("/api/category-mappings", (string? channelCode) =>
        {
            var filtered = string.IsNullOrEmpty(channelCode)
                ? _mappings.ToList()
                : _mappings.Where(m => m.ChannelCode.Equals(channelCode, StringComparison.OrdinalIgnoreCase)).ToList();

            return Results.Json(filtered.OrderBy(m => m.Id).ToList());
        });

        await _app.StartAsync();

        var addresses = _app.Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();

        BaseUrl = BackofficeIdentityApiKestrelFactory.NormalizeAddress(
            addresses?.Addresses.FirstOrDefault() ?? string.Empty);

        Console.WriteLine($"✅ [StubMarketplacesApiHost] Started on: {BaseUrl}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_app != null) await _app.DisposeAsync();
    }
}


/// <summary>
/// Service collection extensions for test setup (stub client registration, CORS).
/// </summary>
internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// Removes the existing client registration and replaces it with a stub implementation.
    /// </summary>
    public static void RemoveAndReplaceClient<TClient>(
        this IServiceCollection services,
        TClient implementation)
        where TClient : class
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TClient));
        if (descriptor != null) services.Remove(descriptor);
        services.AddSingleton<TClient>(implementation);
    }

}
