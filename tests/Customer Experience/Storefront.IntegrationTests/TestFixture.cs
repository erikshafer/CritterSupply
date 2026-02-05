using Alba;
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
}
