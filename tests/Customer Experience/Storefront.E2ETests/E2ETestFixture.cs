using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Marten;
using Storefront.Clients;
using Storefront.E2ETests.Stubs;
using System.Net.Http.Json;
using Testcontainers.PostgreSql;
using Wolverine;

namespace Storefront.E2ETests;

/// <summary>
/// E2E test fixture that starts real Kestrel servers for both Storefront.Web and Storefront.Api,
/// backed by a TestContainers PostgreSQL instance for Marten.
///
/// Architecture:
///   Playwright Browser
///         │
///         ▼
///   Storefront.Web (real Kestrel, random port)
///         │ (HTTP)
///         ▼
///   Storefront.Api (real Kestrel, random port, TestContainers Postgres)
///         ├── IShoppingClient         → StubShoppingClient
///         ├── IOrdersClient           → StubOrdersClient
///         ├── ICatalogClient          → StubCatalogClient
///         └── ICustomerIdentityClient → StubCustomerIdentityClient
///
/// Key constraint: Both services use REAL Kestrel (not TestServer).
/// TestServer does not bind to a TCP port — Playwright's browser cannot connect to it,
/// and SignalR's WebSocket upgrade requires a real HTTP server.
/// </summary>
public sealed class E2ETestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("storefront_e2e_test_db")
        .WithName($"storefront-e2e-postgres-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private StorefrontApiKestrelFactory? _apiFactory;
    private StorefrontWebKestrelFactory? _webFactory;

    /// <summary>Stub clients to configure downstream BC behavior per scenario.</summary>
    public StubShoppingClient StubShoppingClient { get; } = new();
    public StubCatalogClient StubCatalogClient { get; } = new();
    public StubOrdersClient StubOrdersClient { get; } = new();
    public StubCustomerIdentityClient StubCustomerIdentityClient { get; } = new();

    /// <summary>Base URL of Storefront.Web (the Blazor Server app — what Playwright navigates to).</summary>
    public string StorefrontWebBaseUrl { get; private set; } = string.Empty;

    /// <summary>Base URL of Storefront.Api (the BFF — used for direct API setup calls in test hooks).</summary>
    public string StorefrontApiBaseUrl { get; private set; } = string.Empty;

    /// <summary>Direct access to Storefront.Api host for Wolverine message injection (Phase 2 SignalR tests).</summary>
    public IHost StorefrontApiHost { get; private set; } = null!;

    /// <summary>
    /// Cart ID seeded for the standard E2E checkout scenario.
    /// Expose so step definitions can inject it into the browser's localStorage after login.
    /// </summary>
    public Guid? SeededCartId { get; private set; }

    public async Task InitializeAsync()
    {
        // Step 1: Start TestContainers PostgreSQL
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        // Step 2: Start Storefront.Api with stub clients + test database
        _apiFactory = new StorefrontApiKestrelFactory(
            connectionString,
            StubShoppingClient,
            StubCatalogClient,
            StubOrdersClient,
            StubCustomerIdentityClient);

        // UseKestrel + CreateDefaultClient — starts the real Kestrel server and captures the bound port
        _apiFactory.StartKestrel();
        StorefrontApiBaseUrl = _apiFactory.ServerAddress;
        StorefrontApiHost = _apiFactory.Services.GetRequiredService<IHost>();

        // Step 3: Start Storefront.Web pointing at the test Storefront.Api
        _webFactory = new StorefrontWebKestrelFactory(StorefrontApiBaseUrl);
        _webFactory.StartKestrel();
        StorefrontWebBaseUrl = _webFactory.ServerAddress;
    }

    public async Task DisposeAsync()
    {
        if (_webFactory != null) await _webFactory.DisposeAsync();
        if (_apiFactory != null) await _apiFactory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Cleans all Marten document/event data from the test database.
    /// Call this in DataHooks.AfterScenario for complete test isolation.
    /// </summary>
    public async Task CleanDatabaseAsync()
    {
        var store = _apiFactory?.Services.GetRequiredService<IDocumentStore>();
        if (store != null)
        {
            await store.Advanced.Clean.DeleteAllDocumentsAsync();
            await store.Advanced.Clean.DeleteAllEventDataAsync();
        }
    }

    /// <summary>
    /// Resets all stub clients. Call this in DataHooks.BeforeScenario.
    /// </summary>
    public void ClearAllStubs()
    {
        StubShoppingClient.Clear();
        StubCatalogClient.Clear();
        StubOrdersClient.Clear();
        StubCustomerIdentityClient.Clear();
        SeededCartId = null;
    }

    /// <summary>
    /// Seeds the standard E2E test scenario: Alice's cart with 2 products and 2 saved addresses.
    /// After seeding, stubs return data consistent with WellKnownTestData constants.
    /// </summary>
    public async Task SeedStandardCheckoutScenarioAsync()
    {
        // Seed product catalog data
        StubCatalogClient.AddProduct(new Storefront.Clients.ProductDto(
            WellKnownTestData.Products.CeramicDogBowlSku,
            WellKnownTestData.Products.CeramicDogBowlName,
            "High-quality ceramic dog bowl",
            "Dogs",
            WellKnownTestData.Products.CeramicDogBowlPrice,
            "Active",
            [new Storefront.Clients.ProductImageDto("https://example.com/dog-bowl.jpg", "Ceramic Dog Bowl", 1)]));

        StubCatalogClient.AddProduct(new Storefront.Clients.ProductDto(
            WellKnownTestData.Products.InteractiveCatLaserSku,
            WellKnownTestData.Products.InteractiveCatLaserName,
            "Fun interactive laser toy for cats",
            "Cats",
            WellKnownTestData.Products.InteractiveCatLaserPrice,
            "Active",
            [new Storefront.Clients.ProductImageDto("https://example.com/cat-laser.jpg", "Interactive Cat Laser", 1)]));

        // Seed Alice's cart via Shopping stub
        var cartId = await StubShoppingClient.InitializeCartAsync(WellKnownTestData.Customers.Alice);
        await StubShoppingClient.AddItemAsync(
            cartId,
            WellKnownTestData.Products.CeramicDogBowlSku,
            quantity: 2);
        await StubShoppingClient.AddItemAsync(
            cartId,
            WellKnownTestData.Products.InteractiveCatLaserSku,
            quantity: 1);

        // Coordinate stubs: register a deterministic checkoutId so that when the browser
        // POSTs /carts/{cartId}/checkout, InitiateCheckoutAsync returns a known ID,
        // and GetCheckoutAsync will find the pre-seeded checkout in StubOrdersClient.
        StubShoppingClient.SetCheckoutId(cartId, WellKnownTestData.Checkouts.AliceCheckoutId);
        StubOrdersClient.AddCheckout(
            WellKnownTestData.Checkouts.AliceCheckoutId,
            WellKnownTestData.Customers.Alice,
            new Storefront.Clients.CheckoutItemDto(WellKnownTestData.Products.CeramicDogBowlSku, 2, WellKnownTestData.Products.CeramicDogBowlPrice),
            new Storefront.Clients.CheckoutItemDto(WellKnownTestData.Products.InteractiveCatLaserSku, 1, WellKnownTestData.Products.InteractiveCatLaserPrice));

        // Expose cartId so step definitions can inject it into browser localStorage after login
        SeededCartId = cartId;

        // Seed Alice's saved addresses via Customer Identity stub
        StubCustomerIdentityClient.AddAddress(new Storefront.Clients.CustomerAddressDto(
            WellKnownTestData.Addresses.AliceHome,
            WellKnownTestData.Customers.Alice,
            WellKnownTestData.Addresses.AliceHomeNickname,
            WellKnownTestData.Addresses.AliceHomeAddressLine1,
            AddressLine2: null,
            WellKnownTestData.Addresses.AliceHomeCity,
            WellKnownTestData.Addresses.AliceHomeState,
            WellKnownTestData.Addresses.AliceHomePostalCode,
            WellKnownTestData.Addresses.AliceHomeCountry,
            AddressType: "Shipping",
            IsDefault: true,
            DisplayLine: WellKnownTestData.Addresses.AliceHomeDisplayLine));

        StubCustomerIdentityClient.AddAddress(new Storefront.Clients.CustomerAddressDto(
            WellKnownTestData.Addresses.AliceWork,
            WellKnownTestData.Customers.Alice,
            WellKnownTestData.Addresses.AliceWorkNickname,
            WellKnownTestData.Addresses.AliceWorkAddressLine1,
            AddressLine2: null,
            WellKnownTestData.Addresses.AliceWorkCity,
            WellKnownTestData.Addresses.AliceWorkState,
            WellKnownTestData.Addresses.AliceWorkPostalCode,
            WellKnownTestData.Addresses.AliceWorkCountry,
            AddressType: "Shipping",
            IsDefault: false,
            DisplayLine: WellKnownTestData.Addresses.AliceWorkDisplayLine));

        // Seed a pre-placed order for SignalR E2E scenarios.
        // SignalR tests navigate directly to /order-confirmation/{AliceOrderId} rather than
        // driving the full browser checkout UI — following the principle:
        //   "The browser only touches what the test is testing.
        //    Everything else is done via API or stub."
        // The full browser checkout flow is already covered by the happy-path scenario (Scenario 1).
        var orderTotal = (WellKnownTestData.Products.CeramicDogBowlPrice * 2)
                       + WellKnownTestData.Products.InteractiveCatLaserPrice
                       + WellKnownTestData.Shipping.StandardCost;

        StubOrdersClient.AddOrder(new Storefront.Clients.OrderDto(
            WellKnownTestData.Orders.AliceOrderId,
            WellKnownTestData.Customers.Alice,
            "Placed",
            DateTimeOffset.UtcNow,
            orderTotal));
    }
}


/// <summary>
/// WebApplicationFactory for Storefront.Api that binds to a real Kestrel TCP port.
/// This is required because Playwright's browser must connect over real HTTP,
/// and SignalR's WebSocket upgrade requires a real server (not TestServer).
/// Uses the built-in WebApplicationFactory.UseKestrel() support (ASP.NET Core 10+).
/// </summary>
internal sealed class StorefrontApiKestrelFactory(
    string connectionString,
    StubShoppingClient stubShoppingClient,
    StubCatalogClient stubCatalogClient,
    StubOrdersClient stubOrdersClient,
    StubCustomerIdentityClient stubCustomerIdentityClient)
    : WebApplicationFactory<Storefront.Api.StorefrontHub>
{
    public string ServerAddress { get; private set; } = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Override Marten connection to use TestContainers Postgres
            services.ConfigureMarten(opts => opts.Connection(connectionString));

            // Replace real HTTP clients with stubs (same pattern as Storefront.Api.IntegrationTests)
            services.RemoveAndReplaceClient<IShoppingClient>(stubShoppingClient);
            services.RemoveAndReplaceClient<ICatalogClient>(stubCatalogClient);
            services.RemoveAndReplaceClient<IOrdersClient>(stubOrdersClient);
            services.RemoveAndReplaceClient<ICustomerIdentityClient>(stubCustomerIdentityClient);

            // Disable external Wolverine transports (RabbitMQ) — not needed for E2E
            services.DisableAllExternalWolverineTransports();
        });
    }

    internal void StartKestrel()
    {
        // UseKestrel must be called BEFORE the server is initialized (before CreateDefaultClient).
        // Port=0 lets the OS pick a random available port — avoids conflicts in parallel test runs.
        UseKestrel(0);
        CreateDefaultClient(); // triggers Kestrel server startup

        // Read the actual bound address from the server's feature collection after startup.
        var serverAddresses = Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();

        var rawAddress = serverAddresses?.Addresses.FirstOrDefault() ?? string.Empty;

        // On Linux, Kestrel with port=0 may bind to a wildcard address such as
        // http://[::]:PORT (IPv6 any) or http://0.0.0.0:PORT (IPv4 any).
        // Wildcard addresses are not routable from within the browser — the browser
        // needs a concrete host (localhost / 127.0.0.1) to open a connection.
        // This is specifically required for the SignalR WebSocket upgrade from the
        // Playwright browser to the Storefront.Api hub.
        ServerAddress = rawAddress
            .Replace("//[::]:","//localhost:")
            .Replace("//0.0.0.0:","//localhost:");
    }
}

/// <summary>
/// WebApplicationFactory for Storefront.Web that binds to a real Kestrel TCP port
/// and is configured to call the test Storefront.Api instance.
/// Uses the built-in WebApplicationFactory.UseKestrel() support (ASP.NET Core 10+).
/// </summary>
internal sealed class StorefrontWebKestrelFactory(string storefrontApiBaseUrl)
    : WebApplicationFactory<Storefront.Web.StorefrontWebMarker>
{
    public string ServerAddress { get; private set; } = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Inject the test Storefront.Api URL as configuration so both the HttpClient factory
        // and OrderConfirmation.razor's SignalR JS call use the same test server address.
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiClients:StorefrontApiUrl"] = storefrontApiBaseUrl
            });
        });

        builder.ConfigureServices(services =>
        {
            // Override the StorefrontApi HttpClient base address to point at our test API server.
            // All Checkout.razor calls now flow through StorefrontApi (not OrdersClient directly).
            services.ConfigureHttpClientBaseAddress("StorefrontApi", storefrontApiBaseUrl);

            // Stub CustomerIdentityApi so Storefront.Web's /api/auth/login endpoint does not
            // call out to the real Customer Identity service (which is not running in E2E tests).
            // Always returns Alice's login data — intentional for single-user Cycle 20 scenarios.
            // TODO: Add credential-checking logic here if multi-user E2E scenarios are added.
            services.Configure<Microsoft.Extensions.Http.HttpClientFactoryOptions>("CustomerIdentityApi", opts =>
            {
                opts.HttpMessageHandlerBuilderActions.Add(b =>
                    b.PrimaryHandler = new StubCustomerIdentityApiHandler());
            });
        });
    }

    internal void StartKestrel()
    {
        // UseKestrel must be called BEFORE the server is initialized (before CreateDefaultClient).
        UseKestrel(0);
        CreateDefaultClient();

        var serverAddresses = Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();

        // Normalize wildcard addresses to localhost (same reason as StorefrontApiKestrelFactory).
        var rawAddress = serverAddresses?.Addresses.FirstOrDefault() ?? string.Empty;
        ServerAddress = rawAddress
            .Replace("//[::]:","//localhost:")
            .Replace("//0.0.0.0:","//localhost:");
    }
}

/// <summary>
/// Stub HttpMessageHandler for the CustomerIdentityApi named HTTP client used by Storefront.Web.
/// Storefront.Web's /api/auth/login endpoint calls CustomerIdentityApi internally.
/// This handler intercepts those calls and returns Alice's credentials without hitting a real service.
/// Always-Alice is intentional for Cycle 20's single-user test scope.
/// TODO: Add credential-checking logic here if multi-user E2E scenarios are added in a later cycle.
/// </summary>
internal sealed class StubCustomerIdentityApiHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                customerId = WellKnownTestData.Customers.Alice.ToString(),
                email = WellKnownTestData.Customers.AliceEmail,
                firstName = WellKnownTestData.Customers.AliceFirstName,
                lastName = WellKnownTestData.Customers.AliceLastName
            })
        };
        return Task.FromResult(response);
    }
}

/// <summary>
/// Extension methods for service collection manipulation in E2E test fixtures.
/// </summary>
internal static class ServiceCollectionExtensions
{
    public static void RemoveAndReplaceClient<TClient>(
        this IServiceCollection services,
        TClient implementation)
        where TClient : class
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TClient));
        if (descriptor != null) services.Remove(descriptor);
        services.AddSingleton<TClient>(implementation);
    }

    public static void ConfigureHttpClientBaseAddress(
        this IServiceCollection services,
        string clientName,
        string baseAddress)
    {
        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var baseUri))
        {
            throw new ArgumentException(
                $"E2E fixture: '{baseAddress}' is not a valid absolute URI for HttpClient '{clientName}'. " +
                $"Ensure the test server started successfully and ServerAddress was captured.",
                nameof(baseAddress));
        }

        services.Configure<Microsoft.Extensions.Http.HttpClientFactoryOptions>(clientName, options =>
        {
            options.HttpClientActions.Add(client =>
            {
                client.BaseAddress = baseUri;
            });
        });
    }
}
