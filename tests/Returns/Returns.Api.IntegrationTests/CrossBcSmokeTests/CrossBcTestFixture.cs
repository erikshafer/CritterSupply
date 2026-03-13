extern alias OrdersApi;
extern alias FulfillmentApi;

using JasperFx.CommandLine;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Wolverine;
using Wolverine.RabbitMQ;
using Wolverine.Tracking;

namespace Returns.Api.IntegrationTests.CrossBcSmokeTests;

/// <summary>
/// Multi-host test fixture for cross-BC integration testing.
/// Provides 3 Alba hosts (Returns.Api, Orders.Api, Fulfillment.Api) with shared RabbitMQ
/// and isolated PostgreSQL containers to verify end-to-end message flows.
///
/// IMPORTANT: Unlike single-BC TestFixtures that disable external transports,
/// this fixture ENABLES RabbitMQ transport for all 3 hosts to verify actual message routing.
/// </summary>
public class CrossBcTestFixture : IAsyncLifetime
{
    // Shared RabbitMQ container for all 3 bounded contexts
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithName($"crossbc-rabbitmq-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    // Isolated PostgreSQL containers (one per BC)
    private readonly PostgreSqlContainer _returnsPostgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("returns_crossbc_test")
        .WithName($"returns-postgres-crossbc-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private readonly PostgreSqlContainer _ordersPostgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("orders_crossbc_test")
        .WithName($"orders-postgres-crossbc-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private readonly PostgreSqlContainer _fulfillmentPostgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("fulfillment_crossbc_test")
        .WithName($"fulfillment-postgres-crossbc-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    public IAlbaHost ReturnsHost { get; private set; } = null!;
    public IAlbaHost OrdersHost { get; private set; } = null!;
    public IAlbaHost FulfillmentHost { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Start infrastructure containers in parallel
        await Task.WhenAll(
            _rabbitMq.StartAsync(),
            _returnsPostgres.StartAsync(),
            _ordersPostgres.StartAsync(),
            _fulfillmentPostgres.StartAsync()
        );

        var rabbitMqUri = _rabbitMq.GetConnectionString();

        // Enable JasperFx auto-start for WebApplicationFactory
        JasperFxEnvironment.AutoStartHost = true;

        // Start all 3 Alba hosts in parallel using extern aliases
        var returnsTask = AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.ConfigureMarten(opts => opts.Connection(_returnsPostgres.GetConnectionString()));
                services.ConfigureWolverine(opts =>
                {
                    opts.UseRabbitMq(rabbitMqUri);
                    opts.Policies.AutoApplyTransactions();
                    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

                    // IMPORTANT: Include Returns domain assembly for handler discovery
                    opts.Discovery.IncludeAssembly(typeof(Returns.Return).Assembly);
                });
            });
        });

        var ordersTask = AlbaHost.For<OrdersApi::Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.ConfigureMarten(opts => opts.Connection(_ordersPostgres.GetConnectionString()));
                services.ConfigureWolverine(opts =>
                {
                    opts.UseRabbitMq(rabbitMqUri);
                    opts.Policies.AutoApplyTransactions();
                    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

                    // IMPORTANT: Include Orders domain assembly for saga discovery
                    // Without this, Wolverine won't find the Order saga and will throw UnknownSagaException
                    // Use reflection to find the Orders assembly since we're using extern aliases
                    var ordersAssembly = typeof(OrdersApi::Program).Assembly
                        .GetReferencedAssemblies()
                        .Select(Assembly.Load)
                        .First(a => a.GetName().Name == "Orders");
                    opts.Discovery.IncludeAssembly(ordersAssembly);
                });
            });
        });

        var fulfillmentTask = AlbaHost.For<FulfillmentApi::Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.ConfigureMarten(opts => opts.Connection(_fulfillmentPostgres.GetConnectionString()));
                services.ConfigureWolverine(opts =>
                {
                    opts.UseRabbitMq(rabbitMqUri);
                    opts.Policies.AutoApplyTransactions();
                    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

                    // IMPORTANT: Include Fulfillment domain assembly for handler discovery
                    // Use reflection to find the Fulfillment assembly since we're using extern aliases
                    var fulfillmentAssembly = typeof(FulfillmentApi::Program).Assembly
                        .GetReferencedAssemblies()
                        .Select(Assembly.Load)
                        .First(a => a.GetName().Name == "Fulfillment");
                    opts.Discovery.IncludeAssembly(fulfillmentAssembly);
                });
            });
        });

        var hosts = await Task.WhenAll(returnsTask, ordersTask, fulfillmentTask);

        ReturnsHost = hosts[0];
        OrdersHost = hosts[1];
        FulfillmentHost = hosts[2];
    }

    public async Task DisposeAsync()
    {
        // Dispose hosts first
        var hostDisposalTasks = new List<Task>();

        if (ReturnsHost != null)
            hostDisposalTasks.Add(SafeDisposeHost(ReturnsHost));

        if (OrdersHost != null)
            hostDisposalTasks.Add(SafeDisposeHost(OrdersHost));

        if (FulfillmentHost != null)
            hostDisposalTasks.Add(SafeDisposeHost(FulfillmentHost));

        await Task.WhenAll(hostDisposalTasks);

        // Dispose containers
        await Task.WhenAll(
            _rabbitMq.DisposeAsync().AsTask(),
            _returnsPostgres.DisposeAsync().AsTask(),
            _ordersPostgres.DisposeAsync().AsTask(),
            _fulfillmentPostgres.DisposeAsync().AsTask()
        );
    }

    private static async Task SafeDisposeHost(IAlbaHost host)
    {
        try
        {
            await host.StopAsync();
            await host.DisposeAsync();
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

    /// <summary>
    /// Cleans all document and event data from all 3 databases.
    /// Use between tests that need full isolation.
    /// </summary>
    public async Task CleanAllDataAsync()
    {
        await Task.WhenAll(
            CleanDatabaseAsync(ReturnsHost),
            CleanDatabaseAsync(OrdersHost),
            CleanDatabaseAsync(FulfillmentHost)
        );
    }

    private static async Task CleanDatabaseAsync(IAlbaHost host)
    {
        var store = host.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();
        await store.Advanced.Clean.DeleteAllEventDataAsync();
    }

    /// <summary>
    /// Gets a Marten document session for the Returns BC.
    /// Caller is responsible for disposing the session.
    /// </summary>
    public IDocumentSession GetReturnsSession()
    {
        return ReturnsHost.Services.GetRequiredService<IDocumentStore>().LightweightSession();
    }

    /// <summary>
    /// Gets a Marten document session for the Orders BC.
    /// Caller is responsible for disposing the session.
    /// </summary>
    public IDocumentSession GetOrdersSession()
    {
        return OrdersHost.Services.GetRequiredService<IDocumentStore>().LightweightSession();
    }

    /// <summary>
    /// Gets a Marten document session for the Fulfillment BC.
    /// Caller is responsible for disposing the session.
    /// </summary>
    public IDocumentSession GetFulfillmentSession()
    {
        return FulfillmentHost.Services.GetRequiredService<IDocumentStore>().LightweightSession();
    }

    /// <summary>
    /// Executes a message through Wolverine on the Returns host and waits for all cascading messages.
    /// Tracks activity across all 3 hosts to detect cross-BC message flows.
    /// </summary>
    public async Task<ITrackedSession> ExecuteAndWaitAsync<T>(T message, int timeoutSeconds = 30)
        where T : class
    {
        // Track activity across all 3 hosts to capture cross-BC message flows
        return await ReturnsHost.TrackActivity(TimeSpan.FromSeconds(timeoutSeconds))
            .DoNotAssertOnExceptionsDetected()
            .AlsoTrack(ReturnsHost, OrdersHost, FulfillmentHost)
            .ExecuteAndWaitAsync(ctx =>
            {
                return ctx.InvokeAsync(message);
            });
    }

    /// <summary>
    /// Invokes a message on a specific host and waits for all cascading messages across all 3 BCs.
    /// Use this when you need to start a workflow from a specific bounded context.
    /// </summary>
    public async Task<ITrackedSession> ExecuteOnHostAndWaitAsync<T>(
        IAlbaHost host,
        T message,
        int timeoutSeconds = 30)
        where T : class
    {
        return await host.TrackActivity(TimeSpan.FromSeconds(timeoutSeconds))
            .DoNotAssertOnExceptionsDetected()
            .AlsoTrack(ReturnsHost, OrdersHost, FulfillmentHost)
            .ExecuteAndWaitAsync(ctx =>
            {
                return ctx.InvokeAsync(message);
            });
    }
}
