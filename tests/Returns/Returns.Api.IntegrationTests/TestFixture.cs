using JasperFx.CommandLine;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Tracking;

namespace Returns.Api.IntegrationTests;

/// <summary>
/// Test fixture providing PostgreSQL via TestContainers and Alba host for integration tests.
/// Uses the collection fixture pattern to ensure sequential test execution and proper resource sharing.
/// </summary>
public class TestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("returns_test_db")
        .WithName($"returns-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private string? _connectionString;

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _connectionString = _postgres.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        var programType = GetProgramType("Returns.Api");

        Host = await AlbaHost.For(programType, builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.ConfigureMarten(opts =>
                {
                    opts.Connection(_connectionString);
                });

                // Disable external Wolverine transports for testing
                services.DisableAllExternalWolverineTransports();
            });
        });
    }

    private static Type GetProgramType(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);
        var programType = assembly.GetType("Program")
            ?? throw new InvalidOperationException($"Program class not found in assembly {assemblyName}");
        return programType;
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
    /// Caller is responsible for disposing of the session.
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
    /// Cleans all document and event data from the database. Use between tests that need isolation.
    /// </summary>
    public async Task CleanAllDataAsync()
    {
        var store = GetDocumentStore();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();
        await store.Advanced.Clean.DeleteAllEventDataAsync();
    }

    /// <summary>
    /// Executes a message through Wolverine and waits for all cascading messages to complete.
    /// This ensures all side effects are persisted before assertions.
    /// </summary>
    public async Task<ITrackedSession> ExecuteAndWaitAsync<T>(T message, int timeoutSeconds = 15)
        where T : class
    {
        return await Host.TrackActivity(TimeSpan.FromSeconds(timeoutSeconds))
            .DoNotAssertOnExceptionsDetected()
            .AlsoTrack(Host)
            .ExecuteAndWaitAsync(ctx =>
            {
                return ctx.InvokeAsync(message);
            });
    }
}
