using Alba;
using Correspondence;
using CritterSupply.TestUtilities;
using JasperFx.CommandLine;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Tracking;

namespace Correspondence.Api.IntegrationTests;

/// <summary>
/// Test fixture for Correspondence BC integration tests using Alba + TestContainers.
/// Provides isolated PostgreSQL container for each test run.
/// </summary>
public sealed class TestFixture : IAsyncLifetime
{
    // PostgreSQL container (isolated per test run)
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("correspondence_test_db")
        .WithName($"correspondence-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private string? _connectionString;

    public IAlbaHost Host { get; private set; } = null!;

    /// <summary>
    /// Initialize containers and Alba host before tests run.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _connectionString = _postgres.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        // Configure Alba host with test container
        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Override Marten with test database
                services.ConfigureMarten(opts =>
                {
                    opts.Connection(_connectionString);
                });

                // Disable external Wolverine transports for single-BC testing
                services.DisableAllExternalWolverineTransports();

                // Register test authentication for all schemes and roles used by Correspondence.Api
                // Schemes: Backoffice (backoffice JWT)
                // Policies: CustomerService, OperationsManager
                services.AddTestAuthentication(
                    roles: ["CustomerService", "OperationsManager", "SystemAdmin"],
                    schemes: ["Backoffice"]);
            });
        });

        Host.AddDefaultAuthHeader();
    }

    /// <summary>
    /// Cleanup resources after tests complete.
    /// </summary>
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
                // Swallow ObjectDisposedException during cleanup
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
                e is ObjectDisposedException or OperationCanceledException))
            {
                // Swallow disposal exceptions during cleanup
            }
        }

        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Get a lightweight Marten document session for test data seeding.
    /// </summary>
    public IDocumentSession GetDocumentSession()
    {
        var store = Host.Services.GetRequiredService<IDocumentStore>();
        return store.LightweightSession();
    }

    /// <summary>
    /// Get the Marten document store.
    /// </summary>
    public IDocumentStore GetDocumentStore()
    {
        return Host.Services.GetRequiredService<IDocumentStore>();
    }

    /// <summary>
    /// Clean all documents and events from the test database.
    /// Call this at the beginning of each test for isolation.
    /// </summary>
    public async Task CleanAllDataAsync()
    {
        var store = GetDocumentStore();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();
        await store.Advanced.Clean.DeleteAllEventDataAsync();
    }

    /// <summary>
    /// Execute a message through Wolverine and wait for all cascading effects.
    /// Returns tracked session with all sent/received messages.
    /// </summary>
    public async Task<ITrackedSession> ExecuteAndWaitAsync<T>(T message, int timeoutSeconds = 15)
        where T : class
    {
        return await Host.TrackActivity(TimeSpan.FromSeconds(timeoutSeconds))
            .DoNotAssertOnExceptionsDetected()
            .AlsoTrack(Host)
            .ExecuteAndWaitAsync(ctx => ctx.InvokeAsync(message));
    }
}
