using System.Security.Claims;
using System.Text.Encodings.Web;
using Backoffice.Clients;
using JasperFx.CommandLine;
using Marten;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Tracking;

namespace Backoffice.Api.IntegrationTests;

/// <summary>
/// Test fixture providing PostgreSQL via TestContainers and Alba host for integration tests.
/// Uses collection fixture pattern to ensure sequential test execution and proper resource sharing.
/// Replaces domain BC HTTP clients with stubs for isolated testing.
/// </summary>
public class BackofficeTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("backoffice_test_db")
        .WithName($"backoffice-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private string? _connectionString;

    public IAlbaHost Host { get; private set; } = null!;

    // Stub clients for testing
    public StubCustomerIdentityClient CustomerIdentityClient { get; private set; } = null!;
    public StubOrdersClient OrdersClient { get; private set; } = null!;
    public StubReturnsClient ReturnsClient { get; private set; } = null!;
    public StubCorrespondenceClient CorrespondenceClient { get; private set; } = null!;

    /// <summary>
    /// Stable admin user ID for test authentication.
    /// Used consistently across all requests in a test run to allow edit/delete authorization checks.
    /// </summary>
    public static readonly Guid TestAdminUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _connectionString = _postgres.GetConnectionString();

        // Initialize stub clients
        CustomerIdentityClient = new StubCustomerIdentityClient();
        OrdersClient = new StubOrdersClient();
        ReturnsClient = new StubReturnsClient();
        CorrespondenceClient = new StubCorrespondenceClient();

        // Necessary for WebApplicationFactory usage with Alba for integration testing
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

                // Disable external Wolverine transports for testing
                services.DisableAllExternalWolverineTransports();

                // Replace domain BC HTTP clients with stubs
                var clientTypes = new[]
                {
                    typeof(ICustomerIdentityClient),
                    typeof(IOrdersClient),
                    typeof(IReturnsClient),
                    typeof(ICorrespondenceClient),
                    typeof(IInventoryClient),
                    typeof(IFulfillmentClient),
                    typeof(ICatalogClient)
                };

                foreach (var clientType in clientTypes)
                {
                    var descriptor = services.FirstOrDefault(s => s.ServiceType == clientType);
                    if (descriptor != null)
                        services.Remove(descriptor);
                }

                // Register stub clients
                services.AddScoped<ICustomerIdentityClient>(_ => CustomerIdentityClient);
                services.AddScoped<IOrdersClient>(_ => OrdersClient);
                services.AddScoped<IReturnsClient>(_ => ReturnsClient);
                services.AddScoped<ICorrespondenceClient>(_ => CorrespondenceClient);
                services.AddScoped<IInventoryClient>(_ => new StubInventoryClient());
                services.AddScoped<IFulfillmentClient>(_ => new StubFulfillmentClient());
                services.AddScoped<ICatalogClient>(_ => new StubCatalogClient());
                services.AddScoped<IPricingClient>(_ => new StubPricingClient());

                // Replace authentication with test authentication
                var authServices = services.Where(s =>
                    s.ServiceType.Namespace == "Microsoft.AspNetCore.Authentication" ||
                    s.ServiceType.FullName?.Contains("Authentication") == true)
                    .ToList();
                foreach (var service in authServices)
                {
                    services.Remove(service);
                }

                // Register test auth context as singleton (must be before authentication setup)
                services.AddSingleton<ITestAuthContext>(new TestAuthContext(TestAdminUserId));

                // Add test authentication scheme with stable admin user ID
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

                // Add authorization with policies (must be after authentication)
                services.AddAuthorization();
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
            catch (ObjectDisposedException) { }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
                e is OperationCanceledException or ObjectDisposedException)) { }
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
    /// Cleans all document data from the database.
    /// </summary>
    public async Task CleanAllDocumentsAsync()
    {
        var store = Host.Services.GetRequiredService<IDocumentStore>();
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
}

/// <summary>
/// Context for test authentication providing stable user identity.
/// </summary>
internal interface ITestAuthContext
{
    Guid AdminUserId { get; }
}

/// <summary>
/// Implementation of test authentication context.
/// </summary>
internal sealed class TestAuthContext : ITestAuthContext
{
    public Guid AdminUserId { get; }

    public TestAuthContext(Guid adminUserId)
    {
        AdminUserId = adminUserId;
    }
}

/// <summary>
/// Fake authentication handler for integration tests.
/// Automatically authenticates as CustomerService user with cs-agent role.
/// Uses stable admin user ID from ITestAuthContext to allow authorization checks across requests.
/// </summary>
internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ITestAuthContext _authContext;

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ITestAuthContext authContext)
        : base(options, logger, encoder)
    {
        _authContext = authContext;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var adminUserId = _authContext.AdminUserId.ToString();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, adminUserId),
            new Claim("sub", adminUserId),
            new Claim(ClaimTypes.Name, "test-admin"),
            new Claim(ClaimTypes.Role, "cs-agent"),
            new Claim(ClaimTypes.Role, "warehouse-clerk"),
            new Claim(ClaimTypes.Role, "operations-manager"),
            new Claim(ClaimTypes.Role, "executive"),
            new Claim(ClaimTypes.Role, "system-admin"),
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
