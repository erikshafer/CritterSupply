using System.Security.Claims;
using System.Text.Encodings.Web;
using JasperFx.CommandLine;
using Marten;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Tracking;

namespace Fulfillment.Api.IntegrationTests;

/// <summary>
/// Test fixture providing PostgreSQL via TestContainers and Alba host for integration tests.
/// Uses collection fixture pattern to ensure sequential test execution and proper resource sharing.
/// </summary>
public class TestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("fulfillment_test_db")
        .WithName($"fulfillment-postgres-test-{Guid.NewGuid():N}")
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
                services.ConfigureMarten(opts =>
                {
                    opts.Connection(_connectionString);
                });

                // Disable external Wolverine transports for testing
                services.DisableAllExternalWolverineTransports();

                // Replace authentication services with test authentication that bypasses JWT validation
                // Remove existing authentication services
                var authServices = services.Where(s =>
                    s.ServiceType.Namespace == "Microsoft.AspNetCore.Authentication" ||
                    s.ServiceType.FullName?.Contains("Authentication") == true)
                    .ToList();
                foreach (var service in authServices)
                {
                    services.Remove(service);
                }

                // Add test authentication scheme using "Backoffice" scheme name to satisfy policy requirements
                services.AddAuthentication("Backoffice")
                    .AddScheme<AuthenticationSchemeOptions, CustomerServiceAuthHandler>(
                        "Backoffice", _ => { });

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
}

/// <summary>
/// Fake authentication handler used in integration tests.
/// Automatically authenticates every request as a CustomerService user, satisfying the "CustomerService"
/// authorization policy without requiring a real JWT. This keeps test code clean while
/// still exercising the full authorization middleware pipeline.
/// Registered as "Backoffice" scheme to match the policy configuration in Program.cs.
/// </summary>
internal sealed class CustomerServiceAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public CustomerServiceAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "test-cs-agent"),
            new Claim(ClaimTypes.Role, "CustomerService"),
        };
        var identity = new ClaimsIdentity(claims, "Backoffice");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Backoffice");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
