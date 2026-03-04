using JasperFx.CommandLine;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Storefront.Clients;
using Storefront.IntegrationTests.Stubs;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Tracking;

namespace Storefront.IntegrationTests;

/// <summary>
/// Test fixture for BFF integration tests
/// Uses TestContainers for Postgres isolation and stub HTTP clients for downstream BCs
/// </summary>
public class TestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("storefront_test_db")
        .WithName($"storefront-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private string? _connectionString;

    public IAlbaHost Host { get; private set; } = null!;

    // Expose stub clients so tests can configure them with test data
    public StubShoppingClient StubShoppingClient { get; } = new();
    public StubCatalogClient StubCatalogClient { get; } = new();
    public StubOrdersClient StubOrdersClient { get; } = new();
    public StubCustomerIdentityClient StubCustomerIdentityClient { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _connectionString = _postgres.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Configure Marten with the test container connection string
                services.ConfigureMarten(opts =>
                {
                    opts.Connection(_connectionString);
                });

                // Remove existing scoped client registrations from Program.cs
                var shoppiingClientDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IShoppingClient));
                if (shoppiingClientDescriptor != null)
                    services.Remove(shoppiingClientDescriptor);

                var catalogClientDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICatalogClient));
                if (catalogClientDescriptor != null)
                    services.Remove(catalogClientDescriptor);

                var ordersClientDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOrdersClient));
                if (ordersClientDescriptor != null)
                    services.Remove(ordersClientDescriptor);

                var identityClientDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICustomerIdentityClient));
                if (identityClientDescriptor != null)
                    services.Remove(identityClientDescriptor);

                // Replace real HTTP clients with stubs for testing
                services.AddSingleton<IShoppingClient>(StubShoppingClient);
                services.AddSingleton<ICatalogClient>(StubCatalogClient);
                services.AddSingleton<IOrdersClient>(StubOrdersClient);
                services.AddSingleton<ICustomerIdentityClient>(StubCustomerIdentityClient);

                // Disable external Wolverine transports for testing
                services.DisableAllExternalWolverineTransports();
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (Host != null)
        {
            try
            {
                await Host.StopAsync();
                await Host.DisposeAsync();
            }
            catch (ObjectDisposedException)
            {
                // Ignore if already disposed during async shutdown
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
                e is OperationCanceledException or ObjectDisposedException))
            {
                // Ignore cancellation/disposal exceptions during shutdown
            }
        }

        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Gets a Marten document session for direct database operations.
    /// </summary>
    public IDocumentSession GetDocumentSession()
    {
        return Host.Services.GetRequiredService<IDocumentStore>().LightweightSession();
    }

    /// <summary>
    /// Gets the document store for advanced operations.
    /// </summary>
    public IDocumentStore GetDocumentStore()
    {
        return Host.Services.GetRequiredService<IDocumentStore>();
    }

    /// <summary>
    /// Cleans all document data from the database. Use between tests that need isolation.
    /// </summary>
    public async Task CleanAllDocumentsAsync()
    {
        var store = GetDocumentStore();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();
    }

    /// <summary>
    /// Executes a tracked HTTP call through Alba with Wolverine message tracking.
    /// </summary>
    public async Task<(ITrackedSession, IScenarioResult)> TrackedHttpCall(Action<Scenario> configuration)
    {
        IScenarioResult result = null!;

        var tracked = await Host.ExecuteAndWaitAsync(async () =>
        {
            result = await Host.Scenario(configuration);
        });

        return (tracked, result);
    }

    /// <summary>
    /// Clear all stub client data (for test isolation between tests)
    /// </summary>
    public void ClearAllStubs()
    {
        StubShoppingClient.Clear();
        StubCatalogClient.Clear();
        StubOrdersClient.Clear();
        StubCustomerIdentityClient.Clear();
    }

    /// <summary>
    /// Seed common test products into Catalog stub
    /// </summary>
    public void SeedCommonProducts()
    {
        StubCatalogClient.AddProduct(new Storefront.Clients.ProductDto(
            "DOG-BOWL-001",
            "Ceramic Dog Bowl (Large)",
            "High-quality ceramic dog bowl",
            "Dogs",
            19.99m,
            "Active",
            [new Storefront.Clients.ProductImageDto("https://example.com/dog-bowl.jpg", "Ceramic Dog Bowl", 1)]));

        StubCatalogClient.AddProduct(new Storefront.Clients.ProductDto(
            "CAT-TOY-001",
            "Interactive Cat Laser",
            "Fun laser toy for cats",
            "Cats",
            29.99m,
            "Active",
            [new Storefront.Clients.ProductImageDto("https://example.com/cat-laser.jpg", "Cat Laser", 1)]));

        StubCatalogClient.AddProduct(new Storefront.Clients.ProductDto(
            "DOG-FOOD-001",
            "Premium Dog Food (50lb)",
            "Nutritious dog food",
            "Dogs",
            45.00m,
            "Active",
            [new Storefront.Clients.ProductImageDto("https://example.com/dog-food.jpg", "Dog Food", 1)]));
    }

    /// <summary>
    /// Create a cart with items (helper for multi-step tests)
    /// </summary>
    public async Task<Guid> CreateCartWithItemsAsync(
        Guid customerId,
        params (string sku, int quantity, decimal unitPrice)[] items)
    {
        // Initialize cart via Shopping BC stub
        var cartId = await StubShoppingClient.InitializeCartAsync(customerId);

        // Add items to cart
        foreach (var (sku, quantity, unitPrice) in items)
        {
            await StubShoppingClient.AddItemAsync(cartId, sku, quantity, unitPrice);
        }

        return cartId;
    }
}
