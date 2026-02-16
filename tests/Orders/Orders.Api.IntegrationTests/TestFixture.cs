using JasperFx.CommandLine;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Tracking;

namespace Orders.Api.IntegrationTests;

/// <summary>
/// Test fixture providing PostgreSQL via TestContainers and Alba host for integration tests.
/// Uses collection fixture pattern to ensure sequential test execution and proper resource sharing.
/// </summary>
public class TestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("orders_test_db")
        .WithName($"orders-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private string? _connectionString;

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _connectionString = _postgres.GetConnectionString();

        // Necessary for WebApplicationFactory usage with Alba for integration testing, as
        // well as when using JasperFx command line processing. Introducing this without such
        // does not (seem) to have any negative or unintended side effects.
        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Configure Marten with the test container connection string directly
                // This avoids environment variable race conditions in parallel test runs
                services.ConfigureMarten(opts =>
                {
                    opts.Connection(_connectionString);
                });

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
    /// Caller is responsible for disposing the session.
    /// </summary>
    public IDocumentSession GetDocumentSession()
    {
        return Host.Services.GetRequiredService<IDocumentStore>().LightweightSession();
    }

    /// <summary>
    /// Gets the document store for advanced operations like cleaning data.
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
    /// Executes a message through Wolverine and waits for all cascading messages to complete.
    /// This ensures all side effects are persisted before assertions.
    /// Messages with no routes (like integration messages to other contexts) are allowed.
    /// </summary>
    public async Task<ITrackedSession> ExecuteAndWaitAsync<T>(T message, int timeoutSeconds = 15)
        where T : class
    {
        return await Host.TrackActivity(TimeSpan.FromSeconds(timeoutSeconds))
            .DoNotAssertOnExceptionsDetected()
            .AlsoTrack(Host)
            .ExecuteAndWaitAsync((Func<IMessageContext, Task>)(async ctx =>
            {
                await ctx.InvokeAsync(message);
            }));
    }

    /// <summary>
    /// This method allows us to make HTTP calls into our system in memory with Alba while
    /// leveraging Wolverine's test support for message tracking to both record outgoing messages.
    /// This ensures that any cascaded work spawned by the initial command is completed before
    /// passing control back to the calling test.
    /// </summary>
    protected async Task<(ITrackedSession, IScenarioResult)> TrackedHttpCall(Action<Scenario> configuration)
    {
        IScenarioResult result = null!;

        var tracked = await Host.ExecuteAndWaitAsync(async () =>
        {
            result = await Host.Scenario(configuration);
        });

        return (tracked, result);
    }

    /// <summary>
    /// Helper method to create a Shopping BC CheckoutCompleted integration message.
    /// This is the ONLY way to start an Order saga in tests.
    /// Maps from Orders domain types to Shopping integration contract types.
    /// </summary>
    public static Messages.Contracts.Shopping.CheckoutCompleted CreateCheckoutCompletedMessage(
        Guid orderId,
        Guid checkoutId,
        Guid? customerId,
        IReadOnlyList<Orders.Placement.CheckoutLineItem> lineItems,
        Orders.Placement.ShippingAddress shippingAddress,
        string shippingMethod,
        decimal shippingCost,
        string paymentMethodToken,
        DateTimeOffset completedAt)
    {
        // Map Orders domain types to Shopping integration contract types
        var items = lineItems.Select(item =>
            new Messages.Contracts.Shopping.CheckoutLineItem(item.Sku, item.Quantity, item.PriceAtPurchase))
            .ToList();

        var address = new Messages.Contracts.CustomerIdentity.AddressSnapshot(
            shippingAddress.Street,
            shippingAddress.Street2,
            shippingAddress.City,
            shippingAddress.State,
            shippingAddress.PostalCode,
            shippingAddress.Country);

        return new Messages.Contracts.Shopping.CheckoutCompleted(
            orderId,
            checkoutId,
            customerId,
            items,
            address,
            shippingMethod,
            shippingCost,
            paymentMethodToken,
            completedAt);
    }

    /// <summary>
    /// Simplified helper to create a CheckoutCompleted message with test defaults.
    /// Use this for tests that just need to create an order without caring about specific details.
    /// </summary>
    public static Messages.Contracts.Shopping.CheckoutCompleted CreateCheckoutCompletedMessage(
        Guid? customerId = null)
    {
        var orderId = Guid.CreateVersion7();
        var checkoutId = Guid.CreateVersion7();
        var actualCustomerId = customerId ?? Guid.CreateVersion7();

        var lineItems = new List<Orders.Placement.CheckoutLineItem>
        {
            new("SKU-001", 2, 19.99m),
            new("SKU-002", 1, 29.99m)
        };

        var items = lineItems.Select(item =>
            new Messages.Contracts.Shopping.CheckoutLineItem(item.Sku, item.Quantity, item.PriceAtPurchase))
            .ToList();

        var address = new Messages.Contracts.CustomerIdentity.AddressSnapshot(
            "123 Main St",
            null,
            "Springfield",
            "IL",
            "62701",
            "USA");

        return new Messages.Contracts.Shopping.CheckoutCompleted(
            orderId,
            checkoutId,
            actualCustomerId,
            items,
            address,
            "Standard",
            5.99m,
            "tok_visa_4242",
            DateTimeOffset.UtcNow);
    }
}
