using Alba;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;

namespace Orders.Api.IntegrationTests;

/// <summary>
/// Test fixture providing PostgreSQL via TestContainers and Alba host for integration tests.
/// </summary>
public class TestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var connectionString = _postgres.GetConnectionString();

        // Set environment variable before host creation so Program.cs picks it up
        Environment.SetEnvironmentVariable("ConnectionStrings__marten", connectionString);

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Disable external Wolverine transports for testing
                services.DisableAllExternalWolverineTransports();
            });
        });
    }

    public async Task DisposeAsync()
    {
        // Clean up environment variable
        Environment.SetEnvironmentVariable("ConnectionStrings__marten", null);
        
        if (Host != null)
        {
            try
            {
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
}
