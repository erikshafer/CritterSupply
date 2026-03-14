using Alba;
using Correspondence;
using DotNet.Testcontainers.Builders;
using JasperFx.CodeGeneration;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Wolverine;
using Wolverine.Tracking;

namespace Correspondence.Api.IntegrationTests;

/// <summary>
/// Test fixture for Correspondence BC integration tests using Alba + TestContainers.
/// Provides isolated PostgreSQL and RabbitMQ containers for each test run.
/// </summary>
public sealed class TestFixture : IAsyncLifetime
{
    // PostgreSQL container (isolated per test run)
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:18-alpine")
        .WithDatabase("correspondence_test_db")
        .WithName($"correspondence-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
        .Build();

    // RabbitMQ container (for message subscriptions)
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:4-management-alpine")
        .WithName($"correspondence-rabbitmq-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private string _connectionString = string.Empty;

    public IAlbaHost Host { get; private set; } = null!;

    /// <summary>
    /// Initialize containers and Alba host before tests run.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Start containers in parallel
        await Task.WhenAll(
            _postgres.StartAsync(),
            _rabbitMq.StartAsync()
        );

        _connectionString = _postgres.GetConnectionString();
        var rabbitMqConnectionString = _rabbitMq.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        // Configure Alba host with test containers
        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Override Marten with test database
                services.ConfigureMarten(opts =>
                {
                    opts.Connection(_connectionString);
                    opts.DatabaseSchemaName = Constants.Correspondence.ToLowerInvariant();
                    opts.AutoCreateSchemaObjects = Weasel.Core.AutoCreate.All;
                });

                // Override Wolverine RabbitMQ with test container
                services.ConfigureWolverine(opts =>
                {
                    opts.UseRabbitMq(rabbitMqConnectionString);
                    opts.Policies.AutoApplyTransactions();
                    opts.Policies.UseDurableLocalQueues();
                    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

                    // Subscribe to test queues
                    opts.ListenToRabbitQueue("correspondence-orders-events").ProcessInline();
                    opts.ListenToRabbitQueue("correspondence-fulfillment-events").ProcessInline();
                    opts.ListenToRabbitQueue("correspondence-returns-events").ProcessInline();

                    // Discover handlers
                    opts.Discovery.IncludeAssembly(typeof(Messages.Message).Assembly);
                });
            });
        });
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

        await Task.WhenAll(
            _postgres.DisposeAsync().AsTask(),
            _rabbitMq.DisposeAsync().AsTask()
        );
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
