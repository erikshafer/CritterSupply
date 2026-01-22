using Alba;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using ProductCatalog.Products;
using Testcontainers.PostgreSql;
using Wolverine;

namespace ProductCatalog.IntegrationTests;

/// <summary>
/// Alba fixture for Product Catalog integration tests.
/// Uses TestContainers for isolated Postgres instance.
/// </summary>
public sealed class ProductCatalogFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("product_catalog_test")
        .WithName($"productcatalog-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private string? _connectionString;

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _connectionString = _postgres.GetConnectionString();

        // Build Alba host with test database
        Host = await AlbaHost.For<Program>(builder =>
        {
            // Override connection string via environment variable
            Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _connectionString);

            builder.ConfigureServices(services =>
            {
                // Disable external transports for tests
                services.DisableAllExternalWolverineTransports();
            });
        });

        // Ensure schema is created and seed test data
        using var scope = Host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await SeedData.SeedProductsAsync(store);
    }

    public async Task DisposeAsync()
    {
        if (Host is not null)
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
}
