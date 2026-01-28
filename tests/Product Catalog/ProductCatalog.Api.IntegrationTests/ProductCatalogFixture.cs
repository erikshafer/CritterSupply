using Alba;
using JasperFx.CommandLine;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using ProductCatalog.Products;
using Testcontainers.PostgreSql;
using Wolverine;

namespace ProductCatalog.IntegrationTests;

/// <summary>
/// Alba fixture for Product Catalog integration tests.
/// Uses TestContainers for isolated Postgres instance.
/// Collection fixture pattern ensures sequential test execution and proper resource sharing.
/// </summary>
public sealed class ProductCatalogFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
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

        // Necessary for WebApplicationFactory usage with Alba for integration testing
        JasperFxEnvironment.AutoStartHost = true;

        // Build Alba host with test database
        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Configure Marten with the test container connection string directly
                services.ConfigureMarten(opts =>
                {
                    opts.Connection(_connectionString);
                });

                // Disable external transports for tests
                services.DisableAllExternalWolverineTransports();
            });
        });

        // Seed test data
        using var scope = Host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
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
